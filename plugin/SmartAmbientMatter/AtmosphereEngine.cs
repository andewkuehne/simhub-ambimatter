using SmartAmbientMatter.Models;

namespace SmartAmbientMatter
{
    /// <summary>
    /// Three-layer atmospheric model that computes a target LightingState from game telemetry.
    ///
    /// Layer 1 — Solar: keyframe-based Kelvin + Brightness curve driven by time of day.
    /// Layer 2 — Weather: stub (Phase 3). Returns solar output unmodified.
    /// Layer 3 — Local modifiers (tunnels): handled by TransitionCalculator/Guillotine, not here.
    /// </summary>
    public class AtmosphereEngine
    {
        // ── Solar Keyframe Table ───────────────────────────────────────────────
        // Each entry: (hour, kelvin, brightness)
        // Linear interpolation is applied between adjacent keyframes.
        // Hour 0.0 and 24.0 anchor night; values wrap correctly.
        private static readonly (double Hour, int Kelvin, int Brightness)[] SolarKeyframes =
        {
            (  0.0, 2700,  20),   // midnight
            (  5.0, 2700,  20),   // pre-dawn dark
            (  6.0, 2900,  40),   // astronomical dawn
            (  7.0, 3200,  80),   // sunrise warm glow
            (  9.0, 4500, 170),   // mid-morning
            ( 12.0, 6500, 254),   // solar noon — peak cool/bright
            ( 14.0, 5500, 220),   // early afternoon cool-down
            ( 17.0, 4000, 160),   // late afternoon warmth returns
            ( 18.5, 3000,  90),   // sunset
            ( 20.0, 2800,  40),   // dusk
            ( 21.0, 2700,  20),   // civil night
            ( 24.0, 2700,  20),   // midnight (wrap anchor)
        };

        /// <summary>
        /// Calculate the current LightingState from iRacing's SessionTimeOfDay.
        /// </summary>
        /// <param name="sessionTimeOfDaySec">Seconds from midnight (0–86400).</param>
        /// <returns>Calculated LightingState (Kelvin + Brightness). Transition is not set here.</returns>
        public LightingState Calculate(double sessionTimeOfDaySec)
        {
            double hour = sessionTimeOfDaySec / 3600.0;

            // Clamp to [0, 24] to handle any out-of-range telemetry values
            if (hour < 0.0) hour = 0.0;
            if (hour > 24.0) hour = 24.0;

            var solar = InterpolateSolar(hour);
            // Layer 2: weather modifier (Phase 3 stub — returns solar output unchanged)
            return ApplyWeather(solar);
        }

        // ── Private Helpers ────────────────────────────────────────────────────

        private LightingState InterpolateSolar(double hour)
        {
            // Find the two surrounding keyframes
            int hi = 0;
            while (hi < SolarKeyframes.Length - 1 && SolarKeyframes[hi + 1].Hour <= hour)
                hi++;

            // If exactly on or past the last keyframe, return the last value
            if (hi >= SolarKeyframes.Length - 1)
            {
                var last = SolarKeyframes[SolarKeyframes.Length - 1];
                return new LightingState(last.Kelvin, last.Brightness, 0);
            }

            var lo = SolarKeyframes[hi];
            var next = SolarKeyframes[hi + 1];

            double span = next.Hour - lo.Hour;
            double t = (span > 0.0) ? (hour - lo.Hour) / span : 0.0;

            int kelvin = (int)(lo.Kelvin + t * (next.Kelvin - lo.Kelvin));
            int brightness = (int)(lo.Brightness + t * (next.Brightness - lo.Brightness));

            return new LightingState(kelvin, brightness, 0);
        }

        /// <summary>
        /// Phase 3 stub: apply weather modifier to solar output.
        /// In Phase 3 this will blend solar state toward weather-adjusted values
        /// based on iRacing cloud cover / rain properties.
        /// </summary>
        private LightingState ApplyWeather(LightingState solar)
        {
            // No modification in Phase 2
            return solar;
        }
    }
}
