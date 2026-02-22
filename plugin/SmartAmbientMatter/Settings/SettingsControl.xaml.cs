using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SmartAmbientMatter.Models;

namespace SmartAmbientMatter.Settings
{
    /// <summary>
    /// Code-behind for the AmbiMatter settings panel.
    /// Reads from / writes to AmbiMatterPlugin.Settings.
    /// A 500ms DispatcherTimer refreshes the live status panel.
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        private readonly AmbiMatterPlugin _plugin;
        private readonly DispatcherTimer  _statusTimer;
        private bool _loading = false;  // suppress change events during initial load

        public SettingsControl(AmbiMatterPlugin plugin)
        {
            _plugin = plugin;
            InitializeComponent();

            LoadSettings();

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();

            Unloaded += (_, __) => _statusTimer.Stop();
        }

        // ── Load / Save ────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            _loading = true;

            var s = _plugin.Settings;

            TxtBridgeHost.Text = s.BridgeHost;
            TxtBridgePort.Text = s.BridgePort.ToString();

            // Screen capture fields
            TxtMonitorIndex.Text   = s.MonitorIndex.ToString();
            TxtCapX.Text           = s.CaptureX.ToString();
            TxtCapY.Text           = s.CaptureY.ToString();
            TxtCapW.Text           = s.CaptureWidth.ToString();
            TxtCapH.Text           = s.CaptureHeight.ToString();
            SliderSampleStep.Value = s.SampleStep;
            SliderCaptureMs.Value  = s.CaptureIntervalMs;
            SliderBrightMin.Value  = s.BrightnessMin;
            SliderBrightMax.Value  = s.BrightnessMax;
            SliderKelvinMin.Value  = s.KelvinMin;
            SliderKelvinMax.Value  = s.KelvinMax;
            SliderSmoothing.Value  = s.SmoothingAlpha;

            SliderIntensity.Value    = s.IntensityThreshold;
            SliderSleep.Value        = s.MinSleepMs;
            SliderTunnelDrop.Value   = s.TunnelBrightDrop;
            SliderTunnelFloor.Value  = s.TunnelBrightFloor;

            UpdateSliderLabels();

            // Bind the zone list (shallow copy so Cancel doesn't dirty the model)
            ZoneListBox.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<Zone>(s.Zones);

            // Populate NodeIds text boxes (DataTemplate binding doesn't know List<int>→string)
            RefreshNodeIdTextBoxes();

            _loading = false;
        }

        private void SaveToModel()
        {
            var s = _plugin.Settings;

            s.BridgeHost = TxtBridgeHost.Text?.Trim() ?? "127.0.0.1";

            if (int.TryParse(TxtBridgePort.Text, out int port) && port > 0 && port < 65536)
                s.BridgePort = port;

            // Screen capture fields
            if (int.TryParse(TxtMonitorIndex.Text, out int monIdx) && monIdx >= 0)
                s.MonitorIndex = monIdx;
            if (int.TryParse(TxtCapX.Text, out int capX))
                s.CaptureX = capX;
            if (int.TryParse(TxtCapY.Text, out int capY))
                s.CaptureY = capY;
            if (int.TryParse(TxtCapW.Text, out int capW) && capW >= 0)
                s.CaptureWidth = capW;
            if (int.TryParse(TxtCapH.Text, out int capH) && capH >= 0)
                s.CaptureHeight = capH;
            s.SampleStep        = (int)SliderSampleStep.Value;
            s.CaptureIntervalMs = (int)SliderCaptureMs.Value;
            s.BrightnessMin     = (int)SliderBrightMin.Value;
            s.BrightnessMax     = (int)SliderBrightMax.Value;
            s.KelvinMin         = (int)SliderKelvinMin.Value;
            s.KelvinMax         = (int)SliderKelvinMax.Value;
            s.SmoothingAlpha    = Math.Round(SliderSmoothing.Value, 2);

            s.IntensityThreshold = SliderIntensity.Value;
            s.MinSleepMs         = (int)SliderSleep.Value;
            s.TunnelBrightDrop   = (int)SliderTunnelDrop.Value;
            s.TunnelBrightFloor  = (int)SliderTunnelFloor.Value;

            // Sync zone list back (the ObservableCollection is the authoritative list now)
            s.Zones = (ZoneListBox.ItemsSource
                as System.Collections.ObjectModel.ObservableCollection<Zone>)
                ?.ToList() ?? new List<Zone>();
        }

        // ── Zone list management ───────────────────────────────────────────────

        private void BtnAddZone_Click(object sender, RoutedEventArgs e)
        {
            var zones = ZoneListBox.ItemsSource
                as System.Collections.ObjectModel.ObservableCollection<Zone>;
            if (zones == null) return;

            zones.Add(new Zone($"zone{zones.Count + 1}", new List<int>(), 1.0));
        }

        private void BtnRemoveZone_Click(object sender, RoutedEventArgs e)
        {
            var zones = ZoneListBox.ItemsSource
                as System.Collections.ObjectModel.ObservableCollection<Zone>;
            if (zones == null || ZoneListBox.SelectedItem == null) return;

            zones.Remove((Zone)ZoneListBox.SelectedItem);
        }

        /// <summary>
        /// Parses the comma-separated node IDs text box back into List&lt;int&gt;
        /// when focus leaves the field.
        /// </summary>
        private void TxtNodeIds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (tb.Tag is not Zone zone) return;

            zone.NodeIds = tb.Text
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out int id) ? (int?)id : null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
        }

        /// <summary>
        /// After binding the zone list, set each NodeIds TextBox's Text property manually
        /// because the DataTemplate binding doesn't convert List&lt;int&gt; to/from string.
        /// </summary>
        private void RefreshNodeIdTextBoxes()
        {
            // WPF ListBox generates containers asynchronously — defer until rendered
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var item in ZoneListBox.Items)
                {
                    if (item is not Zone zone) continue;
                    var container = ZoneListBox.ItemContainerGenerator
                        .ContainerFromItem(item) as ListBoxItem;
                    if (container == null) continue;

                    var tb = FindVisualChild<TextBox>(container, "TxtNodeIds");
                    if (tb != null)
                        tb.Text = string.Join(", ", zone.NodeIds);
                }
            }));
        }

        // ── Slider value-changed handlers ─────────────────────────────────────

        private void SliderIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblIntensityVal == null) return;
            LblIntensityVal.Text = SliderIntensity.Value.ToString("F3");
        }

        private void SliderSleep_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblSleepVal == null) return;
            LblSleepVal.Text = ((int)SliderSleep.Value).ToString();
        }

        private void SliderTunnelDrop_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblTunnelDropVal == null) return;
            LblTunnelDropVal.Text = ((int)SliderTunnelDrop.Value).ToString();
        }

        private void SliderTunnelFloor_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblTunnelFloorVal == null) return;
            LblTunnelFloorVal.Text = ((int)SliderTunnelFloor.Value).ToString();
        }

        private void SliderSampleStep_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblSampleStep == null) return;
            LblSampleStep.Text = ((int)SliderSampleStep.Value).ToString();
        }

        private void SliderCaptureMs_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblCaptureMs == null) return;
            LblCaptureMs.Text = ((int)SliderCaptureMs.Value).ToString();
        }

        private void SliderBrightMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblBrightMin == null) return;
            LblBrightMin.Text = ((int)SliderBrightMin.Value).ToString();
        }

        private void SliderBrightMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblBrightMax == null) return;
            LblBrightMax.Text = ((int)SliderBrightMax.Value).ToString();
        }

        private void SliderKelvinMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblKelvinMin == null) return;
            LblKelvinMin.Text = ((int)SliderKelvinMin.Value).ToString();
        }

        private void SliderKelvinMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblKelvinMax == null) return;
            LblKelvinMax.Text = ((int)SliderKelvinMax.Value).ToString();
        }

        private void SliderSmoothing_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || LblSmoothing == null) return;
            LblSmoothing.Text = SliderSmoothing.Value.ToString("F2");
        }

        private void UpdateSliderLabels()
        {
            if (LblIntensityVal != null)
                LblIntensityVal.Text   = SliderIntensity.Value.ToString("F3");
            if (LblSleepVal != null)
                LblSleepVal.Text       = ((int)SliderSleep.Value).ToString();
            if (LblTunnelDropVal != null)
                LblTunnelDropVal.Text  = ((int)SliderTunnelDrop.Value).ToString();
            if (LblTunnelFloorVal != null)
                LblTunnelFloorVal.Text = ((int)SliderTunnelFloor.Value).ToString();
            if (LblSampleStep != null)
                LblSampleStep.Text     = ((int)SliderSampleStep.Value).ToString();
            if (LblCaptureMs != null)
                LblCaptureMs.Text      = ((int)SliderCaptureMs.Value).ToString();
            if (LblBrightMin != null)
                LblBrightMin.Text      = ((int)SliderBrightMin.Value).ToString();
            if (LblBrightMax != null)
                LblBrightMax.Text      = ((int)SliderBrightMax.Value).ToString();
            if (LblKelvinMin != null)
                LblKelvinMin.Text      = ((int)SliderKelvinMin.Value).ToString();
            if (LblKelvinMax != null)
                LblKelvinMax.Text      = ((int)SliderKelvinMax.Value).ToString();
            if (LblSmoothing != null)
                LblSmoothing.Text      = SliderSmoothing.Value.ToString("F2");
        }

        // ── Save button ────────────────────────────────────────────────────────

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveToModel();

            var errors = ValidateSettings();
            if (errors.Count > 0)
            {
                MessageBox.Show(
                    "Cannot save — please fix the following:\n\n" + string.Join("\n", errors),
                    "AmbiMatter — Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _plugin.SaveSettings();
            MessageBox.Show("AmbiMatter settings saved.", "AmbiMatter",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private List<string> ValidateSettings()
        {
            var s = _plugin.Settings;
            var errors = new List<string>();

            if (s.BrightnessMin >= s.BrightnessMax)
                errors.Add("Brightness Floor must be less than Brightness Ceiling.");

            if (s.KelvinMin >= s.KelvinMax)
                errors.Add("Kelvin Min must be less than Kelvin Max.");

            if (s.SampleStep < 1)
                errors.Add("Sample Step must be at least 1.");

            var zoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var zone in s.Zones)
            {
                if (string.IsNullOrWhiteSpace(zone.Name))
                {
                    errors.Add("All zones must have a non-empty name.");
                    break;
                }
                if (!zoneNames.Add(zone.Name))
                {
                    errors.Add($"Duplicate zone name: '{zone.Name}'.");
                    break;
                }
                if (zone.BrightnessMultiplier <= 0 || zone.BrightnessMultiplier > 2.0)
                    errors.Add($"Zone '{zone.Name}': Brightness Multiplier must be > 0 and <= 2.0.");
            }

            return errors;
        }

        // ── Live status timer ──────────────────────────────────────────────────

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            var s = _plugin.Settings;

            LblGameRunning.Text = $"Game Running: {(_plugin.GameRunning ? "Yes" : "No")}";

            var computed = _plugin.LastComputed;
            LblCurrentKelvin.Text     = $"Current Kelvin:     {computed?.Kelvin ?? 0} K";
            LblCurrentBrightness.Text = $"Current Brightness: {computed?.Brightness ?? 0}";

            var rgb = _plugin.ScreenCapture?.LastAvgRgb ?? (R: 0, G: 0, B: 0);
            LblAvgRgb.Text = $"Avg Screen RGB:     R={rgb.R}  G={rgb.G}  B={rgb.B}";

            // Per-zone status
            var lines = new System.Text.StringBuilder();
            var allStates = _plugin.ZoneManager.AllStates;
            foreach (var zone in s.Zones)
            {
                if (!allStates.TryGetValue(zone.Name, out var state) || state == null)
                {
                    lines.AppendLine($"[{zone.Name}] no state");
                    continue;
                }

                var lastSent = state.LastSent;
                string since = state.MsSinceLastSend == double.MaxValue
                    ? "never"
                    : $"{(int)state.MsSinceLastSend}ms ago";

                if (lastSent != null)
                    lines.AppendLine($"[{zone.Name}]  {lastSent.Kelvin}K  br={lastSent.Brightness}  sent={since}");
                else
                    lines.AppendLine($"[{zone.Name}]  (never sent)");
            }
            LblZoneStatus.Text = lines.ToString().TrimEnd();
        }

        // ── Visual tree helper ─────────────────────────────────────────────────

        private static T FindVisualChild<T>(DependencyObject parent, string name = null)
            where T : FrameworkElement
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && (name == null || typed.Name == name))
                    return typed;
                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
