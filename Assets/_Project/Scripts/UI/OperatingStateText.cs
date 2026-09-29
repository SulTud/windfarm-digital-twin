using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>Plain-language labels and explanations for the operating states, built from the spec numbers.</summary>
    internal static class OperatingStateText
    {
        public static string Label(TurbineOperatingState state)
        {
            switch (state)
            {
                case TurbineOperatingState.Idle: return "IDLE";
                case TurbineOperatingState.RatedPower: return "RATED POWER";
                case TurbineOperatingState.StormShutdown: return "STORM SHUTDOWN";
                default: return "PRODUCING";
            }
        }

        public static string Explanation(TurbineOperatingState state, TurbineSpecs specs)
        {
            switch (state)
            {
                case TurbineOperatingState.Idle:
                    return UiFormat.Format("Wind below cut-in ({0:0} m/s). Rotor idling, no power.", specs.CutInWindSpeed);

                case TurbineOperatingState.RatedPower:
                    return UiFormat.Format("Wind above rated (~{0:0} m/s). Blades pitch to hold {1:0.0} MW.",
                        specs.RatedWindSpeed, specs.RatedPowerMW);

                case TurbineOperatingState.StormShutdown:
                    return UiFormat.Format("Wind above cut-out ({0:0} m/s). Blades feathered, brake holding. Restarts below {1:0} m/s.",
                        specs.CutOutWindSpeed, specs.RestartWindSpeed);

                default:
                    return UiFormat.Format("Wind between cut-in ({0:0}) and rated (~{1:0} m/s). Rotor follows the wind.",
                        specs.CutInWindSpeed, specs.RatedWindSpeed);
            }
        }
    }
}
