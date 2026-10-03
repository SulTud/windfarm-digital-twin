using System;
using NUnit.Framework;
using WindFarm.Simulation;

namespace WindFarm.Tests
{
    /// <summary>
    /// Transients of the torque balance and the pitch controller: gusts, lulls, start-up and turbulence.
    /// </summary>
    public class DynamicResponseTests
    {
        [Test]
        public void GustAtRatedIsCaughtByThePitchController()
        {
            var plant = new TestPlant();
            plant.Run(12f, 200f);

            float rated = plant.Specs.RatedRotorRpm;
            float maxRpm = 0f;
            for (int i = 0; i < (int)(30f / TestPlant.TimeStep); i++)
            {
                plant.Step(20f);
                maxRpm = Math.Max(maxRpm, plant.Rotor.Rpm);
            }

            // Documented: +12.4 % overspeed, back to rated in about 15 s.
            Assert.That(maxRpm / rated - 1f, Is.LessThan(0.15f), "peak overspeed");
            Assert.That(plant.Rotor.Rpm, Is.EqualTo(rated).Within(rated * 0.01f), "rotor speed 30 s after the gust");
            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.RatedPower));
        }

        [Test]
        public void HighWindStartUpDoesNotOverspeed()
        {
            var plant = new TestPlant();
            float maxRpm = 0f;
            for (int i = 0; i < (int)(120f / TestPlant.TimeStep); i++)
            {
                plant.Step(20f);
                maxRpm = Math.Max(maxRpm, plant.Rotor.Rpm);
            }

            // Documented: +0.6 %. Without the start-up pitch floor it was +24 %.
            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.RatedPower));
            Assert.That(maxRpm / plant.Specs.RatedRotorRpm - 1f, Is.LessThan(0.02f), "peak overspeed");
        }

        [Test]
        public void LullIsBridgedByTheRotorsKineticEnergy()
        {
            var plant = new TestPlant();
            plant.Run(9f, 200f);
            float curveAfterLull = plant.Specs.PowerCurveMW(6f);

            // The generator draws on the spinning rotor: power stays above the new steady state for a while
            // (documented: > 20 s), which is why measured points scatter on both sides of the curve.
            for (int i = 0; i < (int)(10f / TestPlant.TimeStep); i++)
            {
                plant.Step(6f);
                Assert.That(plant.PowerMW, Is.GreaterThan(curveAfterLull), $"power at t = {plant.Time:F2} s");
            }
        }

        [TestCase(5f)]
        [TestCase(7f)]
        [TestCase(9f)]
        public void TurbulentPointsScatterOnBothSidesOfTheCurve(float meanWindSpeed)
        {
            var plant = new TestPlant();
            var wind = new SyntheticWind(meanWindSpeed, 0.12f, seed: 1234);
            for (int i = 0; i < (int)(120f / TestPlant.TimeStep); i++)
                plant.Step(wind.Step(TestPlant.TimeStep));

            int above = 0, below = 0, samples = 0;
            double sumDeviation = 0.0;
            float connectedTime = 0f;
            int stepsPerSample = (int)Math.Round(TestPlant.SampleInterval / TestPlant.TimeStep);

            for (int i = 0; i < (int)(1800f / TestPlant.TimeStep); i++)
            {
                plant.Step(wind.Step(TestPlant.TimeStep));
                Assert.That(float.IsNaN(plant.PowerMW), Is.False, "power is NaN");

                // Like IEC 61400-12 data filtering: skip the first minute after each start-up.
                connectedTime = plant.Controller.IsGeneratorConnected ? connectedTime + TestPlant.TimeStep : 0f;
                if (i % stepsPerSample != 0 || connectedTime < 60f)
                    continue;

                float deviation = plant.PowerMW - plant.Specs.PowerCurveMW(wind.Speed);
                if (deviation > 0.005f)
                    above++;
                else if (deviation < -0.005f)
                    below++;
                sumDeviation += deviation;
                samples++;
            }

            Assert.That(samples, Is.GreaterThan(1000), "connected samples");
            Assert.That(above / (float)samples, Is.GreaterThan(0.3f), "share above the curve");
            Assert.That(below / (float)samples, Is.GreaterThan(0.3f), "share below the curve");
            Assert.That(sumDeviation / samples, Is.EqualTo(0.0).Within(0.05), "mean deviation (MW)");
        }
    }
}
