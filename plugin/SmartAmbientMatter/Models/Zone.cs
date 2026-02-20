using System.Collections.Generic;

namespace SmartAmbientMatter.Models
{
    /// <summary>
    /// A named group of Matter bulbs that receive the same lighting commands.
    /// Each zone can have a brightness multiplier to account for different fixtures.
    /// </summary>
    public class Zone
    {
        /// <summary>Zone name (e.g. "ceiling", "desk_lamp"). Used as the UDP routing key.</summary>
        public string Name { get; set; } = "ceiling";

        /// <summary>Matter node IDs of bulbs assigned to this zone.</summary>
        public List<int> NodeIds { get; set; } = new List<int>();

        /// <summary>
        /// Multiplier applied to the calculated brightness before sending.
        /// 1.0 = full brightness, 0.6 = 60%. Allows per-fixture brightness compensation.
        /// </summary>
        public double BrightnessMultiplier { get; set; } = 1.0;

        public Zone() { }

        public Zone(string name, List<int> nodeIds, double brightnessMultiplier = 1.0)
        {
            Name = name;
            NodeIds = nodeIds;
            BrightnessMultiplier = brightnessMultiplier;
        }

        public override string ToString() =>
            $"{Name} [nodes: {string.Join(",", NodeIds)}, mult: {BrightnessMultiplier:F2}]";
    }
}
