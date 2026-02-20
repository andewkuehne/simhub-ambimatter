namespace SmartAmbientMatter.Models
{
    /// <summary>
    /// Represents a target lighting state to be sent to Matter bulbs.
    /// </summary>
    public class LightingState
    {
        /// <summary>Color temperature in Kelvin (2700–6500).</summary>
        public int Kelvin { get; set; }

        /// <summary>
        /// Brightness level (0–254). Matter Level Control spec uses 0-254, NOT 0-255.
        /// Level 0 with MoveToLevelWithOnOff turns the bulb off.
        /// </summary>
        public int Brightness { get; set; }

        /// <summary>
        /// Transition time in Matter units. 1 unit = 100ms.
        /// 0 = instant, 4 = 400ms, 8 = 800ms (default smooth glide).
        /// </summary>
        public int Transition { get; set; }

        public LightingState() { }

        public LightingState(int kelvin, int brightness, int transition)
        {
            Kelvin = kelvin;
            Brightness = brightness;
            Transition = transition;
        }

        public override string ToString() =>
            $"Kelvin={Kelvin}K, Brightness={Brightness}, Transition={Transition * 100}ms";
    }
}
