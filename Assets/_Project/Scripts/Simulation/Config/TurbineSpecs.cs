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

        [field: SerializeField, Min(0f), Tooltip("A non-protective state change must be requested continuously for this long before " +
                                                  "it happens (s). Debounces turbulence around the thresholds. Storm stops are immediate.")]
        public float StateConfirmTime { get; private set; } = 3f;

        [field: SerializeField, Min(0f), Tooltip("Minimum stop after a storm shutdown before a restart is allowed (s). " +
                                                  "Real turbines wait about 10 min; shortened for the demo.")]
        public float StormRestartDelay { get; private set; } = 10f;

        // The modelled temperature is the stator winding temperature (PT100 sensors embedded in the winding), the usual
        // SCADA generator signal. Its limit is set by the insulation class (IEC 60085): wind generators typically use
        // Class F insulation (155 °C) operated at Class B temperature rise, i.e. roughly 110-120 °C at rated power.
        [field: Header("Thermal")]
        [field: SerializeField, Tooltip("Ambient temperature inside the nacelle (°C).")]
        public float AmbientTemperature { get; private set; } = 15f;

        [field: SerializeField, Min(0f), Tooltip("Winding temperature rise from friction and windage at rated speed with no load (°C).")]
        public float FrictionTemperatureRise { get; private set; } = 8f;

        [field: SerializeField, Min(0f), Tooltip("Additional winding temperature rise from copper (I²R) losses at rated power (°C). " +
                                                  "With the defaults the winding settles at ~118 °C at rated power.")]
        public float LoadTemperatureRise { get; private set; } = 95f;

        [field: SerializeField, Min(0.1f), Tooltip("Generator thermal time constant (s). Real value is 20-40 min; shortened for the demo.")]
        public float ThermalTimeConstant { get; private set; } = 90f;

        // Typical setpoints for Class F insulation. Real values are OEM specific and usually not published.
        [field: Header("Generator Protection")]
        [field: SerializeField, Tooltip("Winding temperature that raises a warning (°C). Operation continues.")]
        public float GeneratorWarningTemperature { get; private set; } = 140f;

        [field: SerializeField, Tooltip("Winding temperature that raises an alarm and derates the output (°C).")]
        public float GeneratorAlarmTemperature { get; private set; } = 150f;

        [field: SerializeField, Tooltip("Winding temperature that trips (stops) the turbine (°C). Class F insulation limit.")]
        public float GeneratorTripTemperature { get; private set; } = 155f;

        [field: Header("Environment")]
        [field: SerializeField, Min(0.5f), Tooltip("Air density ρ (kg/m³).")]
        public float AirDensity { get; private set; } = 1.225f;

        public float SweptArea => Mathf.PI * RotorRadius * RotorRadius;
        public float RatedPowerWatts => RatedPowerMW * 1_000_000f;

        /// <summary>
        /// Wind speed at which rated power is reached (m/s): solves P_rated = ½·ρ·A·Cp·η·v³ (~10.6 m/s with the defaults).
        /// </summary>
        public float RatedWindSpeed =>
            Mathf.Pow(RatedPowerWatts / (0.5f * AirDensity * SweptArea * MaxPowerCoefficient * DrivetrainEfficiency), 1f / 3f);

        /// <summary>
        /// Steady-state power curve (MW), the curve a datasheet shows: zero outside cut-in..cut-out, otherwise
        /// ½·ρ·A·Cp·η·v³ capped at rated power. Same formula as <see cref="PowerModel"/> once the rotor has settled.
        /// </summary>
        public float PowerCurveMW(float windSpeed)
        {
            if (windSpeed < CutInWindSpeed || windSpeed > CutOutWindSpeed)
                return 0f;

            float watts = 0.5f * AirDensity * SweptArea * MaxPowerCoefficient * DrivetrainEfficiency
                          * windSpeed * windSpeed * windSpeed;
            return Mathf.Min(watts, RatedPowerWatts) / 1_000_000f;
        }

        /// <summary>Equilibrium winding temperature at rated speed and rated power (°C).</summary>
        public float RatedGeneratorTemperature => AmbientTemperature + FrictionTemperatureRise + LoadTemperatureRise;
    }
}
