using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Generator winding as a single-mass (lumped) thermal system with an explicit cooling path:
    ///
    ///   C · dT/dt = Q_loss - G · (T - T_ambient)
    ///
    /// Q_loss is the heat generated in the generator: friction and windage (∝ speed) plus copper losses (∝ current²,
    /// so ∝ (P / P_rated)²). G is the conductance of the cooling path to the air. Written in the specs' units (losses as
    /// the temperature rise they cause with the fan running, G relative to the fan-cooled value):
    ///
    ///   dT/dt = (ΔT_loss - c · (T - T_ambient)) / τ
    ///   ΔT_loss = ΔT_friction · (RPM / RPM_rated) + ΔT_load · (P / P_rated)²
    ///   c = 1 with the fan running, NaturalCoolingFraction with the fan stopped
    ///
    /// Equilibrium T_ambient + ΔT_loss / c, time constant τ / c. With the fan running this is exactly the earlier
    /// first-order model (~118 °C at rated power, τ = 90 s). With the fan stopped the same losses settle five times
    /// higher and five times slower, which is what drives the over-temperature scenario: a physical consequence, not a
    /// scripted temperature ramp. Solved exactly per step (exponential), so any step size is stable.
    /// </summary>
    public sealed class ThermalModel
    {
        private readonly TurbineSpecs specs;

        public float Temperature { get; private set; }

        public ThermalModel(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
            Temperature = specs.AmbientTemperature;
        }

        /// <summary>Temperature the winding would settle at with the current losses and cooling (°C).</summary>
        public float EquilibriumTemperature { get; private set; }

        public void Step(float deltaTime, float rotorRpm, float powerMW, bool coolingFanRunning)
        {
            float speedRatio = rotorRpm / specs.RatedRotorRpm;
            float loadRatio = powerMW / specs.RatedPowerMW;
            float lossTemperatureRise = specs.FrictionTemperatureRise * speedRatio
                                        + specs.LoadTemperatureRise * loadRatio * loadRatio;

            float cooling = coolingFanRunning ? 1f : specs.NaturalCoolingFraction;
            EquilibriumTemperature = specs.AmbientTemperature + lossTemperatureRise / cooling;

            float alpha = 1f - Mathf.Exp(-deltaTime * cooling / specs.ThermalTimeConstant);
            Temperature += (EquilibriumTemperature - Temperature) * alpha;
        }
    }
}
