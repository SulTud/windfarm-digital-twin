using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Supervisory controller: decides the operating state from the filtered wind speed and the produced power.
    /// All transitions use hysteresis so the state does not chatter due to momentary noise.
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

            TurbineOperatingState next = EvaluateNextState(windSpeed, powerMW);
            if (next == State)
                return false;

            State = next;
            return true;
        }

        private TurbineOperatingState EvaluateNextState(float instantWindSpeed, float powerMW)
        {
            if (State == TurbineOperatingState.StormShutdown)
            {
                if (averagedWindSpeed >= specs.RestartWindSpeed)
                    return TurbineOperatingState.StormShutdown;

                return averagedWindSpeed >= specs.CutInWindSpeed
                    ? TurbineOperatingState.Producing
                    : TurbineOperatingState.Idle;
            }

            bool stormDetected = averagedWindSpeed >= specs.CutOutWindSpeed ||
                                 instantWindSpeed >= specs.CutOutWindSpeed * GustCutOutFactor;
            if (stormDetected)
                return TurbineOperatingState.StormShutdown;

            switch (State)
            {
                case TurbineOperatingState.Idle:
                    return averagedWindSpeed >= specs.CutInWindSpeed
                        ? TurbineOperatingState.Producing
                        : TurbineOperatingState.Idle;

                case TurbineOperatingState.Producing:
                    if (averagedWindSpeed < specs.CutInWindSpeed - CutInHysteresis)
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
