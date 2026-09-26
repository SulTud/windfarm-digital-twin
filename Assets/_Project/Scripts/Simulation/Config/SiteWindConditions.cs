using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Sahanın rüzgar karakteristiği. Türbinden bağımsızdır; aynı saha profili farklı türbinlerle kullanılabilir.
    /// </summary>
    [Serializable]
    public sealed class SiteWindConditions
    {
        [field: Header("Mean Wind")]
        [field: SerializeField, Min(0f), Tooltip("Uzun dönem ortalama rüzgar hızı (m/s).")]
        public float MeanWindSpeed { get; set; } = 9f;

        [field: SerializeField, Min(0f), Tooltip("Hava durumu kaynaklı yavaş salınımın genliği (m/s).")]
        public float DriftAmplitude { get; private set; } = 3.5f;

        [field: SerializeField, Min(1f), Tooltip("Yavaş salınımın karakteristik periyodu (s).")]
        public float DriftPeriod { get; private set; } = 180f;

        [field: Header("Turbulence")]
        [field: SerializeField, Range(0f, 0.5f), Tooltip("Türbülans yoğunluğu TI = σ / ortalama. IEC sınıf B için ~0.14.")]
        public float TurbulenceIntensity { get; private set; } = 0.12f;

        [field: SerializeField, Min(0.1f), Tooltip("Türbülansın korelasyon süresi (s).")]
        public float TurbulenceTimeConstant { get; private set; } = 4f;

        [field: Header("Gusts")]
        [field: SerializeField, Min(0f), Tooltip("Dakikada ortalama ani rüzgar (gust) sayısı.")]
        public float GustsPerMinute { get; private set; } = 0.6f;

        [field: SerializeField, Tooltip("Gust genlik aralığı (m/s).")]
        public Vector2 GustAmplitudeRange { get; private set; } = new Vector2(2f, 6f);

        [field: SerializeField, Tooltip("Gust süre aralığı (s).")]
        public Vector2 GustDurationRange { get; private set; } = new Vector2(6f, 14f);
    }
}
