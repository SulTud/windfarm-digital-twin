using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Türbinin mekanik / elektriksel katalog değerleri.
    /// Varsayılanlar 112 m rotor çaplı, 3 MW sınıfı jenerik bir kara türbinini temsil eder.
    /// </summary>
    [Serializable]
    public sealed class TurbineSpecs
    {
        [field: Header("Rotor")]
        [field: SerializeField, Min(1f), Tooltip("Rotor yarıçapı (m).")]
        public float RotorRadius { get; private set; } = 56f;

        [field: SerializeField, Min(0f), Tooltip("Jeneratörün şebekeye bağlı kalabildiği minimum rotor devri (RPM).")]
        public float MinRotorRpm { get; private set; } = 6.2f;

        [field: SerializeField, Min(0.1f), Tooltip("Nominal rotor devri (RPM). Pitch kontrolü devri burada sabitler.")]
        public float RatedRotorRpm { get; private set; } = 14f;

        [field: SerializeField, Min(0.1f), Tooltip("Optimum uç hız oranı λ = ωR / v.")]
        public float OptimalTipSpeedRatio { get; private set; } = 7.5f;

        [field: SerializeField, Range(0.1f, 0.593f), Tooltip("Maksimum güç katsayısı Cp (Betz limiti 0.593).")]
        public float MaxPowerCoefficient { get; private set; } = 0.45f;

        [field: SerializeField, Min(0.1f), Tooltip("Rotor ataleti zaman sabiti (s). Büyüdükçe rotor rüzgara daha yavaş tepki verir.")]
        public float RotorTimeConstant { get; private set; } = 8f;

        [field: Header("Generator")]
        [field: SerializeField, Min(0.1f), Tooltip("Nominal elektrik gücü (MW).")]
        public float RatedPowerMW { get; private set; } = 3f;

        [field: SerializeField, Range(0.5f, 1f), Tooltip("Dişli kutusu + jeneratör + çevirici toplam verimi.")]
        public float DrivetrainEfficiency { get; private set; } = 0.94f;

        [field: Header("Operating Envelope")]
        [field: SerializeField, Min(0f), Tooltip("Üretime başlama rüzgar hızı (m/s).")]
        public float CutInWindSpeed { get; private set; } = 3f;

        [field: SerializeField, Min(0f), Tooltip("Fırtına korumasıyla durdurma rüzgar hızı (m/s).")]
        public float CutOutWindSpeed { get; private set; } = 25f;

        [field: SerializeField, Min(0f), Tooltip("Fırtına sonrası yeniden devreye girme hızı (m/s). Histerezis sağlar.")]
        public float RestartWindSpeed { get; private set; } = 20f;

        [field: SerializeField, Min(0.1f), Tooltip("Kontrolcünün karar verirken kullandığı rüzgar ortalama süresi (s).")]
        public float ControlAveragingTime { get; private set; } = 30f;

        [field: Header("Thermal")]
        [field: SerializeField, Tooltip("Nasel içi ortam sıcaklığı (°C).")]
        public float AmbientTemperature { get; private set; } = 15f;

        [field: SerializeField, Min(0f), Tooltip("Nominal devirde yüksüz sürtünme/havalandırma kaynaklı sıcaklık artışı (°C).")]
        public float FrictionTemperatureRise { get; private set; } = 6f;

        [field: SerializeField, Min(0f), Tooltip("Nominal güçte bakır (I²R) kayıplarından doğan ek sıcaklık artışı (°C).")]
        public float LoadTemperatureRise { get; private set; } = 55f;

        [field: SerializeField, Min(0.1f), Tooltip("Jeneratör termal zaman sabiti (s). Gerçekte 20-40 dk; demo için kısaltıldı.")]
        public float ThermalTimeConstant { get; private set; } = 90f;

        [field: Header("Environment")]
        [field: SerializeField, Min(0.5f), Tooltip("Hava yoğunluğu ρ (kg/m³).")]
        public float AirDensity { get; private set; } = 1.225f;

        public float SweptArea => Mathf.PI * RotorRadius * RotorRadius;
        public float RatedPowerWatts => RatedPowerMW * 1_000_000f;
    }
}
