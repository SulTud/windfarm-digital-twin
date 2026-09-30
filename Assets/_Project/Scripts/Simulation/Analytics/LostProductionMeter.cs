using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Energy lost to a fault event, computed from telemetry alone (so it works for any
    /// <see cref="ITurbineTelemetrySource"/>, mock or real), the way SCADA availability reports do it (IEC 61400-26):
    ///
    ///   lost = ∫ (expected power - actual power) dt   over the affected period
    ///   expected power = power curve at the averaged measured wind
    ///
    /// The affected period starts with the first derated sample or a FaultStop and lasts while the turbine is derated,
    /// fault-stopped, or recovering from the stop (after a FaultStop it takes a while to run the rotor up again: the
    /// recovery ends once the power first reaches the expected power, or the turbine is not producing for another
    /// reason). The value of the last event stays readable until the next one starts.
    ///
    /// The wind is averaged over 30 s instead of the standard 10 min because the demo compresses time; the difference
    /// is signed (turbulence scatters on both sides) and only the total is clamped at zero.
    /// </summary>
    public sealed class LostProductionMeter
    {
        private const float WindAveragingTime = 30f;   // s
        private const double MaxSampleGap = 5.0;       // s; longer gaps (paused feed) are not integrated
        private const float TableStep = 0.1f;          // m/s
        private const float DeratedThreshold = 0.999f;

        private readonly TurbineSpecs specs;
        private readonly float[] expectedPowerTable;

        private bool hasSample;
        private double lastTime;
        private float averagedWind;
        private bool recovering;
        private double lostEnergyMWh;

        /// <param name="specs">The power curve source. With a real turbine this would be the OEM's warranted curve.</param>
        public LostProductionMeter(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));

            // The curve bisects per wind speed; tabulate it once instead of per sample.
            int count = Mathf.CeilToInt(specs.CutOutWindSpeed / TableStep) + 2;
            expectedPowerTable = new float[count];
            for (int i = 0; i < count; i++)
                expectedPowerTable[i] = specs.PowerCurveMW(i * TableStep);
        }

        /// <summary>Energy lost in the current or the last event (MWh).</summary>
        public double LostEnergyMWh => Math.Max(0.0, lostEnergyMWh);

        /// <summary>True while the turbine is derated, fault-stopped or recovering from a fault stop.</summary>
        public bool EventActive { get; private set; }

        /// <summary>True once any event was recorded since the last reset.</summary>
        public bool HasEvent { get; private set; }

        /// <summary>Power the turbine would produce at the averaged wind without the fault (MW).</summary>
        public float ExpectedPowerMW { get; private set; }

        public void Reset()
        {
            hasSample = false;
            recovering = false;
            lostEnergyMWh = 0.0;
            EventActive = false;
            HasEvent = false;
            ExpectedPowerMW = 0f;
        }

        public void Add(in TurbineTelemetry telemetry)
        {
            // The simulation time jumped back: the source was restarted, the old event belongs to another run.
            if (hasSample && telemetry.SimulationTime < lastTime)
                Reset();

            double deltaTime = hasSample ? telemetry.SimulationTime - lastTime : 0.0;
            if (hasSample)
                averagedWind += (telemetry.WindSpeed - averagedWind) * (1f - Mathf.Exp(-(float)deltaTime / WindAveragingTime));
            else
                averagedWind = telemetry.WindSpeed;

            hasSample = true;
            lastTime = telemetry.SimulationTime;
            ExpectedPowerMW = ExpectedPower(averagedWind);

            bool faultStopped = telemetry.State == TurbineOperatingState.FaultStop;
            bool derated = telemetry.PowerLimitMW < specs.RatedPowerMW * DeratedThreshold;
            bool producing = telemetry.State == TurbineOperatingState.Producing ||
                             telemetry.State == TurbineOperatingState.RatedPower;

            if (faultStopped)
                recovering = true;
            else if (recovering && (!producing || telemetry.PowerOutputMW >= ExpectedPowerMW))
                recovering = false;

            bool affected = faultStopped || derated || recovering;
            if (affected && !EventActive)
            {
                EventActive = true;
                HasEvent = true;
                lostEnergyMWh = 0.0;
                return; // integrate from the next sample on
            }

            if (!affected)
            {
                EventActive = false;
                return;
            }

            if (deltaTime > 0.0 && deltaTime <= MaxSampleGap)
                lostEnergyMWh += (ExpectedPowerMW - telemetry.PowerOutputMW) * deltaTime / 3600.0;
        }

        private float ExpectedPower(float windSpeed)
        {
            float position = Mathf.Max(0f, windSpeed) / TableStep;
            int index = (int)position;
            if (index >= expectedPowerTable.Length - 1)
                return 0f; // beyond cut-out
            return Mathf.Lerp(expectedPowerTable[index], expectedPowerTable[index + 1], position - index);
        }
    }
}
