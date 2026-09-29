using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// The datasheet power curve, computed from the same physics the simulator runs: for each wind speed, the rotor
    /// speed at which aerodynamic and generator torque balance (β = 0), and the electrical power there. Above that,
    /// the pitch controller holds rated speed and power. OEMs compute their published curves the same way (from the
    /// aerodynamic model and the control law), so measured points scatter around it instead of sitting on one side.
    ///
    /// Stateless. A bisection per wind speed costs ~40 torque evaluations; the chart asks for ~90 points only when it
    /// repaints its static layer.
    /// </summary>
    public static class SteadyStatePowerCurve
    {
        private const int Iterations = 40;

        /// <summary>Steady-state electrical power (MW); zero outside cut-in..cut-out.</summary>
        public static float PowerMW(TurbineSpecs specs, float windSpeed)
        {
            if (windSpeed < specs.CutInWindSpeed || windSpeed > specs.CutOutWindSpeed)
                return 0f;

            float minOmega = specs.MinRotorRpm * PowerModel.RpmToRadPerSec;
            float ratedOmega = specs.RatedRotorRpm * PowerModel.RpmToRadPerSec;

            // Enough wind to reach rated speed at fine pitch: the pitch controller holds it there (regions 2.5 / 3).
            if (NetTorque(specs, ratedOmega, windSpeed) >= 0f)
                return PowerModel.ElectricalPowerMW(specs, ratedOmega, PowerModel.GeneratorTorque(specs, ratedOmega));

            // Otherwise the balance lies between minimum and rated speed. At minimum speed the generator torque is
            // zero, so the net torque is positive there and negative at rated speed: bisect for the crossing.
            float low = minOmega;
            float high = ratedOmega;
            for (int i = 0; i < Iterations; i++)
            {
                float mid = 0.5f * (low + high);
                if (NetTorque(specs, mid, windSpeed) > 0f)
                    low = mid;
                else
                    high = mid;
            }

            float omega = 0.5f * (low + high);
            return PowerModel.ElectricalPowerMW(specs, omega, PowerModel.GeneratorTorque(specs, omega));
        }

        /// <summary>Lowest wind speed at which the steady-state curve reaches rated power (m/s).</summary>
        public static float RatedWindSpeed(TurbineSpecs specs)
        {
            float target = specs.RatedPowerMW * 0.9999f;
            float low = specs.CutInWindSpeed;
            float high = specs.CutOutWindSpeed;
            if (PowerMW(specs, high) < target)
                return high;

            for (int i = 0; i < Iterations; i++)
            {
                float mid = 0.5f * (low + high);
                if (PowerMW(specs, mid) >= target)
                    high = mid;
                else
                    low = mid;
            }

            return high;
        }

        private static float NetTorque(TurbineSpecs specs, float omega, float windSpeed) =>
            Aerodynamics.Torque(specs, omega, windSpeed, 0f) - PowerModel.GeneratorTorque(specs, omega);
    }
}
