using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Elektrik gücünü rotor devrinden hesaplar (durumsuz / stateless).
    ///
    /// Kısmi yük bölgesinde gerçek türbinlerde kullanılan standart tork kontrol yasası uygulanır:
    ///   T = k·ω²  →  P = k·ω³,   k = ½·ρ·π·R⁵·Cp_max / λ_opt³
    /// Bu sayede güç rüzgarı değil rotoru takip eder: rotor yavaşça hızlanırken güç de onunla birlikte artar.
    ///
    /// Fiziksel sınırlar:
    /// - Rüzgardaki mevcut güç (½·ρ·A·Cp·v³) aşılamaz — rüzgar aniden düşerse güç de düşer.
    /// - Nominal gücün üstüne çıkılamaz (pitch kontrolü fazlayı döker).
    /// </summary>
    public sealed class PowerModel
    {
        private const float RpmToRadPerSec = 2f * Mathf.PI / 60f;

        private readonly TurbineSpecs specs;

        public PowerModel(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
        }

        public float CalculateElectricalPowerMW(float rotorRpm, float windSpeed, bool generatorConnected)
        {
            if (!generatorConnected || rotorRpm <= 0f)
                return 0f;

            float omega = rotorRpm * RpmToRadPerSec;
            float tipSpeedRatioCubed = Mathf.Pow(specs.OptimalTipSpeedRatio, 3f);
            float torqueGain = 0.5f * specs.AirDensity * Mathf.PI * Mathf.Pow(specs.RotorRadius, 5f)
                               * specs.MaxPowerCoefficient / tipSpeedRatioCubed;

            float controllerPower = torqueGain * omega * omega * omega;
            float availableWindPower = 0.5f * specs.AirDensity * specs.SweptArea
                                       * specs.MaxPowerCoefficient * windSpeed * windSpeed * windSpeed;

            float mechanicalPower = Mathf.Min(controllerPower, availableWindPower);
            float electricalPower = Mathf.Min(mechanicalPower * specs.DrivetrainEfficiency, specs.RatedPowerWatts);

            return electricalPower / 1_000_000f;
        }
    }
}
