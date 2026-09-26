using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Sensör ölçüm gürültüsünün standart sapmaları. Gürültü yalnızca yayınlanan okumaya eklenir,
    /// fizik durumunu (state) bozmaz — gerçek bir sensör de süreci değil, ölçümü gürültülü yapar.
    /// </summary>
    [Serializable]
    public sealed class SensorNoiseProfile
    {
        [field: SerializeField, Min(0f), Tooltip("Anemometre gürültüsü σ (m/s).")]
        public float WindSpeedStdDev { get; private set; } = 0.15f;

        [field: SerializeField, Min(0f), Tooltip("Enkoder gürültüsü σ (RPM).")]
        public float RotorRpmStdDev { get; private set; } = 0.04f;

        [field: SerializeField, Min(0f), Tooltip("PT100 sıcaklık sensörü gürültüsü σ (°C).")]
        public float TemperatureStdDev { get; private set; } = 0.1f;

        [field: SerializeField, Range(0f, 0.05f), Tooltip("Güç ölçer gürültüsü σ, nominal gücün oranı olarak.")]
        public float PowerStdDevFraction { get; private set; } = 0.004f;
    }
}
