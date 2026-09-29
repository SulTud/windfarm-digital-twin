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

        // Blades + hub + generator referred to the rotor shaft. The NREL 5-MW rotor has ~3.5e7 kg·m² (61.5 m blades);
        // mass scales steeply with length, so ~2.4e7 for 54.6 m blades. Under optimal torque control this gives a
        // response time τ = J·ω² / (3·P) of ~10 s at 5 m/s and ~5 s near rated wind.
        [field: SerializeField, Min(1e5f), Tooltip("Rotational inertia of rotor and drivetrain on the rotor shaft (kg·m²). " +
                                                   "Heavier rotors react more slowly to the wind and store more energy.")]
        public float RotorInertia { get; private set; } = 2.4e7f;

        [field: SerializeField, Min(0.1f), Tooltip("Time constant of the start-up (spin-up to generator sync speed) and shutdown " +
                                                   "sequences (s). While producing, the rotor follows the torque balance instead.")]
        public float RotorTimeConstant { get; private set; } = 7f;

        [field: Header("Pitch")]
        [field: SerializeField, Min(0.5f), Tooltip("Pitch angle that reduces the power coefficient by a factor e (deg). Sets how strongly " +
                                                   "pitching sheds power; 12 gives a typical schedule (~5 deg at 12 m/s, ~25 deg at 25 m/s).")]
        public float PitchSensitivityAngle { get; private set; } = 12f;

        [field: SerializeField, Min(0.1f), Tooltip("Maximum pitch rate of the blade drives (deg/s). Real drives move about 5-10 deg/s.")]
        public float MaxPitchRate { get; private set; } = 8f;

        [field: SerializeField, Range(0f, 90f), Tooltip("Pitch while idle below cut-in (deg). Real turbines park near feather.")]
        public float IdlePitch { get; private set; } = 70f;

        [field: SerializeField, Range(0f, 90f), Tooltip("Pitch during storm shutdown (deg). 90 = fully feathered, chord parallel to the wind.")]
        public float FeatherPitch { get; private set; } = 90f;

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
        /// Wind speed at which the steady-state curve reaches rated power (m/s, ~10.6 with the defaults).
        /// </summary>
        public float RatedWindSpeed => SteadyStatePowerCurve.RatedWindSpeed(this);

        /// <summary>
        /// Steady-state power curve (MW), the curve a datasheet shows, computed from the simulator's own aerodynamics
        /// and control law (see <see cref="SteadyStatePowerCurve"/>). Zero outside cut-in..cut-out.
        /// </summary>
        public float PowerCurveMW(float windSpeed) => SteadyStatePowerCurve.PowerMW(this, windSpeed);

        /// <summary>Equilibrium winding temperature at rated speed and rated power (°C).</summary>
        public float RatedGeneratorTemperature => AmbientTemperature + FrictionTemperatureRise + LoadTemperatureRise;
    }
}
