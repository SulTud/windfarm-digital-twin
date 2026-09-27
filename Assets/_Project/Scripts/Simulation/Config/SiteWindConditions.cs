using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Wind characteristics of the site. Independent of the turbine; the same site profile can be used with different turbines.
    /// </summary>
    [Serializable]
    public sealed class SiteWindConditions
    {
        [field: Header("Mean Wind")]
        [field: SerializeField, Min(0f), Tooltip("Long-term mean wind speed (m/s).")]
        public float MeanWindSpeed { get; set; } = 9f;

        [field: SerializeField, Min(0f), Tooltip("Amplitude of the slow, weather-driven drift (m/s).")]
        public float DriftAmplitude { get; private set; } = 3.5f;

        [field: SerializeField, Min(1f), Tooltip("Characteristic period of the slow drift (s).")]
        public float DriftPeriod { get; private set; } = 180f;

        [field: Header("Turbulence")]
        [field: SerializeField, Range(0f, 0.5f), Tooltip("Turbulence intensity TI = σ / mean. About 0.14 for IEC class B.")]
        public float TurbulenceIntensity { get; private set; } = 0.12f;

        [field: SerializeField, Min(0.1f), Tooltip("Correlation time of the turbulence (s).")]
        public float TurbulenceTimeConstant { get; private set; } = 4f;

        [field: Header("Gusts")]
        [field: SerializeField, Min(0f), Tooltip("Average number of gusts per minute.")]
        public float GustsPerMinute { get; private set; } = 0.6f;

        [field: SerializeField, Tooltip("Gust amplitude range (m/s).")]
        public Vector2 GustAmplitudeRange { get; private set; } = new Vector2(2f, 6f);

        [field: SerializeField, Tooltip("Gust duration range (s).")]
        public Vector2 GustDurationRange { get; private set; } = new Vector2(6f, 14f);
    }
}
