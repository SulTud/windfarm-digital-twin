using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>
    /// What the status pill shows. The operating state from the telemetry, except that a producing turbine under a
    /// power limit reads as Derated: SCADA systems show derated operation as its own status, and for a viewer it is
    /// the most important fact about it.
    /// </summary>
    internal enum DisplayedState
    {
        Idle,
        Producing,
        RatedPower,
        Derated,
        StormShutdown,
        FaultStop,
    }

    /// <summary>Plain-language labels and explanations for the operating states, built from the spec numbers.</summary>
    internal static class OperatingStateText
    {
        private const float DeratedThreshold = 0.999f;

        public static DisplayedState Displayed(in TurbineTelemetry telemetry, TurbineSpecs specs)
        {
            switch (telemetry.State)
            {
                case TurbineOperatingState.Idle: return DisplayedState.Idle;
                case TurbineOperatingState.StormShutdown: return DisplayedState.StormShutdown;
                case TurbineOperatingState.FaultStop: return DisplayedState.FaultStop;
            }

            if (telemetry.PowerLimitMW < specs.RatedPowerMW * DeratedThreshold)
                return DisplayedState.Derated;
            return telemetry.State == TurbineOperatingState.RatedPower ? DisplayedState.RatedPower : DisplayedState.Producing;
        }

        public static string Label(DisplayedState state)
        {
            switch (state)
            {
                case DisplayedState.Idle: return "IDLE";
                case DisplayedState.RatedPower: return "RATED POWER";
                case DisplayedState.Derated: return "DERATED";
                case DisplayedState.StormShutdown: return "STORM SHUTDOWN";
                case DisplayedState.FaultStop: return "FAULT STOP";
                default: return "PRODUCING";
            }
        }

        public static string Explanation(DisplayedState state, TurbineSpecs specs)
        {
            switch (state)
            {
                case DisplayedState.Idle:
                    return UiFormat.Format("Wind below cut-in ({0:0} m/s). Rotor idling, no power.", specs.CutInWindSpeed);

                case DisplayedState.RatedPower:
                    return UiFormat.Format("Wind above rated (~{0:0} m/s). Blades pitch to hold {1:0.0} MW.",
                        specs.RatedWindSpeed, specs.RatedPowerMW);

                case DisplayedState.Derated:
                    return UiFormat.Format("Generator above {0:0} °C. Output limited to cut the heat; blades pitch to shed the rest.",
                        specs.GeneratorAlarmTemperature);

                case DisplayedState.StormShutdown:
                    return UiFormat.Format("Wind above cut-out ({0:0} m/s). Blades feathered, brake holding. Restarts below {1:0} m/s.",
                        specs.CutOutWindSpeed, specs.RestartWindSpeed);

                case DisplayedState.FaultStop:
                    return UiFormat.Format("Protection stopped the turbine. Restarts once the fault is cleared and the generator is below {0:0} °C.",
                        specs.GeneratorRestartTemperature);

                default:
                    return UiFormat.Format("Wind between cut-in ({0:0}) and rated (~{1:0} m/s). Rotor follows the wind.",
                        specs.CutInWindSpeed, specs.RatedWindSpeed);
            }
        }
    }
}
