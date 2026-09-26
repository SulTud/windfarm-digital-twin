using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Jeneratörü tek kütleli termal sistem (lumped thermal model) olarak modeller.
    ///
    /// Denge sıcaklığı kayıplardan gelir:
    ///   T_denge = T_ortam + ΔT_sürtünme · (RPM / RPM_nominal) + ΔT_yük · (P / P_nominal)²
    /// Yük terimi kareseldir çünkü bakır kayıpları akımın karesiyle (I²R) orantılıdır.
    /// Gerçek sıcaklık dengeye termal zaman sabitiyle yavaşça yaklaşır; güç düştüğünde de yavaşça soğur.
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
