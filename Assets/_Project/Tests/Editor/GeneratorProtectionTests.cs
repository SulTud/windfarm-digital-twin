using NUnit.Framework;
using WindFarm.Simulation;

namespace WindFarm.Tests
{
    /// <summary>
    /// Graded generator protection on its own: fan fault, warning and alarm with hysteresis, derating curve,
    /// latched trip and its reset conditions.
    /// </summary>
    public class GeneratorProtectionTests
    {
        private TurbineSpecs specs;
        private GeneratorProtection protection;

        [SetUp]
        public void SetUp()
        {
            specs = new TurbineSpecs();
            protection = new GeneratorProtection(specs);
        }

        [Test]
        public void ReportsAFanFaultBeforeTheTemperatureMoves()
        {
            protection.Step(specs.RatedGeneratorTemperature, coolingFanRunning: false);

            Assert.That(protection.Alarms, Is.EqualTo(TurbineAlarms.CoolingFanFault));
            Assert.That(protection.PowerLimitMW, Is.EqualTo(specs.RatedPowerMW));
        }

        [Test]
        public void WarningUsesHysteresis()
        {
            float warning = specs.GeneratorWarningTemperature;

            protection.Step(warning - 0.1f, true);
            Assert.That(protection.Alarms, Is.EqualTo(TurbineAlarms.None), "below the warning");

            protection.Step(warning, true);
            Assert.That(protection.Alarms, Is.EqualTo(TurbineAlarms.GeneratorTemperatureWarning), "at the warning");

            protection.Step(warning - GeneratorProtection.Hysteresis + 0.1f, true);
            Assert.That(protection.Alarms, Is.EqualTo(TurbineAlarms.GeneratorTemperatureWarning), "inside the hysteresis band");

            protection.Step(warning - GeneratorProtection.Hysteresis - 0.1f, true);
            Assert.That(protection.Alarms, Is.EqualTo(TurbineAlarms.None), "below the band");
        }

        [Test]
        public void DeratesLinearlyBetweenAlarmAndTrip()
        {
            float alarm = specs.GeneratorAlarmTemperature;
            float trip = specs.GeneratorTripTemperature;
            float rated = specs.RatedPowerMW;
            float floor = rated * specs.DeratedPowerFraction;

            protection.Step(alarm, true);
            Assert.That(protection.PowerLimitMW, Is.EqualTo(rated).Within(1e-4f), "at the alarm");
            Assert.That(protection.Alarms & TurbineAlarms.GeneratorTemperatureAlarm, Is.Not.EqualTo(TurbineAlarms.None));

            protection.Step((alarm + trip) * 0.5f, true);
            Assert.That(protection.PowerLimitMW, Is.EqualTo((rated + floor) * 0.5f).Within(1e-4f), "half way");

            protection.Step(trip + 10f, true);
            Assert.That(protection.PowerLimitMW, Is.EqualTo(floor).Within(1e-4f), "above the trip");
        }

        [Test]
        public void TripIsLatchedUntilCooledAndTheFanRuns()
        {
            float restart = specs.GeneratorRestartTemperature;

            protection.Step(specs.GeneratorTripTemperature, coolingFanRunning: false);
            Assert.That(protection.Tripped, Is.True, "at the trip temperature");

            protection.Step(restart + 5f, coolingFanRunning: true);
            Assert.That(protection.Tripped, Is.True, "above the restart temperature");

            protection.Step(restart - 1f, coolingFanRunning: false);
            Assert.That(protection.Tripped, Is.True, "cooled, but the fan is still dead");

            protection.Step(restart - 1f, coolingFanRunning: true);
            Assert.That(protection.Tripped, Is.False, "cooled and repaired");
            Assert.That(protection.Alarms, Is.EqualTo(TurbineAlarms.None));
        }
    }
}
