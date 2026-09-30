using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Collective blade pitch: a PI speed controller above rated plus the supervisory pitch positions, driven by a
    /// rate-limited pitch actuator (like a MoveTowards with a max speed).
    ///
    ///   Producing / RatedPower   PI on the rotor speed error (ω - ω_rated). Below rated speed the integrator unwinds
    ///                            and the blades sit at fine pitch (0 deg, maximum Cp); above it they pitch toward
    ///                            feather to shed aerodynamic power and hold the speed.
    ///   Idle                     park pitch (near feather, the rotor idles without load)
    ///   StormShutdown, FaultStop full feather (90 deg), the blades act as an aerodynamic brake
    ///
    /// While the controller derates, the generator torque is capped, the rotor speeds up to rated speed and this PI
    /// holds it there, as in region 3. The loop gain drops with the aerodynamic torque (half at 50 % power), which
    /// slows the loop but keeps it stable.
    ///
    /// Gains follow the NREL 5-MW design method (Jonkman et al., 2009): the speed loop is a second-order system with
    /// natural frequency 0.6 rad/s and damping 0.7:
    ///   Kp = 2·ζ·ω_n·J / S,   Ki = ω_n²·J / S,   S = -∂T_aero/∂β at rated speed and power
    /// With the exponential pitch term of <see cref="Aerodynamics"/>, S = P_rated,mech / (β₀·ω_rated) is the same at every
    /// wind speed, so no gain scheduling is needed (the NREL rotor needs it because its sensitivity grows with pitch).
    ///
    /// Two standard safeguards, both found necessary in the console harness:
    ///   - Anti-windup by conditional integration: the actuator moves at most 8 deg/s, so after a large speed error the
    ///     PI command runs far ahead of the real blades. The integral stops charging while that happens; without it the
    ///     integral filled to 90 deg, the pitch overshot to 75 and the rotor settled into a 19 s limit cycle at 20 m/s.
    ///   - Start-up pitch: after connecting, the blades pitch in only down to 4 deg below the angle the averaged wind
    ///     needs at rated speed (~15 deg at 20 m/s) until the rotor first reaches 98 % of rated speed. Pitching in to
    ///     0 deg at high wind overspun the rotor by 24 %, past a real overspeed trip; a floor at the full angle left
    ///     too little surplus to ever reach rated speed, so it never released.
    /// </summary>
    public sealed class PitchController
    {
        private const float NaturalFrequency = 0.6f;   // rad/s
        private const float DampingRatio = 0.7f;
        private const float MaxPitch = 90f;            // deg
        private const float StartUpReleaseSpeed = 0.98f; // fraction of rated speed that ends the start-up pitch floor
        private const float StartUpPitchMargin = 4f;     // deg below the rated-speed pitch, so the rotor can still get there

        private readonly TurbineSpecs specs;
        private float integral;                        // deg
        private bool startingUp = true;                // a turbine starts parked, so its first connection is a start-up

        public PitchController(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
            Angle = specs.IdlePitch;                   // a turbine starts parked
        }

        /// <summary>Collective blade pitch (deg). 0 = fine pitch (full power), 90 = feathered.</summary>
        public float Angle { get; private set; }

        /// <summary>Pitch the controller is steering toward, before the rate limit (deg).</summary>
        public float Command { get; private set; }

        /// <param name="averagedWindSpeed">The supervisory controller's averaged wind (m/s), for the start-up pitch.</param>
        public void Step(float deltaTime, float omega, TurbineOperatingState state, float averagedWindSpeed)
        {
            bool speedControl = state == TurbineOperatingState.Producing || state == TurbineOperatingState.RatedPower;
            if (!speedControl)
            {
                integral = 0f;
                startingUp = true;             // the next connection starts with the start-up pitch
                bool feather = state == TurbineOperatingState.StormShutdown || state == TurbineOperatingState.FaultStop;
                Command = feather ? specs.FeatherPitch : specs.IdlePitch;
                Angle = Mathf.MoveTowards(Angle, Command, specs.MaxPitchRate * deltaTime);
                return;
            }

            float ratedOmega = specs.RatedRotorRpm * PowerModel.RpmToRadPerSec;
            float sensitivity = PowerModel.RatedMechanicalPower(specs) / (specs.PitchSensitivityAngle * ratedOmega); // N·m/deg
            float kp = 2f * DampingRatio * NaturalFrequency * specs.RotorInertia / sensitivity;                    // deg per rad/s
            float ki = NaturalFrequency * NaturalFrequency * specs.RotorInertia / sensitivity;                    // deg per rad

            float speedError = omega - ratedOmega;
            float floor = 0f;
            if (startingUp)
            {
                // A few degrees below the need, so the rotor still has the surplus to reach rated speed and release it.
                floor = RatedSpeedPitch(averagedWindSpeed) - StartUpPitchMargin;
                if (omega >= ratedOmega * StartUpReleaseSpeed || floor <= 0f)
                {
                    // Bumpless transfer: the PI continues from where the blades are, instead of from an empty
                    // integral (which dropped the pitch by 5 deg and overspun the rotor by 11 %).
                    if (floor > 0f)
                        integral = Mathf.Clamp(Angle - kp * speedError, 0f, MaxPitch);
                    startingUp = false;
                    floor = 0f;
                }
            }

            // Conditional integration (anti-windup): if the blades cannot even reach the current command within this
            // step, and the error would push the integral further the same way, hold the integral instead of charging.
            float maxStep = specs.MaxPitchRate * deltaTime;
            float gap = Mathf.Clamp(Mathf.Max(kp * speedError + integral, floor), 0f, MaxPitch) - Angle;
            bool actuatorBehind = Mathf.Abs(gap) > maxStep && gap * speedError > 0f;
            if (!actuatorBehind)
                integral = Mathf.Clamp(integral + ki * speedError * deltaTime, 0f, MaxPitch);

            Command = Mathf.Clamp(Mathf.Max(kp * speedError + integral, floor), 0f, MaxPitch);
            Angle = Mathf.MoveTowards(Angle, Command, maxStep);
        }

        /// <summary>
        /// Pitch at which the rotor, at rated speed, takes exactly rated power from a wind speed (deg; 0 below rated
        /// wind). The Cp surface separates into λ and β parts, so this is closed form:
        /// Cp(λ_r, 0)·e^(-β/β₀)·½ρAv³ = P_rated  →  β = β₀·ln(Cp(λ_r, 0)·½ρAv³ / P_rated).
        /// </summary>
        public float RatedSpeedPitch(float windSpeed)
        {
            if (windSpeed <= 0.1f)
                return 0f;

            float ratedOmega = specs.RatedRotorRpm * PowerModel.RpmToRadPerSec;
            float lambda = ratedOmega * specs.RotorRadius / windSpeed;
            float finePitchPower = Aerodynamics.PowerCoefficient(specs, lambda, 0f)
                                   * 0.5f * specs.AirDensity * specs.SweptArea * windSpeed * windSpeed * windSpeed;
            float ratio = finePitchPower / PowerModel.RatedMechanicalPower(specs);
            return ratio > 1f ? specs.PitchSensitivityAngle * Mathf.Log(ratio) : 0f;
        }
    }
}
