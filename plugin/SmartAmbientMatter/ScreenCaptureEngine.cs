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
                return _cached ?? new LightingState(_settings.BrightnessMin > 0 ? 4600 : 4600,
                                                    _settings.BrightnessMin, 0);

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

            // ── Kelvin from warmth ratio ───────────────────────────────────────
            // warmth: -1.0 = pure blue (cool), 0.0 = neutral, +1.0 = pure red (warm)
            double warmth = (avgR - avgB) / 255.0;
            int    kelvin = Clamp((int)(4600 - warmth * 1900), 2700, 6500);

            // ── Brightness from relative luminance ────────────────────────────
            // Standard Rec.709 luminance coefficients, output 0-255
            double luminance = 0.2126 * avgR + 0.7152 * avgG + 0.0722 * avgB;
            double t         = luminance / 255.0;
            int    brightness = Clamp(
                (int)(_settings.BrightnessMin + t * (_settings.BrightnessMax - _settings.BrightnessMin)),
                0, 254);

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

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;
    }
}
