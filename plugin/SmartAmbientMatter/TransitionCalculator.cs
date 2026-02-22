using System;
using SmartAmbientMatter.Models;

namespace SmartAmbientMatter
{
    /// <summary>
    /// Computes intensity scores and transition times for lighting updates.
    ///
    /// Intensity scoring determines whether a change is large enough to send.
    /// Transition time ("Snap or Glide") anti-aliases the digital step between updates —
    /// it does NOT simulate event duration. The game paces events; we smooth the steps.
    ///
    /// The Guillotine Check is a hard override for tunnel entry/exit (instant cuts).
    /// </summary>
    public class TransitionCalculator
    {
        // ── Transition Constants (Matter units: 1 = 100ms) ─────────────────────
        public const int TRANSITION_INSTANT = 0;   // 0ms — tunnel, massive shift
        public const int TRANSITION_QUICK   = 4;   // 400ms — cloud cover entering
        public const int TRANSITION_DEFAULT = 8;   // 800ms — sunset drift (default)

        /// <summary>
        /// Calculates change intensity between current state and last-sent state.
        /// Uses Max(deltaBrightness, deltaKelvin) to avoid masking large single-axis changes.
        /// Returns 1.0 (maximum) if lastSent is null (first send — always update).
        /// </summary>
        /// <param name="current">Current computed lighting state.</param>
        /// <param name="lastSent">Last state sent to the bridge (null if never sent).</param>
        /// <param name="kelvinRange">KelvinMax - KelvinMin from settings, used for normalization.</param>
        public double CalculateIntensity(LightingState current, LightingState lastSent, int kelvinRange)
        {
            if (lastSent == null) return 1.0;
            if (kelvinRange <= 0) kelvinRange = 3800; // fallback to avoid division by zero

            double deltaBright = Math.Abs(current.Brightness - lastSent.Brightness) / 255.0;
            double deltaKelvin = Math.Abs(current.Kelvin - lastSent.Kelvin) / (double)kelvinRange;

            return Math.Max(deltaBright, deltaKelvin);
        }

        /// <summary>
        /// Checks whether the brightness change from the previous frame to the current frame
        /// constitutes a Guillotine event (sudden tunnel entry or exit).
        ///
        /// Entry: large DROP in brightness AND result is below the floor (going dark)
        /// Exit:  large RISE in brightness AND previous value was below the floor (leaving dark)
        ///
        /// Guillotine events bypass intensity scoring and use transition=0 (instant).
        /// </summary>
        /// <param name="currentBrightness">Brightness computed this frame.</param>
        /// <param name="prevBrightness">Brightness computed the previous frame.</param>
        /// <param name="tunnelBrightDrop">Min drop magnitude to trigger (from settings).</param>
        /// <param name="tunnelBrightFloor">Max brightness considered "in tunnel" (from settings).</param>
        public bool CheckGuillotine(int currentBrightness, int prevBrightness,
                                    int tunnelBrightDrop, int tunnelBrightFloor)
        {
            int delta = currentBrightness - prevBrightness;

            // Tunnel entry: brightness dropped sharply and is now very dark
            bool isEntry = (delta < -tunnelBrightDrop) && (currentBrightness <= tunnelBrightFloor);

            // Tunnel exit: brightness rose sharply and previous frame was very dark
            bool isExit = (delta > tunnelBrightDrop) && (prevBrightness <= tunnelBrightFloor);

            return isEntry || isExit;
        }

        /// <summary>
        /// Determines the Matter transition time for this update.
        ///
        /// Snap-or-Glide rules (in priority order):
        ///   guillotine     → 0   (instant — tunnel entry/exit, scene cut)
        ///   intensity > 0.30 → 0   (instant — lights-out or massive shift)
        ///   intensity > 0.10 → 4   (400ms  — cloud cover entering, weather shift)
        ///   else             → 8   (800ms  — sunset drift, subtle time-of-day change)
        /// </summary>
        public int GetTransition(double intensity, bool guillotine)
        {
            if (guillotine || intensity > 0.30) return TRANSITION_INSTANT;
            if (intensity > 0.10)               return TRANSITION_QUICK;
            return TRANSITION_DEFAULT;
        }
    }
}
