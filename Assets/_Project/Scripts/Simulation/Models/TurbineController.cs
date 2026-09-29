using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Supervisory controller: decides the operating state from the filtered wind speed and the produced power.
    ///
    /// Chatter protection, like a real turbine PLC:
    /// - Threshold hysteresis on every transition (enter and exit levels differ).
    /// - Confirmation timer: a non-protective transition must be requested continuously for StateConfirmTime.
    /// - Storm stops are immediate (safety), but a restart needs the averaged wind below the restart speed
    ///   AND a minimum stop time. Without the delay, a gust trip at a low average restarts on the next step.
    /// - Low-wind disconnect: when the instant wind drops below cut-in minus hysteresis (~1 % of rated power), the
    ///   turbine goes idle after the confirmation time instead of waiting for the slow 30 s average. A real generator
    ///   would otherwise motor the rotor. The instant wind is used rather than the power, because the power is still
    ///   low for a few seconds while the rotor runs up after a restart.
    /// </summary>
    public sealed class TurbineController
    {
        private const float CutInHysteresis = 0.5f;          // m/s
        private const float RatedPowerEnterRatio = 0.99f;
        private const float RatedPowerExitRatio = 0.95f;
        private const float GustCutOutFactor = 1.12f;        // stop immediately on a ~28 m/s gust without waiting for the average

        private readonly TurbineSpecs specs;
        private float averagedWindSpeed;
        private bool initialized;

        private TurbineOperatingState pendingState;
        private float pendingTime;
        private float timeInState;

        public TurbineOperatingState State { get; private set; } = TurbineOperatingState.Idle;

        public bool IsGeneratorConnected =>
            State == TurbineOperatingState.Producing || State == TurbineOperatingState.RatedPower;

        public TurbineController(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
        }

        /// <returns>True if the state changed during this step.</returns>
        public bool Step(float deltaTime, float windSpeed, float powerMW)
        {
            if (!initialized)
            {
                averagedWindSpeed = windSpeed;
                initialized = true;
            }
            else
            {
                float alpha = 1f - Mathf.Exp(-deltaTime / specs.ControlAveragingTime);
                averagedWindSpeed += (windSpeed - averagedWindSpeed) * alpha;
            }

            timeInState += deltaTime;

            TurbineOperatingState requested = EvaluateRequestedState(windSpeed, powerMW);
            if (requested == State)
            {
                pendingTime = 0f;
                return false;
            }

            // Protective stops happen at once; everything else is debounced (like a coyote-time timer on a state change).
            if (requested != TurbineOperatingState.StormShutdown)
            {
                if (requested != pendingState)
                {
                    pendingState = requested;
                    pendingTime = 0f;
                }

                pendingTime += deltaTime;
                if (pendingTime < specs.StateConfirmTime)
                    return false;
            }

            State = requested;
            pendingTime = 0f;
            timeInState = 0f;
            return true;
        }

        private TurbineOperatingState EvaluateRequestedState(float instantWindSpeed, float powerMW)
        {
            bool windAboveCutIn = averagedWindSpeed >= specs.CutInWindSpeed && instantWindSpeed >= specs.CutInWindSpeed;

            if (State == TurbineOperatingState.StormShutdown)
            {
                if (timeInState < specs.StormRestartDelay || averagedWindSpeed >= specs.RestartWindSpeed)
                    return TurbineOperatingState.StormShutdown;

                return windAboveCutIn ? TurbineOperatingState.Producing : TurbineOperatingState.Idle;
            }

            bool stormDetected = averagedWindSpeed >= specs.CutOutWindSpeed ||
                                 instantWindSpeed >= specs.CutOutWindSpeed * GustCutOutFactor;
            if (stormDetected)
                return TurbineOperatingState.StormShutdown;

            switch (State)
            {
                case TurbineOperatingState.Idle:
                    // Both the average and the instant wind: after a lull the average is still high, and reconnecting
                    // into no wind would trip the low-wind disconnect right away.
                    return windAboveCutIn ? TurbineOperatingState.Producing : TurbineOperatingState.Idle;

                case TurbineOperatingState.Producing:
                    float stopWindSpeed = specs.CutInWindSpeed - CutInHysteresis;
                    if (averagedWindSpeed < stopWindSpeed || instantWindSpeed < stopWindSpeed)
                        return TurbineOperatingState.Idle;

                    return powerMW >= specs.RatedPowerMW * RatedPowerEnterRatio
                        ? TurbineOperatingState.RatedPower
                        : TurbineOperatingState.Producing;

                case TurbineOperatingState.RatedPower:
                    return powerMW < specs.RatedPowerMW * RatedPowerExitRatio
                        ? TurbineOperatingState.Producing
                        : TurbineOperatingState.RatedPower;

                default:
                    return State;
            }
        }
    }
}
