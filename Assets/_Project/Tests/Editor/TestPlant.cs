using System;
using WindFarm.Simulation;

namespace WindFarm.Tests
{
    /// <summary>
    /// The simulator's physics chain without the MonoBehaviour, so tests can drive it with a chosen wind:
    /// protection -> supervisory control -> pitch -> rotor -> power -> generator temperature.
    /// Keep the order in sync with <c>TurbineDataSimulator.StepPhysics</c>.
    /// </summary>
    internal sealed class TestPlant
    {
        public const float TimeStep = 0.05f;   // s, the simulator's physics step
        public const float SampleInterval = 0.2f; // s, the simulator's publish interval

        public readonly TurbineSpecs Specs;
        public readonly TurbineController Controller;
        public readonly PitchController Pitch;
        public readonly RotorModel Rotor;
        public readonly ThermalModel Thermal;
        public readonly GeneratorProtection Protection;

        public TestPlant() : this(new TurbineSpecs()) { }

        public TestPlant(TurbineSpecs specs)
        {
            Specs = specs;
            Controller = new TurbineController(specs);
            Pitch = new PitchController(specs);
            Rotor = new RotorModel(specs);
            Thermal = new ThermalModel(specs);
            Protection = new GeneratorProtection(specs);
        }

        public float PowerMW { get; private set; }
        public double EnergyMWh { get; private set; }
        public double Time { get; private set; }
        public bool CoolingFanRunning { get; set; } = true;

        public TurbineOperatingState State => Controller.State;

        public void Step(float windSpeed)
        {
            Protection.Step(Thermal.Temperature, CoolingFanRunning);
            Controller.Step(TimeStep, windSpeed, PowerMW, Protection.Tripped);
            Pitch.Step(TimeStep, Rotor.Omega, Controller.State, Controller.AveragedWindSpeed);
            Rotor.Step(TimeStep, windSpeed, Pitch.Angle, Controller.IsGeneratorConnected, Protection.PowerLimitMW);
            PowerMW = PowerModel.ElectricalPowerMW(Specs, Rotor.Omega, Rotor.GeneratorTorque);
            Thermal.Step(TimeStep, Rotor.Rpm, PowerMW, CoolingFanRunning);

            EnergyMWh += PowerMW * TimeStep / 3600.0;
            Time += TimeStep;
        }

        /// <summary>Steps with a constant wind for the given simulated time.</summary>
        public void Run(float windSpeed, float seconds)
        {
            int steps = (int)Math.Round(seconds / TimeStep);
            for (int i = 0; i < steps; i++)
                Step(windSpeed);
        }

        /// <summary>A telemetry snapshot without sensor noise.</summary>
        public TurbineTelemetry Sample(float measuredWindSpeed) => new TurbineTelemetry(
            "TEST", Time, measuredWindSpeed, Rotor.Rpm, Pitch.Angle, Thermal.Temperature, PowerMW, EnergyMWh,
            Controller.State, Protection.PowerLimitMW, Protection.Alarms);
    }

    /// <summary>
    /// Deterministic turbulent wind for tests: Ornstein-Uhlenbeck turbulence plus occasional IEC-style (1 - cos)
    /// gusts, like <see cref="WindModel"/> without its Perlin drift (Mathf.PerlinNoise is native code).
    /// </summary>
    internal sealed class SyntheticWind
    {
        private const float IntegralTime = 8f;     // s
        private const float GustsPerSecond = 1f / 60f;

        private readonly float mean;
        private readonly float sigma;
        private readonly Random random;
        private float turbulence;
        private float gustTime = -1f;
        private float gustDuration;
        private float gustAmplitude;

        public SyntheticWind(float mean, float turbulenceIntensity, int seed)
        {
            this.mean = mean;
            sigma = turbulenceIntensity * mean;
            random = new Random(seed);
            Speed = mean;
        }

        public float Speed { get; private set; }

        public float Step(float deltaTime)
        {
            turbulence += -turbulence / IntegralTime * deltaTime +
                          sigma * (float)Math.Sqrt(2.0 * deltaTime / IntegralTime) * Gaussian();

            float gust = 0f;
            if (gustTime < 0f && random.NextDouble() < deltaTime * GustsPerSecond)
            {
                gustTime = 0f;
                gustDuration = 6f + (float)random.NextDouble() * 6f;
                gustAmplitude = mean * (0.15f + 0.2f * (float)random.NextDouble());
            }

            if (gustTime >= 0f)
            {
                gust = gustAmplitude * 0.5f * (1f - (float)Math.Cos(2.0 * Math.PI * gustTime / gustDuration));
                gustTime += deltaTime;
                if (gustTime > gustDuration)
                    gustTime = -1f;
            }

            Speed = Math.Max(0f, mean + turbulence + gust);
            return Speed;
        }

        private float Gaussian()
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
