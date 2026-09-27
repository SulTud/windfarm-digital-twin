using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Mechanical / electrical datasheet values of the turbine.
    /// Defaults represent a generic 3 MW class onshore turbine with a 112 m rotor diameter.
    /// </summary>
    [Serializable]
    public sealed class TurbineSpecs
    {
        [field: Header("Rotor")]
        [field: SerializeField, Min(1f), Tooltip("Rotor radius (m).")]
        public float RotorRadius { get; private set; } = 56f;

        [field: SerializeField, Min(0f), Tooltip("Minimum rotor speed at which the generator can stay grid-connected (RPM).")]
        public float MinRotorRpm { get; private set; } = 6.2f;

        [field: SerializeField, Min(0.1f), Tooltip("Rated rotor speed (RPM). Pitch control holds the rotor at this speed.")]
        public float RatedRotorRpm { get; private set; } = 14f;

        [field: SerializeField, Min(0.1f), Tooltip("Optimal tip speed ratio λ = ωR / v.")]
        public float OptimalTipSpeedRatio { get; private set; } = 7.5f;

        [field: SerializeField, Range(0.1f, 0.593f), Tooltip("Maximum power coefficient Cp (Betz limit is 0.593).")]
        public float MaxPowerCoefficient { get; private set; } = 0.45f;

        // τ = J·ω² / (3·P) for a rotor under optimal torque control (J ≈ 2.4e7 kg·m² for this class).
        // Gives ~10 s at 5 m/s, ~7 s at 8 m/s and ~5 s near rated wind; 7 s represents a typical mean wind.
        [field: SerializeField, Min(0.1f), Tooltip("Rotor inertia time constant (s). Higher values make the rotor respond more slowly to the wind. " +
                                                   "Realistic range for a 3 MW turbine is 5-10 s.")]
        public float RotorTimeConstant { get; private set; } = 7f;

        [field: Header("Generator")]
        [field: SerializeField, Min(0.1f), Tooltip("Rated electrical power (MW).")]
        public float RatedPowerMW { get; private set; } = 3f;

        [field: SerializeField, Range(0.5f, 1f), Tooltip("Combined efficiency of gearbox, generator and power converter.")]
        public float DrivetrainEfficiency { get; private set; } = 0.94f;

        [field: Header("Operating Envelope")]
        [field: SerializeField, Min(0f), Tooltip("Wind speed at which power production starts (m/s).")]
        public float CutInWindSpeed { get; private set; } = 3f;

        [field: SerializeField, Min(0f), Tooltip("Wind speed at which the storm protection shuts the turbine down (m/s).")]
        public float CutOutWindSpeed { get; private set; } = 25f;

        [field: SerializeField, Min(0f), Tooltip("Wind speed at which the turbine restarts after a storm shutdown (m/s). Provides hysteresis.")]
        public float RestartWindSpeed { get; private set; } = 20f;

        [field: SerializeField, Min(0.1f), Tooltip("Wind averaging time used by the controller for its decisions (s).")]
        public float ControlAveragingTime { get; private set; } = 30f;

        [field: Header("Thermal")]
        [field: SerializeField, Tooltip("Ambient temperature inside the nacelle (°C).")]
        public float AmbientTemperature { get; private set; } = 15f;

        [field: SerializeField, Min(0f), Tooltip("Temperature rise from friction and windage at rated speed with no load (°C).")]
        public float FrictionTemperatureRise { get; private set; } = 6f;

        [field: SerializeField, Min(0f), Tooltip("Additional temperature rise from copper (I²R) losses at rated power (°C).")]
        public float LoadTemperatureRise { get; private set; } = 55f;

        [field: SerializeField, Min(0.1f), Tooltip("Generator thermal time constant (s). Real value is 20-40 min; shortened for the demo.")]
        public float ThermalTimeConstant { get; private set; } = 90f;

        [field: Header("Environment")]
        [field: SerializeField, Min(0.5f), Tooltip("Air density ρ (kg/m³).")]
        public float AirDensity { get; private set; } = 1.225f;

        public float SweptArea => Mathf.PI * RotorRadius * RotorRadius;
        public float RatedPowerWatts => RatedPowerMW * 1_000_000f;
    }
}
