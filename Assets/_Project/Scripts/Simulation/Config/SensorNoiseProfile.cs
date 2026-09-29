using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Standard deviations of sensor measurement noise. Noise is added only to the published reading and never
    /// corrupts the physics state — a real sensor makes the measurement noisy, not the process itself.
    /// </summary>
    [Serializable]
    public sealed class SensorNoiseProfile
    {
        [field: SerializeField, Min(0f), Tooltip("Anemometer noise σ (m/s).")]
        public float WindSpeedStdDev { get; private set; } = 0.15f;

        [field: SerializeField, Min(0f), Tooltip("Encoder noise σ (RPM).")]
        public float RotorRpmStdDev { get; private set; } = 0.04f;

        // A digital absolute encoder: its error is the resolution, not random noise. A parked blade reads a constant
        // value (Gaussian noise made the 70 deg park reading flicker 69.98 / 70.00).
        [field: SerializeField, Min(0f), Tooltip("Blade pitch encoder resolution (deg). Readings are rounded to it; 0 = exact.")]
        public float PitchResolution { get; private set; } = 0.1f;

        [field: SerializeField, Min(0f), Tooltip("PT100 temperature sensor noise σ (°C).")]
        public float TemperatureStdDev { get; private set; } = 0.1f;

        [field: SerializeField, Range(0f, 0.05f), Tooltip("Power meter noise σ as a fraction of rated power.")]
        public float PowerStdDevFraction { get; private set; } = 0.004f;
    }
}
