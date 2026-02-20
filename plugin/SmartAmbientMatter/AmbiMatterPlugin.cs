using System;
using System.Windows.Media;
using GameReaderCommon;
using SimHub.Plugins;
using SmartAmbientMatter.Models;
using SmartAmbientMatter.Settings;

namespace SmartAmbientMatter
{
    /// <summary>
    /// Main SimHub plugin class for AmbiMatter.
    /// Captures the game screen, derives Kelvin + Brightness from the average pixel color,
    /// and sends zone-aware UDP commands to the Python Matter bridge.
    /// </summary>
    [PluginDescription("Screen capture ambient lighting for Matter smart bulbs")]
    [PluginAuthor("Andrew Kuehne")]
    [PluginName("AmbiMatter")]
    public class AmbiMatterPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        // ── Public state (accessible to settings UI) ───────────────────────────
        public AmbiMatterSettings Settings { get; private set; }
        public PluginManager PluginManager { get; set; }

        // ── Core components ────────────────────────────────────────────────────
        private ScreenCaptureEngine   _screenCapture;
        private readonly ZoneManager         _zoneManager         = new ZoneManager();
        private readonly TransitionCalculator _transitionCalc      = new TransitionCalculator();
        private UdpSender _udpSender;

        // ── Per-frame tracking ─────────────────────────────────────────────────
        /// <summary>Brightness from the previous DataUpdate frame. Used by Guillotine check.</summary>
        private int _prevComputedBrightness = -1;

        /// <summary>Last computed LightingState. Exposed to settings UI via delegate.</summary>
        private LightingState _lastComputed;

        /// <summary>Whether the game was running last DataUpdate. Exposed to UI.</summary>
        private bool _gameRunning;

        // ── IPlugin ────────────────────────────────────────────────────────────

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;

            SimHub.Logging.Current.Info("AmbiMatter: Initializing");

            Settings = this.ReadCommonSettings<AmbiMatterSettings>(
                "GeneralSettings",
                () => new AmbiMatterSettings());

            _screenCapture = new ScreenCaptureEngine(Settings);
            _zoneManager.Initialize(Settings.Zones);
            _udpSender = new UdpSender();

            // Expose live state as SimHub properties (available in overlays/dashboards)
            this.AttachDelegate("AmbiMatter.CurrentKelvin",    () => _lastComputed?.Kelvin ?? 0);
            this.AttachDelegate("AmbiMatter.CurrentBrightness", () => _lastComputed?.Brightness ?? 0);
            this.AttachDelegate("AmbiMatter.GameRunning",       () => _gameRunning);
            this.AttachDelegate("AmbiMatter.AvgR",              () => _screenCapture?.LastAvgRgb.R ?? 0);
            this.AttachDelegate("AmbiMatter.AvgG",              () => _screenCapture?.LastAvgRgb.G ?? 0);
            this.AttachDelegate("AmbiMatter.AvgB",              () => _screenCapture?.LastAvgRgb.B ?? 0);

            SimHub.Logging.Current.Info(
                $"AmbiMatter: Ready — {Settings.Zones.Count} zone(s), " +
                $"bridge={Settings.BridgeHost}:{Settings.BridgePort}");
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            _gameRunning = data.GameRunning;

            if (!data.GameRunning)
                return;

            // ── Screen capture → LightingState ────────────────────────────────
            LightingState computed = _screenCapture.Capture();

            // ── Layer 3a: Guillotine check ─────────────────────────────────────
            // _prevComputedBrightness == -1 on first frame; treat as no guillotine
            bool guillotine = (_prevComputedBrightness >= 0) &&
                              _transitionCalc.CheckGuillotine(
                                  computed.Brightness,
                                  _prevComputedBrightness,
                                  Settings.TunnelBrightDrop,
                                  Settings.TunnelBrightFloor);

            if (guillotine)
                SimHub.Logging.Current.Info(
                    $"AmbiMatter: Guillotine! prev={_prevComputedBrightness} " +
                    $"curr={computed.Brightness}");

            _prevComputedBrightness = computed.Brightness;
            _lastComputed = computed;

            // ── Per-zone dispatch ──────────────────────────────────────────────
            foreach (var zone in Settings.Zones)
            {
                var zoneState = _zoneManager.GetState(zone.Name);
                if (zoneState == null) continue;

                // Cooldown check — guillotine bypasses it
                if (zoneState.IsOnCooldown(Settings.MinSleepMs) && !guillotine)
                    continue;

                // Intensity check — first send always goes through (LastSent == null)
                double intensity = _transitionCalc.CalculateIntensity(computed, zoneState.LastSent);
                if (!guillotine && intensity < Settings.IntensityThreshold)
                    continue;

                // Apply zone brightness multiplier and clamp to 0-254
                int finalBrightness = Clamp(
                    (int)(computed.Brightness * zone.BrightnessMultiplier), 0, 254);

                int transition = _transitionCalc.GetTransition(intensity, guillotine);

                var final = new LightingState(computed.Kelvin, finalBrightness, transition);

                try
                {
                    _udpSender.Send(Settings.BridgeHost, Settings.BridgePort, zone.Name, final);
                    SimHub.Logging.Current.Debug(
                        $"AmbiMatter: [{zone.Name}] Sent {final} (intensity={intensity:F3})");
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn(
                        $"AmbiMatter: UDP send failed for zone '{zone.Name}': {ex.Message}");
                }

                zoneState.RecordSend(final);
            }
        }

        public void End(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("AmbiMatter: Shutting down");
            this.SaveCommonSettings("GeneralSettings", Settings);
            _screenCapture?.Dispose();
            _udpSender?.Dispose();
        }

        // ── IWPFSettingsV2 ─────────────────────────────────────────────────────

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this);
        }

        public ImageSource PictureIcon => null;

        public string LeftMenuTitle => "AmbiMatter";

        // ── Settings helpers ───────────────────────────────────────────────────

        /// <summary>Saves settings, re-initializes zone manager, and updates capture engine. Called by settings UI.</summary>
        public void SaveSettings()
        {
            this.SaveCommonSettings("GeneralSettings", Settings);
            _zoneManager.Initialize(Settings.Zones);
            _screenCapture.UpdateSettings(Settings);
            SimHub.Logging.Current.Info(
                $"AmbiMatter: Settings saved, {Settings.Zones.Count} zone(s) reloaded");
        }

        // ── Exposed to settings UI ─────────────────────────────────────────────

        /// <summary>Exposes ZoneManager for settings UI status panel.</summary>
        public ZoneManager ZoneManager => _zoneManager;

        /// <summary>Last computed state from screen capture. Read by settings UI status panel.</summary>
        public LightingState LastComputed => _lastComputed;

        /// <summary>Whether the game was running on the last DataUpdate. Read by settings UI.</summary>
        public bool GameRunning => _gameRunning;

        /// <summary>Exposes ScreenCaptureEngine for settings UI RGB readout.</summary>
        public ScreenCaptureEngine ScreenCapture => _screenCapture;

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;
    }
}
