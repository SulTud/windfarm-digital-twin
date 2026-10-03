using NUnit.Framework;
using WindFarm.Simulation;

namespace WindFarm.Tests
{
    /// <summary>
    /// Constant wind: the dynamic model must settle exactly on the datasheet power curve computed from the same physics,
    /// at the documented operating points.
    /// </summary>
    public class SteadyStateTests
    {
        private const float SettleTime = 600f; // s; > 6 thermal time constants

        [TestCase(5f)]
        [TestCase(6f)]
        [TestCase(8f)]
        [TestCase(10f)]
        [TestCase(10.6f)]
        [TestCase(12f)]
        [TestCase(15f)]
        [TestCase(20f)]
        [TestCase(24f)]
        public void SettlesOnThePowerCurve(float windSpeed)
        {
            var plant = new TestPlant();
            plant.Run(windSpeed, SettleTime);

            Assert.That(plant.PowerMW, Is.EqualTo(plant.Specs.PowerCurveMW(windSpeed)).Within(0.005f));
        }

        [TestCase(5f, 6.49f, 0.319f)]
        [TestCase(8f, 10.23f, 1.307f)]
        [TestCase(10.6f, 14.0f, 3.0f)]
        public void MatchesTheDocumentedOperatingPoint(float windSpeed, float rpm, float powerMW)
        {
            var plant = new TestPlant();
            plant.Run(windSpeed, SettleTime);

            Assert.That(plant.Rotor.Rpm, Is.EqualTo(rpm).Within(0.03f), "rotor speed (rpm)");
            Assert.That(plant.PowerMW, Is.EqualTo(powerMW).Within(0.005f), "power (MW)");
        }

        [TestCase(12f, 4.5f)]
        [TestCase(15f, 11.6f)]
        [TestCase(20f, 19.4f)]
        [TestCase(24f, 23.8f)]
        public void PitchesToHoldRatedPowerAboveRatedWind(float windSpeed, float pitchDegrees)
        {
            var plant = new TestPlant();
            plant.Run(windSpeed, SettleTime);

            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.RatedPower));
            Assert.That(plant.Rotor.Rpm, Is.EqualTo(plant.Specs.RatedRotorRpm).Within(0.05f), "rotor speed (rpm)");
            Assert.That(plant.Pitch.Angle, Is.EqualTo(pitchDegrees).Within(0.3f), "blade pitch (deg)");
        }

        [Test]
        public void RatedWindSpeedFollowsFromThePhysics()
        {
            var specs = new TurbineSpecs();

            Assert.That(specs.RatedWindSpeed, Is.EqualTo(10.6f).Within(0.1f));
            Assert.That(specs.PowerCurveMW(specs.CutInWindSpeed - 0.1f), Is.EqualTo(0f), "below cut-in");
            Assert.That(specs.PowerCurveMW(specs.CutOutWindSpeed + 0.1f), Is.EqualTo(0f), "above cut-out");
        }

        [Test]
        public void GeneratorWarmsToTheRatedTemperatureWithoutAlarms()
        {
            var plant = new TestPlant();
            plant.Run(12f, SettleTime);

            Assert.That(plant.Thermal.Temperature, Is.EqualTo(plant.Specs.RatedGeneratorTemperature).Within(1f));
            Assert.That(plant.Protection.Alarms, Is.EqualTo(TurbineAlarms.None));
            Assert.That(plant.Protection.PowerLimitMW, Is.EqualTo(plant.Specs.RatedPowerMW));
        }
    }
}
