using System.Collections.Generic;
using SmartAmbientMatter.Models;

namespace SmartAmbientMatter
{
    /// <summary>
    /// All user-configurable settings for AmbiMatter, serialized to SimHub's settings store.
    /// Loaded with ReadCommonSettings / saved with SaveCommonSettings.
    /// </summary>
    public class AmbiMatterSettings
    {
        // ── Connection ─────────────────────────────────────────────────────────
        /// <summary>IP address of the Python Matter bridge.</summary>
        public string BridgeHost { get; set; } = "127.0.0.1";

        /// <summary>UDP port the Python bridge listens on.</summary>
        public int BridgePort { get; set; } = 10001;

        // ── Screen Capture ─────────────────────────────────────────────────────

        /// <summary>Monitor to capture from. 0 = primary monitor.</summary>
        public int MonitorIndex      { get; set; } = 0;

        /// <summary>Capture region X offset within the selected monitor. 0 = monitor left edge.</summary>
        public int CaptureX          { get; set; } = 0;

        /// <summary>Capture region Y offset within the selected monitor. 0 = monitor top edge.</summary>
        public int CaptureY          { get; set; } = 0;

        /// <summary>Capture region width in pixels. 0 = full monitor width.</summary>
        public int CaptureWidth      { get; set; } = 0;

        /// <summary>Capture region height in pixels. 0 = full monitor height.</summary>
        public int CaptureHeight     { get; set; } = 0;

        /// <summary>Sample every Nth pixel (higher = faster, less accurate).</summary>
        public int SampleStep        { get; set; } = 20;

        /// <summary>Minimum milliseconds between screen captures (100 = 10 Hz).</summary>
        public int CaptureIntervalMs { get; set; } = 100;

        /// <summary>Minimum bulb brightness (0-254). Prevents the room going fully dark.</summary>
        public int BrightnessMin     { get; set; } = 10;

        /// <summary>Maximum bulb brightness (0-254). Ceiling for full-white screens.</summary>
        public int BrightnessMax     { get; set; } = 254;

        /// <summary>Minimum color temperature sent to bulbs (K). Set to your bulbs' lower limit.</summary>
        public int KelvinMin         { get; set; } = 2700;

        /// <summary>Maximum color temperature sent to bulbs (K). Set to your bulbs' upper limit.</summary>
        public int KelvinMax         { get; set; } = 6500;

        /// <summary>
        /// EMA smoothing factor (0.05=very smooth, 0.50=barely smoothed).
        /// Controls how quickly the output tracks screen changes.
        /// Lower values dampen HUD flicker better but make transitions slower.
        /// </summary>
        public double SmoothingAlpha { get; set; } = 0.15;

        // ── Rate Limiting ───────────────────────────────────────────────────────
        /// <summary>
        /// Minimum milliseconds between commands per zone (the "Matter Wall").
        /// Increase to 2000 if bulbs disconnect under load.
        /// </summary>
        public int MinSleepMs { get; set; } = 1500;

        // ── Intensity Scoring ──────────────────────────────────────────────────
        /// <summary>
        /// Minimum change magnitude (0.0–1.0) needed to trigger a lighting update.
        /// Computed as Max(deltaBrightness, deltaKelvin) normalized to 0-1.
        /// Higher = fewer updates. Lower = more reactive.
        /// </summary>
        public double IntensityThreshold { get; set; } = 0.02;

        // ── Guillotine / Tunnel Detection ──────────────────────────────────────
        /// <summary>
        /// Brightness drop (0-255 scale) in a single frame that triggers Guillotine check.
        /// Lower = more sensitive to sudden darkness (risk of false triggers in heavy weather).
        /// </summary>
        public int TunnelBrightDrop { get; set; } = 50;

        /// <summary>
        /// Maximum brightness (0-255) that qualifies as "dark enough" to be in a tunnel.
        /// Lower = stricter (only triggers in near-darkness).
        /// </summary>
        public int TunnelBrightFloor { get; set; } = 60;

        // ── Zones ──────────────────────────────────────────────────────────────
        /// <summary>
        /// Named groups of bulbs. Each zone gets identical Kelvin/Brightness (after multiplier).
        /// Defaults to a single "ceiling" zone with node IDs 1, 2, 3.
        /// </summary>
        public List<Zone> Zones { get; set; } = new List<Zone>
        {
            new Zone("ceiling", new List<int> { 1, 2, 3 }, 1.0)
        };
    }
}
