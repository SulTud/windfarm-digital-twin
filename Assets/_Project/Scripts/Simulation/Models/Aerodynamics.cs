using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Rotor aerodynamics as a power coefficient surface Cp(λ, β), the standard input of a one-mass drivetrain model
    /// (the role of the BEM-derived Cp tables in OpenFAST or the Cp look-up in IEC 61400-27-1 simplified models).
    ///
    ///   Cp(λ, β) = Cp_max · x² · e^(2(1 - x)) · e^(-β / β₀),   x = λ / λ_opt
    ///
    /// The λ shape peaks at exactly (λ_opt, Cp_max) and stays broad like a modern blade (~0.35 at λ 12, ~0.25 at 15).
    /// The pitch term sheds power exponentially; with β₀ = 12 deg the pitch needed to hold rated power follows a
    /// typical schedule (~5 deg at 12 m/s, ~12 at 15, ~19 at 20, ~25 at 25 m/s, close to the NREL 5-MW reference).
    /// The widely used Heier / Simulink analytic surface was rejected: it goes negative above λ ≈ 12, so the rotor
    /// could not run at its minimum speed near cut-in.
    ///
    /// Stateless; valid for β in the operating range (0 to ~35 deg). Start-up and shutdown are scripted sequences in
    /// <see cref="RotorModel"/>, as on a real turbine, so the feathered, stalled range is not needed.
    /// </summary>
    public static class Aerodynamics
    {
        /// <summary>Power coefficient for a tip speed ratio and a pitch angle (deg).</summary>
        public static float PowerCoefficient(TurbineSpecs specs, float tipSpeedRatio, float pitchDegrees)
        {
            float x = Mathf.Max(0f, tipSpeedRatio) / specs.OptimalTipSpeedRatio;
            return specs.MaxPowerCoefficient * x * x * Mathf.Exp(2f * (1f - x)) * PitchFactor(specs, pitchDegrees);
        }

        /// <summary>
        /// Aerodynamic torque on the rotor shaft (N·m). Written as ½·ρ·A·R·v²·(Cp / λ), which stays finite when the
        /// rotor is at rest (Cp / λ → 0), instead of P / ω, which divides by zero.
        /// </summary>
        public static float Torque(TurbineSpecs specs, float omega, float windSpeed, float pitchDegrees)
        {
            if (windSpeed <= 0.01f)
                return 0f;

            float lambda = omega * specs.RotorRadius / windSpeed;
            float x = Mathf.Max(0f, lambda) / specs.OptimalTipSpeedRatio;

            // Cp / λ with the λ² of the shape reduced by one power, so no division by λ.
            float cpOverLambda = specs.MaxPowerCoefficient / specs.OptimalTipSpeedRatio
                                 * x * Mathf.Exp(2f * (1f - x)) * PitchFactor(specs, pitchDegrees);

            return 0.5f * specs.AirDensity * specs.SweptArea * specs.RotorRadius * windSpeed * windSpeed * cpOverLambda;
        }

        /// <summary>Aerodynamic power on the rotor shaft (W).</summary>
        public static float Power(TurbineSpecs specs, float omega, float windSpeed, float pitchDegrees) =>
            Torque(specs, omega, windSpeed, pitchDegrees) * omega;

        private static float PitchFactor(TurbineSpecs specs, float pitchDegrees) =>
            Mathf.Exp(-Mathf.Max(0f, pitchDegrees) / specs.PitchSensitivityAngle);
    }
}
