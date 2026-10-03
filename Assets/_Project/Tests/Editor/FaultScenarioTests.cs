using System;
using NUnit.Framework;
using WindFarm.Simulation;

namespace WindFarm.Tests
{
    /// <summary>
    /// The cooling fan failure scenario end to end through the whole physics chain: rising temperature, warning,
    /// derating, trip, repair, restart, and the lost production compared with a healthy twin on the same wind.
    /// </summary>
    public class FaultScenarioTests
    {
        private const float WindSpeed = 12f;

        private static TestPlant SettledPlant()
        {
            var plant = new TestPlant();
            plant.Run(WindSpeed, 600f);
            return plant;
        }

        [Test]
        public void EscalatesFromWarningToDerateToTrip()
        {
            var plant = SettledPlant();
            plant.CoolingFanRunning = false;

            double warning = -1, alarm = -1, trip = -1, start = plant.Time;
            float maxRpmWhileDerated = 0f;
            float minPowerWhileDerated = float.MaxValue;
            while (trip < 0 && plant.Time - start < 300.0)
            {
                plant.Step(WindSpeed);
                double t = plant.Time - start;
                TurbineAlarms alarms = plant.Protection.Alarms;

                if (warning < 0 && (alarms & TurbineAlarms.GeneratorTemperatureWarning) != 0) warning = t;
                if (alarm < 0 && (alarms & TurbineAlarms.GeneratorTemperatureAlarm) != 0) alarm = t;
                if (trip < 0 && plant.State == TurbineOperatingState.FaultStop) trip = t;

                if (plant.Controller.IsGeneratorConnected && plant.Protection.PowerLimitMW < plant.Specs.RatedPowerMW)
                {
                    maxRpmWhileDerated = Math.Max(maxRpmWhileDerated, plant.Rotor.Rpm);
                    minPowerWhileDerated = Math.Min(minPowerWhileDerated, plant.PowerMW);
                    Assert.That(plant.PowerMW, Is.LessThanOrEqualTo(plant.Protection.PowerLimitMW + 1e-3f), "power above the limit");
                }
            }

            // Documented at 12 m/s (turbulent): warning ~30 s, alarm ~41 s, trip ~63 s after the failure.
            Assert.That(warning, Is.GreaterThan(0.0), "warning raised");
            Assert.That(alarm, Is.GreaterThan(warning), "alarm after the warning");
            Assert.That(trip, Is.GreaterThan(alarm), "trip after the alarm");
            Assert.That(trip, Is.LessThan(120.0), "trip time (s)");
            Assert.That(minPowerWhileDerated, Is.LessThan(plant.Specs.RatedPowerMW * 0.75f), "derating reduced the power");
            Assert.That(maxRpmWhileDerated / plant.Specs.RatedRotorRpm - 1f, Is.LessThan(0.05f), "overspeed while derated");
        }

        [Test]
        public void StaysStoppedWhileTheFanIsDead()
        {
            var plant = SettledPlant();
            plant.CoolingFanRunning = false;
            while (plant.State != TurbineOperatingState.FaultStop)
                plant.Step(WindSpeed);

            // The winding cools below the restart temperature within minutes, but a restart with a dead fan would
            // only trip again: it must not happen at any step.
            double tripTime = plant.Time;
            while (plant.Time - tripTime < 900.0)
            {
                plant.Step(WindSpeed);
                Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.FaultStop), $"state {plant.Time - tripTime:F1} s after the trip");
            }

            Assert.That(plant.Thermal.Temperature, Is.LessThan(plant.Specs.GeneratorRestartTemperature), "cooled down meanwhile");
            Assert.That(plant.PowerMW, Is.EqualTo(0f));
        }

        [Test]
        public void RestartsAndReturnsToRatedAfterTheRepair()
        {
            var plant = SettledPlant();
            plant.CoolingFanRunning = false;
            while (plant.State != TurbineOperatingState.FaultStop)
                plant.Step(WindSpeed);

            plant.Run(WindSpeed, 30f);
            plant.CoolingFanRunning = true;

            // Documented: cools to 130 °C in ~12 s, restarts, back at rated ~40 s later.
            plant.Run(WindSpeed, 120f);
            Assert.That(plant.State, Is.EqualTo(TurbineOperatingState.RatedPower));
            Assert.That(plant.Protection.Alarms, Is.EqualTo(TurbineAlarms.None));
        }

        [Test]
        public void LostProductionMatchesAHealthyTwin()
        {
            var faulted = SettledPlant();
            var healthy = SettledPlant();
            var meter = new LostProductionMeter(faulted.Specs);
            int stepsPerSample = (int)Math.Round(TestPlant.SampleInterval / TestPlant.TimeStep);

            faulted.CoolingFanRunning = false;
            double tripTime = -1, eventStart = -1, eventEnd = -1;
            double faultedAtStart = 0, healthyAtStart = 0, faultedAtEnd = 0, healthyAtEnd = 0;
            bool wasActive = false;

            for (int i = 0; i < (int)(900f / TestPlant.TimeStep) && eventEnd < 0; i++)
            {
                faulted.Step(WindSpeed);
                healthy.Step(WindSpeed);

                if (tripTime < 0 && faulted.State == TurbineOperatingState.FaultStop) tripTime = faulted.Time;
                if (tripTime >= 0 && !faulted.CoolingFanRunning && faulted.Time >= tripTime + 30.0)
                    faulted.CoolingFanRunning = true;

                if (i % stepsPerSample != 0)
                    continue;

                meter.Add(faulted.Sample(WindSpeed));
                if (meter.EventActive && !wasActive)
                {
                    eventStart = faulted.Time;
                    faultedAtStart = faulted.EnergyMWh;
                    healthyAtStart = healthy.EnergyMWh;
                }
                if (!meter.EventActive && wasActive)
                {
                    eventEnd = faulted.Time;
                    faultedAtEnd = faulted.EnergyMWh;
                    healthyAtEnd = healthy.EnergyMWh;
                }
                wasActive = meter.EventActive;
            }

            Assert.That(eventStart, Is.GreaterThan(0.0), "event started");
            Assert.That(eventEnd, Is.GreaterThan(eventStart), "event ended after the repair");

            // Documented at constant 12 m/s: meter 55.6 kWh, twin counterfactual 55.6 kWh.
            double twinLossMWh = (healthyAtEnd - healthyAtStart) - (faultedAtEnd - faultedAtStart);
            Assert.That(twinLossMWh, Is.GreaterThan(0.01), "the twin lost something");
            Assert.That(meter.LostEnergyMWh, Is.EqualTo(twinLossMWh).Within(twinLossMWh * 0.05), "meter vs twin (MWh)");
        }
    }
}
