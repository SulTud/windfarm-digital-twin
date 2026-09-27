namespace WindFarm.Simulation
{
    /// <summary>
    /// Immutable snapshot of all sensor readings at a single sampling instant.
    /// Being a struct, publishing it through an event causes no heap allocation (no GC pressure).
    /// </summary>
    public readonly struct TurbineTelemetry
    {
        public readonly string TurbineId;

        /// <summary>Time elapsed since the simulation started (s).</summary>
        public readonly double SimulationTime;

        /// <summary>Nacelle anemometer reading (m/s).</summary>
        public readonly float WindSpeed;

        /// <summary>Main shaft (rotor) speed (RPM).</summary>
        public readonly float RotorRpm;

        /// <summary>Generator winding temperature (°C).</summary>
        public readonly float GeneratorTemperature;

        /// <summary>Instantaneous electrical power delivered to the grid (MW).</summary>
        public readonly float PowerOutputMW;

        /// <summary>Total energy produced over the simulation (MWh).</summary>
        public readonly double TotalEnergyMWh;

        public readonly TurbineOperatingState State;

        public TurbineTelemetry(
            string turbineId,
            double simulationTime,
            float windSpeed,
            float rotorRpm,
            float generatorTemperature,
            float powerOutputMW,
            double totalEnergyMWh,
            TurbineOperatingState state)
        {
            TurbineId = turbineId;
            SimulationTime = simulationTime;
            WindSpeed = windSpeed;
            RotorRpm = rotorRpm;
            GeneratorTemperature = generatorTemperature;
            PowerOutputMW = powerOutputMW;
            TotalEnergyMWh = totalEnergyMWh;
            State = state;
        }

        public override string ToString() =>
            $"[{TurbineId} t={SimulationTime:F1}s] {State} | Wind {WindSpeed:F2} m/s | " +
            $"Rotor {RotorRpm:F2} rpm | Gen {GeneratorTemperature:F1} °C | " +
            $"Power {PowerOutputMW:F3} MW | Energy {TotalEnergyMWh:F4} MWh";
    }
}
