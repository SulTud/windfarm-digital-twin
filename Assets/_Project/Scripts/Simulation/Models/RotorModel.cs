using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Rotor speed from a one-mass drivetrain torque balance, the core of every wind turbine simulator (OpenFAST's
    /// rigid drivetrain, the simplified models of IEC 61400-27-1):
    ///
    ///   J · dω/dt = T_aero(ω, v, β) - T_generator(ω)
    ///
    /// Like Rigidbody.AddTorque instead of SmoothDamp: the rotor is not pulled toward a target speed, it accelerates
    /// or slows down from the difference of two torques. That makes the energy exchange real: in a lull the generator
    /// keeps loading the rotor and draws on its kinetic energy, in a gust the rotor stores energy before the power
    /// rises. Both put measured points on either side of the steady-state power curve, as in real SCADA data.
    ///
    /// Start-up and shutdown are supervisory sequences, as on a real turbine, modelled as first-order lags:
    ///   - connected but below minimum speed: spin-up with the blades pitching in, generator not yet synchronised;
    ///   - disconnected (Idle, StormShutdown, FaultStop): run-down with the blades feathered (aerodynamic brake), then parked.
    /// </summary>
    public sealed class RotorModel
    {
        private const float BrakingTimeConstantFactor = 0.6f; // feathering + brake stops faster than a free spin-up
        private const float SpinUpOvershoot = 1.05f;          // spin-up target just above the minimum speed, so it is crossed

        private readonly TurbineSpecs specs;

        public RotorModel(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
        }

        /// <summary>Rotor speed (rad/s).</summary>
        public float Omega { get; private set; }

        public float Rpm => Omega / PowerModel.RpmToRadPerSec;

        /// <summary>Generator torque applied in the last step (N·m on the rotor shaft); 0 while not synchronised.</summary>
        public float GeneratorTorque { get; private set; }

        /// <param name="powerLimitMW">Active power limit from the controller (MW); rated power when not derating.</param>
        public void Step(float deltaTime, float windSpeed, float pitchDegrees, bool generatorConnected, float powerLimitMW)
        {
            if (!generatorConnected)
            {
                GeneratorTorque = 0f;
                Lag(0f, specs.RotorTimeConstant * BrakingTimeConstantFactor, deltaTime);
                return;
            }

            float minOmega = specs.MinRotorRpm * PowerModel.RpmToRadPerSec;
            if (Omega < minOmega)
            {
                // Start-up sequence: bring the rotor to the speed at which the generator synchronises.
                GeneratorTorque = 0f;
                float target = Mathf.Max(OptimalOmega(windSpeed), minOmega * SpinUpOvershoot);
                Lag(target, specs.RotorTimeConstant, deltaTime);
                return;
            }

            float aerodynamicTorque = Aerodynamics.Torque(specs, Omega, windSpeed, pitchDegrees);
            GeneratorTorque = PowerModel.GeneratorTorque(specs, Omega, powerLimitMW);
            Omega = Mathf.Max(0f, Omega + (aerodynamicTorque - GeneratorTorque) / specs.RotorInertia * deltaTime);
        }

        private float OptimalOmega(float windSpeed) =>
            Mathf.Min(specs.OptimalTipSpeedRatio * windSpeed / specs.RotorRadius, specs.RatedRotorRpm * PowerModel.RpmToRadPerSec);

        private void Lag(float target, float timeConstant, float deltaTime)
        {
            float alpha = 1f - Mathf.Exp(-deltaTime / timeConstant);
            Omega += (target - Omega) * alpha;
        }
    }
}
