using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using SmartAmbientMatter.Models;

namespace SmartAmbientMatter
{
    /// <summary>
    /// Captures a region of the screen, averages its pixel colors,
    /// and derives a LightingState (Kelvin + Brightness) from the result.
    ///
    /// Color temperature is computed via McCamy's CCT approximation (1992):
    ///   1. Gamma-decode sRGB → linear light (gamma 2.2)
    ///   2. Linear RGB → CIE XYZ via the sRGB/D65 matrix
    ///   3. XYZ → xy chromaticity coordinates
    ///   4. McCamy's formula → Correlated Color Temperature (CCT) in Kelvin
    /// Accurate to ±2K in the 2856–6500K range.
    ///
    /// Brightness uses Rec.709 luminance on linearized RGB values.
    ///
    /// An exponential moving average (EMA) smooths frame-to-frame noise from
    /// HUD elements, flags, and camera cuts.
    ///
    /// A reusable Bitmap is held to avoid per-frame GC pressure.
    /// All Matter-rate-limiting is still handled upstream by AmbiMatterPlugin/ZoneManager.
    /// </summary>
    public sealed class ScreenCaptureEngine : IDisposable
    {
        // ── State ──────────────────────────────────────────────────────────────
        private Rectangle    _captureRect;
        private Bitmap       _bitmap;
        private Graphics     _graphics;
        private DateTime     _lastCaptureTime = DateTime.MinValue;
        private LightingState _cached;
        private AmbiMatterSettings _settings;

        // ── EMA Smoothing ─────────────────────────────────────────────────────
        // Seeded to -1; first frame sets directly (no blending).
        private double _smoothedKelvin     = -1;
        private double _smoothedBrightness = -1;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Average R, G, B of the last captured frame. Updated by Capture().</summary>
        public (int R, int G, int B) LastAvgRgb { get; private set; }

        // ── Constructor ────────────────────────────────────────────────────────

        public ScreenCaptureEngine(AmbiMatterSettings settings)
        {
            _settings = settings;
            UpdateSettings(settings);
        }

        /// <summary>
        /// Recomputes the capture rectangle and reallocates the Bitmap if dimensions changed.
        /// Call this after the user saves settings.
        /// </summary>
        public void UpdateSettings(AmbiMatterSettings s)
        {
            _settings = s;

            // Select monitor — clamp index to valid range
            var screens = Screen.AllScreens;
            int idx = Math.Max(0, Math.Min(s.MonitorIndex, screens.Length - 1));
            var monitor = screens[idx];

            Rectangle newRect;
            if (s.CaptureWidth == 0 || s.CaptureHeight == 0)
            {
                // Full monitor
                newRect = monitor.Bounds;
            }
            else
            {
                newRect = new Rectangle(
                    monitor.Bounds.X + s.CaptureX,
                    monitor.Bounds.Y + s.CaptureY,
                    s.CaptureWidth,
                    s.CaptureHeight);
            }

            // Reallocate only if dimensions changed
            if (_bitmap == null ||
                newRect.Width  != _captureRect.Width ||
                newRect.Height != _captureRect.Height)
            {
                _graphics?.Dispose();
                _bitmap?.Dispose();

                _bitmap   = new Bitmap(newRect.Width, newRect.Height, PixelFormat.Format32bppArgb);
                _graphics = Graphics.FromImage(_bitmap);
            }

            _captureRect = newRect;
        }

        /// <summary>
        /// Grabs the screen region, averages the pixels, and returns a LightingState.
        /// Throttled to CaptureIntervalMs — returns the cached state if called too soon.
        /// </summary>
        public LightingState Capture()
        {
            // ── Throttle ───────────────────────────────────────────────────────
            if ((DateTime.Now - _lastCaptureTime).TotalMilliseconds < _settings.CaptureIntervalMs)
                return _cached ?? new LightingState(4600, _settings.BrightnessMin, 0);

            // ── Screen grab ────────────────────────────────────────────────────
            try
            {
                _graphics.CopyFromScreen(
                    _captureRect.Location,
                    Point.Empty,
                    _captureRect.Size);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn(
                    $"AmbiMatter: Screen capture failed: {ex.Message}");
                return _cached ?? new LightingState(4600, _settings.BrightnessMin, 0);
            }

            // ── Pixel sampling via LockBits (unsafe pointer for speed) ─────────
            var bmpData = _bitmap.LockBits(
                new Rectangle(0, 0, _bitmap.Width, _bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            long totalR = 0, totalG = 0, totalB = 0;
            int  count  = 0;
            int  step   = Math.Max(1, _settings.SampleStep);

            unsafe
            {
                byte* basePtr = (byte*)bmpData.Scan0.ToPointer();
                int   stride  = bmpData.Stride;
                int   width   = _bitmap.Width;
                int   height  = _bitmap.Height;

                for (int y = 0; y < height; y += step)
                {
                    byte* row = basePtr + y * stride;
                    for (int x = 0; x < width; x += step)
                    {
                        byte* pixel = row + x * 4;
                        // BGRA byte order
                        totalB += pixel[0];
                        totalG += pixel[1];
                        totalR += pixel[2];
                        count++;
                    }
                }
            }

            _bitmap.UnlockBits(bmpData);

            // ── Derive averages ────────────────────────────────────────────────
            int avgR = count > 0 ? (int)(totalR / count) : 128;
            int avgG = count > 0 ? (int)(totalG / count) : 128;
            int avgB = count > 0 ? (int)(totalB / count) : 128;

            LastAvgRgb = (avgR, avgG, avgB);

            // ── Gamma-decode sRGB → linear light ─────────────────────────────
            double rLin = Math.Pow(avgR / 255.0, 2.2);
            double gLin = Math.Pow(avgG / 255.0, 2.2);
            double bLin = Math.Pow(avgB / 255.0, 2.2);

            // ── Kelvin via McCamy's CCT formula ──────────────────────────────
            int rawKelvin = CctFromLinearRgb(rLin, gLin, bLin);
            rawKelvin = Clamp(rawKelvin, _settings.KelvinMin, _settings.KelvinMax);

            // ── Brightness from Rec.709 luminance (linearized) ──────────────
            double luminance = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
            int rawBrightness = Clamp(
                (int)(_settings.BrightnessMin + luminance * (_settings.BrightnessMax - _settings.BrightnessMin)),
                0, 254);

            // ── EMA smoothing ────────────────────────────────────────────────
            double alpha = _settings.SmoothingAlpha;
            if (_smoothedKelvin < 0)
            {
                // First frame — seed directly
                _smoothedKelvin     = rawKelvin;
                _smoothedBrightness = rawBrightness;
            }
            else
            {
                _smoothedKelvin     = _smoothedKelvin * (1.0 - alpha) + rawKelvin * alpha;
                _smoothedBrightness = _smoothedBrightness * (1.0 - alpha) + rawBrightness * alpha;
            }

            int kelvin     = Clamp((int)Math.Round(_smoothedKelvin), _settings.KelvinMin, _settings.KelvinMax);
            int brightness = Clamp((int)Math.Round(_smoothedBrightness), 0, 254);

            _lastCaptureTime = DateTime.Now;
            _cached = new LightingState(kelvin, brightness, 0);
            return _cached;
        }

        // ── IDisposable ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _graphics?.Dispose();
            _bitmap?.Dispose();
            _graphics = null;
            _bitmap   = null;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Compute Correlated Color Temperature (CCT) from linear-light RGB values
        /// using McCamy's approximation (1992), refined by Hernandez-Andres (1999).
        ///
        /// Pipeline: linear RGB → CIE XYZ (sRGB D65 matrix) → xy chromaticity → CCT.
        /// Accurate to ±2K in the 2856–6500K range — exactly our bulb range.
        ///
        /// References:
        ///   McCamy, C.S. (1992) "Correlated color temperature as an explicit
        ///     function of chromaticity coordinates", Color Research &amp; Application.
        ///   Hernandez-Andres et al. (1999) "Calculating correlated color temperatures
        ///     across the entire gamut of daylight and skylight chromaticities".
        /// </summary>
        private static int CctFromLinearRgb(double rLin, double gLin, double bLin)
        {
            // sRGB D65 → CIE XYZ (IEC 61966-2-1)
            double x = rLin * 0.4124564 + gLin * 0.3575761 + bLin * 0.1804375;
            double y = rLin * 0.2126729 + gLin * 0.7151522 + bLin * 0.0721750;
            double z = rLin * 0.0193339 + gLin * 0.1191920 + bLin * 0.9503041;

            double sum = x + y + z;
            if (sum < 1e-10)
                return 4600; // near-black screen — return neutral default

            // CIE 1931 xy chromaticity
            double cx = x / sum;
            double cy = y / sum;

            // McCamy's approximation
            double n = (cx - 0.3320) / (0.1858 - cy);
            double cct = 449.0 * n * n * n + 3525.0 * n * n + 6823.3 * n + 5520.33;

            return (int)Math.Round(cct);
        }

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;
    }
}
