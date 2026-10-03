using NUnit.Framework;
using WindFarm.Simulation;

namespace WindFarm.Tests
{
    /// <summary>
    /// The lost production meter on hand-made telemetry, as it would receive it from any source (mock or real).
    /// </summary>
    public class LostProductionMeterTests
    {
        private const float Interval = 0.2f;  // s
        private const float Wind = 12f;       // m/s; above rated, so the expected power is rated

        private TurbineSpecs specs;
        private LostProductionMeter meter;
        private double time;

        [SetUp]
        public void SetUp()
        {
            specs = new TurbineSpecs();
            meter = new LostProductionMeter(specs);
            time = 0.0;
        }

        private void Feed(float seconds, TurbineOperatingState state, float powerMW, float limitMW)
        {
            int samples = (int)System.Math.Round(seconds / Interval);
            for (int i = 0; i < samples; i++)
            {
                meter.Add(new TurbineTelemetry("TEST", time, Wind, 14f, 5f, 120f, powerMW, 0.0, state, limitMW,
                    TurbineAlarms.None));
                time += Interval;
            }
        }

        [Test]
        public void HealthyOperationRecordsNoEvent()
        {
            Feed(120f, TurbineOperatingState.RatedPower, specs.RatedPowerMW, specs.RatedPowerMW);

            Assert.That(meter.HasEvent, Is.False);
            Assert.That(meter.LostEnergyMWh, Is.EqualTo(0.0));
        }

        [Test]
        public void IntegratesTheShortfallWhileDerated()
        {
            float limit = specs.RatedPowerMW * 0.5f;
            Feed(100f, TurbineOperatingState.Producing, limit, limit);

            // Integration starts with the second sample of the event.
            double expected = (specs.RatedPowerMW - limit) * (100.0 - Interval) / 3600.0;
            Assert.That(meter.EventActive, Is.True);
            Assert.That(meter.LostEnergyMWh, Is.EqualTo(expected).Within(expected * 0.01));
        }

        [Test]
        public void KeepsTheLastEventReadableAfterRecovery()
        {
            float limit = specs.RatedPowerMW * 0.5f;
            Feed(100f, TurbineOperatingState.Producing, limit, limit);
            double lost = meter.LostEnergyMWh;

            Feed(30f, TurbineOperatingState.RatedPower, specs.RatedPowerMW, specs.RatedPowerMW);

            Assert.That(meter.EventActive, Is.False);
            Assert.That(meter.HasEvent, Is.True);
            Assert.That(meter.LostEnergyMWh, Is.EqualTo(lost));
        }

        [Test]
        public void CountsTheRecoveryAfterAFaultStopUntilExpectedPowerIsReached()
        {
            Feed(20f, TurbineOperatingState.FaultStop, 0f, specs.RatedPowerMW);
            Feed(20f, TurbineOperatingState.Producing, 1f, specs.RatedPowerMW);
            Assert.That(meter.EventActive, Is.True, "ramping up after the stop");

            Feed(1f, TurbineOperatingState.RatedPower, specs.RatedPowerMW, specs.RatedPowerMW);
            Assert.That(meter.EventActive, Is.False, "back at the expected power");
        }

        [Test]
        public void ResetsWhenTheSourceRestarts()
        {
            float limit = specs.RatedPowerMW * 0.5f;
            Feed(100f, TurbineOperatingState.Producing, limit, limit);

            time = 0.0;
            Feed(1f, TurbineOperatingState.RatedPower, specs.RatedPowerMW, specs.RatedPowerMW);

            Assert.That(meter.HasEvent, Is.False);
            Assert.That(meter.LostEnergyMWh, Is.EqualTo(0.0));
        }
    }
}
