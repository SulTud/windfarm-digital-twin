using NUnit.Framework;
using WindFarm.Simulation;

namespace WindFarm.Tests
{
    /// <summary>
    /// Supervisory state machine: cut-in, low-wind disconnect, storm shutdown and restart.
    /// </summary>
    public class TurbineControllerTests
    {
        [Test]
        public void StaysIdleBelowCutIn()
        {
            var plant = new TestPlant();
            plant.Run(2f, 120f);

            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.Idle));
            Assert.That(plant.PowerMW, Is.EqualTo(0f));
        }

        [Test]
        public void StartsProducingAfterTheConfirmationTime()
        {
            var plant = new TestPlant();
            plant.Run(8f, plant.Specs.StateConfirmTime - 0.5f);
            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.Idle), "before the confirmation time");

            plant.Run(8f, 1f);
            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.Producing), "after the confirmation time");
        }

        [Test]
        public void DisconnectsWithinSecondsWhenTheWindDrops()
        {
            var plant = new TestPlant();
            plant.Run(8f, 200f);

            // Acts on the instant wind, so it does not wait ~80 s for the 30 s average to fall.
            plant.Run(2f, plant.Specs.StateConfirmTime + 2f);
            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.Idle));
        }

        [Test]
        public void ShutsDownAboveCutOutAndFeathers()
        {
            var plant = new TestPlant();
            plant.Run(26f, 60f);

            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.StormShutdown));
            Assert.That(plant.Pitch.Angle, Is.EqualTo(plant.Specs.FeatherPitch).Within(0.1f));
            Assert.That(plant.PowerMW, Is.EqualTo(0f));
        }

        [Test]
        public void StrongGustTripsWithoutWaitingForTheAverage()
        {
            var plant = new TestPlant();
            plant.Run(15f, 200f);

            plant.Step(29f);
            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.StormShutdown));
        }

        [Test]
        public void RestartsOnlyBelowTheRestartWindSpeedAfterTheMinimumStop()
        {
            var plant = new TestPlant();
            plant.Run(26f, 60f);

            // The 30 s average is still above the restart speed (20 m/s) 10 s after the wind drops.
            plant.Run(15f, 10f);
            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.StormShutdown), "10 s after the wind dropped");

            plant.Run(15f, 50f);
            Assert.That(plant.Controller.IsGeneratorConnected, Is.True, "60 s after the wind dropped");
        }
    }
}
