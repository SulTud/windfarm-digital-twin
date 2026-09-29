using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>One stored point of the trend history.</summary>
    internal readonly struct TelemetrySample
    {
        public readonly double Time;          // simulation time (s)
        public readonly float PowerMW;
        public readonly float WindSpeed;
        public readonly float Temperature;

        public TelemetrySample(double time, float powerMW, float windSpeed, float temperature)
        {
            Time = time;
            PowerMW = powerMW;
            WindSpeed = windSpeed;
            Temperature = temperature;
        }
    }

    /// <summary>
    /// Fixed-size ring buffer of telemetry samples in simulation time (oldest first). No allocation after construction:
    /// when full, the oldest sample is overwritten, like a pooled projectile array.
    /// Samples closer than <see cref="minInterval"/> in simulation time are skipped, which bounds the memory for the
    /// chart window at any simulation speed.
    /// </summary>
    internal sealed class TelemetryHistory
    {
        private readonly TelemetrySample[] buffer;
        private readonly double minInterval;
        private int start;

        public TelemetryHistory(int capacity, double minInterval)
        {
            buffer = new TelemetrySample[capacity];
            this.minInterval = minInterval;
        }

        public int Count { get; private set; }

        /// <summary>Sample by age order: 0 = oldest, Count - 1 = newest.</summary>
        public TelemetrySample this[int index] => buffer[(start + index) % buffer.Length];

        public void Add(in TurbineTelemetry telemetry) =>
            Add(new TelemetrySample(telemetry.SimulationTime, telemetry.PowerOutputMW, telemetry.WindSpeed,
                telemetry.GeneratorTemperature));

        public void Add(in TelemetrySample sample)
        {
            if (Count > 0)
            {
                double newestTime = this[Count - 1].Time;
                // Too close to the previous sample, or time went backwards (source restarted): skip / reset.
                if (sample.Time < newestTime)
                    Clear();
                else if (sample.Time - newestTime < minInterval)
                    return;
            }

            if (Count < buffer.Length)
            {
                buffer[(start + Count) % buffer.Length] = sample;
                Count++;
            }
            else
            {
                buffer[start] = sample;
                start = (start + 1) % buffer.Length;
            }
        }

        public void Clear()
        {
            start = 0;
            Count = 0;
        }

        /// <summary>Index of the first sample with Time >= time (Count if none). Binary search; samples are sorted.</summary>
        public int LowerBound(double time)
        {
            int low = 0;
            int high = Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (this[mid].Time < time)
                    low = mid + 1;
                else
                    high = mid;
            }

            return low;
        }
    }
}
