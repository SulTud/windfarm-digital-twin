using System;
using UnityEngine;
using Random = System.Random;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Orchestrator that produces mock sensor data for a single wind turbine.
    ///
    /// Causal chain (in this order on every physics step):
    ///   Wind → Controller (state) → Rotor RPM (inertia lag) → Power (k·ω³) → Generator temperature (thermal lag)
    ///
    /// Physics advances with a fixed timestep, so results are independent of the frame rate (FPS).
    /// Telemetry is published at a separate, lower sampling rate — just like a real SCADA system.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurbineDataSimulator : MonoBehaviour, ITurbineTelemetrySource
    {
        // Prevents a huge accumulated deltaTime (e.g. WebGL tab in background) from running hundreds of steps in one frame.
        private const float MaxFrameDeltaTime = 0.25f;

        [SerializeField] private string turbineId = "WTG-01";
        [SerializeField] private TurbineSpecs specs = new TurbineSpecs();
        [SerializeField] private SiteWindConditions windConditions = new SiteWindConditions();
        [SerializeField] private SensorNoiseProfile sensorNoise = new SensorNoiseProfile();

        [Header("Simulation")]
        [SerializeField, Min(0.005f), Tooltip("Physics integration step (s).")]
        private float physicsTimeStep = 0.05f;

        [SerializeField, Min(0.02f), Tooltip("Telemetry publish period (s, real time).")]
        private float publishInterval = 0.2f;

        [SerializeField, Range(0.1f, 20f), Tooltip("Simulation time multiplier. Speeds up the processes during a demo.")]
        private float simulationSpeed = 1f;

        [SerializeField, Min(0f), Tooltip("Time silently simulated at startup (s) so data starts at equilibrium instead of 'cold'.")]
        private float prewarmSeconds = 60f;

        [SerializeField, Tooltip("0 = different on every run. A fixed value gives reproducible data (for tests / presentations).")]
        private int randomSeed;

        private WindModel wind;
        private TurbineController controller;
        private RotorModel rotor;
        private PowerModel power;
        private ThermalModel thermal;
        private Random sensorRandom;

        private double simulationTime;
        private double totalEnergyMWh;
        private float currentPowerMW;
        private float physicsAccumulator;
        private float publishAccumulator;

        public event Action<TurbineTelemetry> TelemetryUpdated;
        public event Action<TurbineOperatingState, TurbineOperatingState> OperatingStateChanged;

        public string TurbineId => turbineId;
        public TurbineTelemetry LatestTelemetry { get; private set; }
        public TurbineOperatingState CurrentState => controller.State;
        public TurbineSpecs Specs => specs;

        /// <summary>Current site mean wind (m/s); see <see cref="SetMeanWindSpeed"/>.</summary>
        public float MeanWindSpeed => windConditions.MeanWindSpeed;

        public float SimulationSpeed
        {
            get => simulationSpeed;
            set => simulationSpeed = Mathf.Clamp(value, 0.1f, 20f);
        }

        /// <summary>
        /// Changes the site's long-term mean wind at runtime (e.g. UI slider, "storm scenario").
        /// The change is not instant: wind, rotor and temperature converge to the new state with their own dynamics.
        /// </summary>
        public void SetMeanWindSpeed(float metersPerSecond) =>
            windConditions.MeanWindSpeed = Mathf.Max(0f, metersPerSecond);

        private void Awake()
        {
            BuildModels();
            Prewarm();
            LatestTelemetry = SampleSensors();
        }

        private void Start()
        {
            // Publish the first reading for UIs that could not subscribe before Awake.
            TelemetryUpdated?.Invoke(LatestTelemetry);
        }

        private void Update()
        {
            float frameDeltaTime = Mathf.Min(Time.deltaTime, MaxFrameDeltaTime);

            physicsAccumulator += frameDeltaTime * simulationSpeed;
            while (physicsAccumulator >= physicsTimeStep)
            {
                StepPhysics(physicsTimeStep);
                physicsAccumulator -= physicsTimeStep;
            }

            publishAccumulator += frameDeltaTime;
            if (publishAccumulator >= publishInterval)
            {
                publishAccumulator %= publishInterval;
                Publish();
            }
        }

        private void BuildModels()
        {
            int seed = randomSeed != 0 ? randomSeed : Environment.TickCount ^ GetInstanceID();
            var masterRandom = new Random(seed);

            // Each subsystem has its own random stream: adding a parameter to one does not shift the sequence of the others.
            wind = new WindModel(windConditions, new Random(masterRandom.Next()));
            sensorRandom = new Random(masterRandom.Next());
            controller = new TurbineController(specs);
            rotor = new RotorModel(specs);
            power = new PowerModel(specs);
            thermal = new ThermalModel(specs);
        }

        private void Prewarm()
        {
            int steps = Mathf.CeilToInt(prewarmSeconds / physicsTimeStep);
            for (int i = 0; i < steps; i++)
                StepPhysics(physicsTimeStep);
        }

        private void StepPhysics(float deltaTime)
        {
            wind.Step(deltaTime);
            float windSpeed = wind.CurrentSpeed;

            TurbineOperatingState previousState = controller.State;
            if (controller.Step(deltaTime, windSpeed, currentPowerMW))
                OperatingStateChanged?.Invoke(previousState, controller.State);

            bool connected = controller.IsGeneratorConnected;
            rotor.Step(deltaTime, windSpeed, connected);
            currentPowerMW = power.CalculateElectricalPowerMW(rotor.Rpm, windSpeed, connected);
            thermal.Step(deltaTime, rotor.Rpm, currentPowerMW);

            totalEnergyMWh += currentPowerMW * deltaTime / 3600.0;
            simulationTime += deltaTime;
        }

        private void Publish()
        {
            LatestTelemetry = SampleSensors();
            TelemetryUpdated?.Invoke(LatestTelemetry);
        }

        /// <summary>Reads the physics state and adds measurement noise on top. Does not modify the physics state.</summary>
        private TurbineTelemetry SampleSensors()
        {
            float measuredWind = Mathf.Max(0f, wind.CurrentSpeed + sensorRandom.NextGaussian(sensorNoise.WindSpeedStdDev));

            // A stationary rotor reads zero on the encoder / power meter; only add noise while there is motion.
            float measuredRpm = rotor.Rpm > 0.05f
                ? Mathf.Max(0f, rotor.Rpm + sensorRandom.NextGaussian(sensorNoise.RotorRpmStdDev))
                : 0f;

            float measuredPower = currentPowerMW > 0f
                ? Mathf.Max(0f, currentPowerMW + sensorRandom.NextGaussian(sensorNoise.PowerStdDevFraction * specs.RatedPowerMW))
                : 0f;

            float measuredTemperature = thermal.Temperature + sensorRandom.NextGaussian(sensorNoise.TemperatureStdDev);

            return new TurbineTelemetry(
                turbineId,
                simulationTime,
                measuredWind,
                measuredRpm,
                measuredTemperature,
                measuredPower,
                totalEnergyMWh,
                controller.State);
        }
    }
}
