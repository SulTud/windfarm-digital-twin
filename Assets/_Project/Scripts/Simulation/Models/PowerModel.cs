using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Generator torque control and electrical power (stateless), following the variable-speed control structure of
    /// the NREL 5-MW reference turbine (Jonkman et al., 2009), the de facto reference in wind turbine simulation:
    ///
    ///   Region 1.5  min speed .. +5 %   linear torque ramp from zero, so the rotor never runs below its minimum
    ///                                   speed while connected (λ is then above optimum at low wind)
    ///   Region 2    above that          T = k·ω², k = ½·ρ·π·R⁵·Cp_max / λ_opt³ (holds λ at λ_opt in steady state)
    ///   Region 3    k·ω³ >= rated       constant power, T = P_rated / ω; the pitch controller holds the speed
    ///
    /// Torques are on the rotor (low-speed) shaft. Unlike the earlier model, power is NOT capped by the power in the
    /// wind: when the wind drops, the generator keeps drawing k·ω³ while the heavy rotor slows down, so for a few
    /// seconds the output is above the steady-state curve, fed by the rotor's kinetic energy (~26 MJ at rated speed).
    /// Real SCADA data scatters on both sides of the power curve for this reason.
    /// </summary>
    public static class PowerModel
    {
        public const float RpmToRadPerSec = 2f * Mathf.PI / 60f;

        // Region 1.5 width above the minimum speed. Narrow keeps the steady state close to λ_opt; 5 % is still gentle
        // enough for the 0.05 s explicit physics step (torque slope / inertia · step ≈ 0.03).
        private const float Region15Width = 0.05f;

        /// <summary>Optimal-mode gain k (N·m·s²).</summary>
        public static float OptimalTorqueGain(TurbineSpecs specs)
        {
            float lambda = specs.OptimalTipSpeedRatio;
            return 0.5f * specs.AirDensity * Mathf.PI * Mathf.Pow(specs.RotorRadius, 5f) * specs.MaxPowerCoefficient
                   / (lambda * lambda * lambda);
        }

        /// <summary>Mechanical power the generator may take at rated output (W): rated electrical / efficiency.</summary>
        public static float RatedMechanicalPower(TurbineSpecs specs) => specs.RatedPowerWatts / specs.DrivetrainEfficiency;

        /// <summary>Generator torque demanded by the converter at a rotor speed (N·m on the rotor shaft).</summary>
        public static float GeneratorTorque(TurbineSpecs specs, float omega)
        {
            float minOmega = specs.MinRotorRpm * RpmToRadPerSec;
            if (omega <= minOmega)
                return 0f;

            float k = OptimalTorqueGain(specs);
            float ratedPower = RatedMechanicalPower(specs);

            // Region 3: constant power once the optimal law would exceed it.
            if (k * omega * omega * omega >= ratedPower)
                return ratedPower / omega;

            // Region 1.5: ramp up to the optimal law.
            float rampEnd = minOmega * (1f + Region15Width);
            if (omega < rampEnd)
                return k * rampEnd * rampEnd * (omega - minOmega) / (rampEnd - minOmega);

            return k * omega * omega;
        }

        /// <summary>Electrical output (MW) for a rotor speed and the generator torque at it.</summary>
        public static float ElectricalPowerMW(TurbineSpecs specs, float omega, float generatorTorque)
        {
            float watts = Mathf.Max(0f, generatorTorque * omega) * specs.DrivetrainEfficiency;
            return Mathf.Min(watts, specs.RatedPowerWatts) / 1_000_000f;
        }
    }
}
