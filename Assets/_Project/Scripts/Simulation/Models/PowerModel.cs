using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Computes electrical power from rotor speed (stateless).
    ///
    /// In the partial-load region the standard torque control law used by real turbines is applied:
    ///   T = k·ω²  →  P = k·ω³,   k = ½·ρ·π·R⁵·Cp_max / λ_opt³
    /// This way power follows the rotor rather than the wind: as the rotor slowly speeds up, power rises with it.
    ///
    /// Physical limits:
    /// - Power available in the wind (½·ρ·A·Cp·v³) cannot be exceeded — if the wind drops suddenly, power drops too.
    /// - Rated power cannot be exceeded (pitch control sheds the excess).
    /// </summary>
    public sealed class PowerModel
    {
        private const float RpmToRadPerSec = 2f * Mathf.PI / 60f;

        private readonly TurbineSpecs specs;

        public PowerModel(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
        }

        public float CalculateElectricalPowerMW(float rotorRpm, float windSpeed, bool generatorConnected)
        {
            if (!generatorConnected || rotorRpm <= 0f)
                return 0f;

            float omega = rotorRpm * RpmToRadPerSec;
            float tipSpeedRatioCubed = Mathf.Pow(specs.OptimalTipSpeedRatio, 3f);
            float torqueGain = 0.5f * specs.AirDensity * Mathf.PI * Mathf.Pow(specs.RotorRadius, 5f)
                               * specs.MaxPowerCoefficient / tipSpeedRatioCubed;

            float controllerPower = torqueGain * omega * omega * omega;
            float availableWindPower = 0.5f * specs.AirDensity * specs.SweptArea
                                       * specs.MaxPowerCoefficient * windSpeed * windSpeed * windSpeed;

            float mechanicalPower = Mathf.Min(controllerPower, availableWindPower);
            float electricalPower = Mathf.Min(mechanicalPower * specs.DrivetrainEfficiency, specs.RatedPowerWatts);

            return electricalPower / 1_000_000f;
        }
    }
}
