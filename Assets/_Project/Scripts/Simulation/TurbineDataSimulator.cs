using System;
using UnityEngine;
using Random = System.Random;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Tek bir rüzgar türbininin mock sensör verisini üreten orkestratör.
    ///
    /// Nedensellik zinciri (her fizik adımında bu sırayla):
    ///   Rüzgar → Kontrolcü (durum) → Rotor RPM (atalet gecikmesi) → Güç (k·ω³) → Jeneratör sıcaklığı (termal gecikme)
    ///
    /// Fizik sabit adımla (fixed timestep) ilerler, böylece sonuçlar kare hızından (FPS) bağımsızdır.
    /// Telemetri ise ayrı ve daha düşük bir örnekleme frekansıyla yayınlanır — gerçek bir SCADA gibi.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurbineDataSimulator : MonoBehaviour, ITurbineTelemetrySource
    {
        // Sekme arka plandayken (WebGL) biriken dev deltaTime'ın tek karede yüzlerce adım koşturmasını engeller.
        private const float MaxFrameDeltaTime = 0.25f;

        [SerializeField] private string turbineId = "WTG-01";
        [SerializeField] private TurbineSpecs specs = new TurbineSpecs();
        [SerializeField] private SiteWindConditions windConditions = new SiteWindConditions();
        [SerializeField] private SensorNoiseProfile sensorNoise = new SensorNoiseProfile();

        [Header("Simulation")]
        [SerializeField, Min(0.005f), Tooltip("Fizik entegrasyon adımı (s).")]
        private float physicsTimeStep = 0.05f;

        [SerializeField, Min(0.02f), Tooltip("Telemetri yayın periyodu (s, gerçek zaman).")]
        private float publishInterval = 0.2f;

        [SerializeField, Range(0.1f, 20f), Tooltip("Simülasyon zamanı çarpanı. Demo sırasında süreçleri hızlandırmak için.")]
        private float simulationSpeed = 1f;

        [SerializeField, Min(0f), Tooltip("Başlangıçta sessizce koşturulacak süre (s); veriler 'soğuk' değil dengede başlar.")]
        private float prewarmSeconds = 60f;

        [SerializeField, Tooltip("0 = her çalıştırmada farklı. Sabit değer = tekrarlanabilir veri (test / sunum için).")]
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

        public float SimulationSpeed
        {
            get => simulationSpeed;
            set => simulationSpeed = Mathf.Clamp(value, 0.1f, 20f);
        }

        /// <summary>
        /// Sahanın uzun dönem ortalama rüzgarını çalışma anında değiştirir (ör. UI slider, "fırtına senaryosu").
        /// Değişim anlık değildir: rüzgar, rotor ve sıcaklık kendi dinamikleriyle yeni duruma yakınsar.
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
            // Awake'te abone olamayan UI'lar için ilk okumayı yayınla.
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

            // Her alt sistemin kendi rastgele akışı var: birine parametre eklemek diğerlerinin dizisini kaydırmaz.
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

        /// <summary>Fizik durumunu okuyup üzerine ölçüm gürültüsü ekler. Fizik durumunu değiştirmez.</summary>
        private TurbineTelemetry SampleSensors()
        {
            float measuredWind = Mathf.Max(0f, wind.CurrentSpeed + sensorRandom.NextGaussian(sensorNoise.WindSpeedStdDev));

            // Duran rotorda enkoder / güç ölçer sıfır okur; gürültüyü yalnızca hareket varken ekle.
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
