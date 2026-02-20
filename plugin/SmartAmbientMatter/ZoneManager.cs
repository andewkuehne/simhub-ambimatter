using System;
using System.Collections.Generic;
using SmartAmbientMatter.Models;

namespace SmartAmbientMatter
{
    /// <summary>
    /// Tracks per-zone runtime state: last-sent lighting values and cooldown timing.
    /// Each zone independently rate-limits its own updates so zone A's cooldown
    /// does not block zone B.
    /// </summary>
    public class ZoneManager
    {
        private readonly Dictionary<string, ZoneRuntimeState> _states =
            new Dictionary<string, ZoneRuntimeState>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// (Re)initializes the zone state dictionary from the current settings.
        /// Called on plugin Init() and whenever zone settings are saved.
        /// Existing state is discarded — this forces a fresh send to all bulbs.
        /// </summary>
        public void Initialize(List<Zone> zones)
        {
            _states.Clear();
            foreach (var zone in zones)
            {
                if (!string.IsNullOrWhiteSpace(zone.Name))
                    _states[zone.Name] = new ZoneRuntimeState();
            }
        }

        /// <summary>Returns the runtime state for a zone, or null if the zone is unknown.</summary>
        public ZoneRuntimeState GetState(string zoneName)
        {
            _states.TryGetValue(zoneName, out var state);
            return state;
        }

        /// <summary>Read-only view of all zone states for the settings UI status panel.</summary>
        public IReadOnlyDictionary<string, ZoneRuntimeState> AllStates => _states;
    }

    /// <summary>
    /// Per-zone mutable runtime state. Tracks last-sent values and when the last send occurred.
    /// </summary>
    public class ZoneRuntimeState
    {
        /// <summary>
        /// Last LightingState successfully sent for this zone.
        /// Null means the zone has never been sent to — forces the first send through
        /// regardless of intensity threshold.
        /// </summary>
        public LightingState LastSent { get; private set; } = null;

        /// <summary>When the last send occurred. MinValue = never.</summary>
        public DateTime LastSentTime { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Returns true if the zone is still within its mandatory cooldown window.
        /// </summary>
        public bool IsOnCooldown(int minSleepMs) =>
            (DateTime.Now - LastSentTime).TotalMilliseconds < minSleepMs;

        /// <summary>
        /// Milliseconds since the last send. Returns a large value if never sent.
        /// Used by the settings UI status panel.
        /// </summary>
        public double MsSinceLastSend =>
            LastSentTime == DateTime.MinValue
                ? double.MaxValue
                : (DateTime.Now - LastSentTime).TotalMilliseconds;

        /// <summary>Records a successful send, updating last-sent state and timestamp.</summary>
        public void RecordSend(LightingState state)
        {
            LastSent = state;
            LastSentTime = DateTime.Now;
        }
    }
}
