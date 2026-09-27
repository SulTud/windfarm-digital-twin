using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Models the generator as a single-mass (lumped) thermal system.
    ///
    /// The equilibrium temperature comes from the losses:
    ///   T_eq = T_ambient + ΔT_friction · (RPM / RPM_rated) + ΔT_load · (P / P_rated)²
    /// The load term is quadratic because copper losses scale with the square of the current (I²R).
    /// The actual temperature approaches equilibrium slowly with the thermal time constant, and cools down slowly when power drops.
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

        public void Step(float deltaTime, float rotorRpm, float powerMW)
        {
            float speedRatio = rotorRpm / specs.RatedRotorRpm;
            float loadRatio = powerMW / specs.RatedPowerMW;

            float equilibrium = specs.AmbientTemperature
                                + specs.FrictionTemperatureRise * speedRatio
                                + specs.LoadTemperatureRise * loadRatio * loadRatio;

            float alpha = 1f - Mathf.Exp(-deltaTime / specs.ThermalTimeConstant);
            Temperature += (equilibrium - Temperature) * alpha;
        }
    }
}
