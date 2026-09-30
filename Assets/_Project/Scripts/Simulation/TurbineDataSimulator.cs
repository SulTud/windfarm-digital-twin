using System;
using UnityEngine;
using Random = System.Random;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Orchestrator that produces mock sensor data for a single wind turbine.
    ///
    /// Causal chain (in this order on every physics step):
    ///   Wind → Protection (alarms, power limit, trip) → Controller (state) → Pitch (PI + rate limit)
    ///   → Rotor (torque balance J·dω/dt = T_aero - T_gen) → Power (generator torque · ω · η)
    ///   → Generator temperature (heat balance, cooling fan)
    /// Protection acts on the temperature of the previous step, like a PLC reading its inputs at the start of a scan.
    ///
    /// Fault injection (<see cref="InjectFault"/>) changes the physical plant only (e.g. the cooling fan stops). What
    /// follows (heating, alarms, derating, trip) comes from the models and the controller, and consumers see it through
    /// the telemetry exactly as they would see it from a real turbine.
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
        private PitchController pitch;
        private RotorModel rotor;
        private ThermalModel thermal;
        private GeneratorProtection protection;
        private Random sensorRandom;
        private TurbineFaults activeFaults;

        private double simulationTime;
        private double totalEnergyMWh;
        private float currentPowerMW;
        private float physicsAccumulator;
        private float publishAccumulator;
        private bool prewarming;

        // Startup values restored by ResetSimulation (the demo controls change them at runtime).
        private float initialMeanWindSpeed;
        private float initialSimulationSpeed;

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

        /// <summary>Physical faults currently injected (mock only; consumers see their effects as alarms in the telemetry).</summary>
        public TurbineFaults ActiveFaults => activeFaults;

        /// <summary>Injects a fault into the physical plant from the next physics step on (demo "what-if" scenario).</summary>
        public void InjectFault(TurbineFaults faults) => activeFaults |= faults;

        /// <summary>Repairs a fault (the technician's visit). The controller then resets its alarms on its own conditions.</summary>
        public void ClearFault(TurbineFaults faults) => activeFaults &= ~faults;

        /// <summary>
        /// Starts over as on scene load: startup mean wind and simulation speed, no faults, fresh models (new random
        /// streams unless a seed is set), clock and energy counter at zero, then the prewarm. Publishes at once.
        /// Consumers see the simulation time jump back, which is also how a restarted real source would look.
        /// </summary>
        public void ResetSimulation()
        {
            windConditions.MeanWindSpeed = initialMeanWindSpeed;
            simulationSpeed = initialSimulationSpeed;
            activeFaults = TurbineFaults.None;

            TurbineOperatingState previousState = controller.State;
            Initialize();
            if (controller.State != previousState)
                OperatingStateChanged?.Invoke(previousState, controller.State);

            publishAccumulator = 0f;
            TelemetryUpdated?.Invoke(LatestTelemetry);
        }

        private void Awake()
        {
            initialMeanWindSpeed = windConditions.MeanWindSpeed;
            initialSimulationSpeed = simulationSpeed;
            Initialize();
        }

        private void Initialize()
        {
            simulationTime = 0.0;
            totalEnergyMWh = 0.0;
            currentPowerMW = 0f;
            physicsAccumulator = 0f;

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
            pitch = new PitchController(specs);
            rotor = new RotorModel(specs);
            thermal = new ThermalModel(specs);
            protection = new GeneratorProtection(specs);
        }

        private void Prewarm()
        {
            // State changes during the prewarm are history nobody saw: consumers only get the resulting state.
            prewarming = true;
            int steps = Mathf.CeilToInt(prewarmSeconds / physicsTimeStep);
            for (int i = 0; i < steps; i++)
                StepPhysics(physicsTimeStep);
            prewarming = false;
        }

        private void StepPhysics(float deltaTime)
        {
            wind.Step(deltaTime);
            float windSpeed = wind.CurrentSpeed;

            bool coolingFanRunning = (activeFaults & TurbineFaults.CoolingFanFailure) == 0;
            protection.Step(thermal.Temperature, coolingFanRunning);

            TurbineOperatingState previousState = controller.State;
            if (controller.Step(deltaTime, windSpeed, currentPowerMW, protection.Tripped) && !prewarming)
                OperatingStateChanged?.Invoke(previousState, controller.State);

            bool connected = controller.IsGeneratorConnected;
            pitch.Step(deltaTime, rotor.Omega, controller.State, controller.AveragedWindSpeed);
            rotor.Step(deltaTime, windSpeed, pitch.Angle, connected, protection.PowerLimitMW);
            currentPowerMW = PowerModel.ElectricalPowerMW(specs, rotor.Omega, rotor.GeneratorTorque);
            thermal.Step(deltaTime, rotor.Rpm, currentPowerMW, coolingFanRunning);

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

            float resolution = sensorNoise.PitchResolution;
            float measuredPitch = resolution > 0f ? Mathf.Round(pitch.Angle / resolution) * resolution : pitch.Angle;
            float measuredTemperature = thermal.Temperature + sensorRandom.NextGaussian(sensorNoise.TemperatureStdDev);

            return new TurbineTelemetry(
                turbineId,
                simulationTime,
                measuredWind,
                measuredRpm,
                measuredPitch,
                measuredTemperature,
                measuredPower,
                totalEnergyMWh,
                controller.State,
                protection.PowerLimitMW,
                protection.Alarms);
        }
    }
}
