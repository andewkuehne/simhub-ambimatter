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
    /// Reads iRacing telemetry, runs the three-layer atmospheric model,
    /// and sends zone-aware UDP commands to the Python Matter bridge.
    /// </summary>
    [PluginDescription("Atmospheric ambient lighting for Matter smart bulbs")]
    [PluginAuthor("Andrew Kuehne")]
    [PluginName("AmbiMatter")]
    public class AmbiMatterPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        // ── Public state (accessible to settings UI) ───────────────────────────
        public AmbiMatterSettings Settings { get; private set; }
        public PluginManager PluginManager { get; set; }

        // ── Core components ────────────────────────────────────────────────────
        private readonly AtmosphereEngine    _atmosphereEngine    = new AtmosphereEngine();
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

            _zoneManager.Initialize(Settings.Zones);
            _udpSender = new UdpSender();

            // Expose live state as SimHub properties (available in overlays/dashboards)
            this.AttachDelegate("AmbiMatter.CurrentKelvin",    () => _lastComputed?.Kelvin ?? 0);
            this.AttachDelegate("AmbiMatter.CurrentBrightness", () => _lastComputed?.Brightness ?? 0);
            this.AttachDelegate("AmbiMatter.GameRunning",       () => _gameRunning);

            SimHub.Logging.Current.Info(
                $"AmbiMatter: Ready — {Settings.Zones.Count} zone(s), " +
                $"bridge={Settings.BridgeHost}:{Settings.BridgePort}");
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            _gameRunning = data.GameRunning;

            if (!data.GameRunning)
                return;

            // ── Layer 1+2: Atmospheric calculation ────────────────────────────
            double timeSec = GetSessionTimeOfDaySec(pluginManager);
            LightingState computed = _atmosphereEngine.Calculate(timeSec);

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
            _udpSender?.Dispose();
        }

        // ── IWPFSettingsV2 ─────────────────────────────────────────────────────

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this);
        }

        public ImageSource PictureIcon => null;

        public string LeftMenuTitle => "AmbiMatter";

        // ── Internal helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Reads iRacing's SessionTimeOfDay (seconds from midnight).
        /// Falls back to noon (43200s) if the property is unavailable.
        /// Never throws — missing telemetry should never crash the plugin.
        /// </summary>
        private double GetSessionTimeOfDaySec(PluginManager pluginManager)
        {
            try
            {
                var raw = pluginManager.GetPropertyValue(
                    "DataCorePlugin.GameRawData.SessionTimeOfDay");
                if (raw != null)
                    return Convert.ToDouble(raw);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Debug(
                    $"AmbiMatter: SessionTimeOfDay unavailable ({ex.Message}), using noon");
            }
            return 43200.0;  // fallback: noon
        }

        /// <summary>Saves settings and re-initializes zone manager. Called by settings UI.</summary>
        public void SaveSettings()
        {
            this.SaveCommonSettings("GeneralSettings", Settings);
            _zoneManager.Initialize(Settings.Zones);
            SimHub.Logging.Current.Info(
                $"AmbiMatter: Settings saved, {Settings.Zones.Count} zone(s) reloaded");
        }

        /// <summary>Exposes ZoneManager for settings UI status panel.</summary>
        public ZoneManager ZoneManager => _zoneManager;

        /// <summary>Last computed atmospheric state. Read by settings UI status panel.</summary>
        public LightingState LastComputed => _lastComputed;

        /// <summary>Whether the game was running on the last DataUpdate. Read by settings UI.</summary>
        public bool GameRunning => _gameRunning;

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;
    }
}
