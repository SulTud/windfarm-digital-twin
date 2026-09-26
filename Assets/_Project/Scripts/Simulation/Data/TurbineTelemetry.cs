namespace WindFarm.Simulation
{
    /// <summary>
    /// Tek bir örnekleme anındaki sensör okumalarının değişmez (immutable) anlık görüntüsü.
    /// Struct olduğu için event ile yayınlanırken heap tahsisi (GC) oluşturmaz.
    /// </summary>
    public readonly struct TurbineTelemetry
    {
        public readonly string TurbineId;

        /// <summary>Simülasyon başlangıcından beri geçen süre (s).</summary>
        public readonly double SimulationTime;

        /// <summary>Nasel anemometresi okuması (m/s).</summary>
        public readonly float WindSpeed;

        /// <summary>Ana şaft (rotor) devri (RPM).</summary>
        public readonly float RotorRpm;

        /// <summary>Jeneratör sargı sıcaklığı (°C).</summary>
        public readonly float GeneratorTemperature;

        /// <summary>Şebekeye verilen anlık elektrik gücü (MW).</summary>
        public readonly float PowerOutputMW;

        /// <summary>Simülasyon boyunca üretilen toplam enerji (MWh).</summary>
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
