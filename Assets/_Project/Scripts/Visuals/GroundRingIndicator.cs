using UnityEngine;
using WindFarm.Simulation;

namespace WindFarm.Visuals
{
    /// <summary>
    /// Makes the ground ring (WindFarm/Ground Ring shader) a status light in the 3D scene, the counterpart of the
    /// dashboard's state pill: cyan while producing, amber while derated, warned or stopped by a storm, red in a fault
    /// stop, dim grey while idle. The radar sweep runs only while the turbine produces.
    ///
    /// Reads only telemetry (consumer pattern), so it works for a real source too. Color and sweep glide to their
    /// targets every frame (exponential smoothing), because the telemetry arrives at 5 Hz.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public sealed class GroundRingIndicator : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SweepStrengthId = Shader.PropertyToID("_SweepStrength");

        // Unity cannot serialize interfaces; take the concrete component and use it through the interface.
        [SerializeField] private TurbineDataSimulator simulator;

        [SerializeField, Tooltip("Producing / rated power. Matches the dashboard accent.")]
        private Color producingColor = new Color(0.36f, 0.78f, 0.88f);

        [SerializeField, Tooltip("Derated, generator temperature warning, storm shutdown. Matches the dashboard amber.")]
        private Color cautionColor = new Color(0.94f, 0.69f, 0.24f);

        [SerializeField, Tooltip("Fault stop (protection trip). Matches the dashboard red.")]
        private Color faultColor = new Color(0.94f, 0.35f, 0.29f);

        [SerializeField, Tooltip("Idle below cut-in.")]
        private Color idleColor = new Color(0.30f, 0.36f, 0.40f);

        [SerializeField, Min(0.01f), Tooltip("Color and sweep transition time (s).")]
        private float transitionTime = 0.6f;

        private ITurbineTelemetrySource source;
        private Material material;
        private float baseSweepStrength;
        private Color targetColor;
        private float targetSweep;
        private Color currentColor;
        private float currentSweep;
        private bool hasTelemetry;

        private void Awake()
        {
            // Instance the material once, so the asset keeps its tuned values and the base sweep strength.
            material = GetComponent<Renderer>().material;
            baseSweepStrength = material.GetFloat(SweepStrengthId);
            currentColor = targetColor = producingColor;
            currentSweep = targetSweep = baseSweepStrength;
        }

        private void OnEnable()
        {
            if (simulator == null)
            {
                Debug.LogWarning($"{nameof(GroundRingIndicator)}: simulator is not assigned.", this);
                return;
            }

            source = simulator;
            source.TelemetryUpdated += HandleTelemetry;
            if (source.LatestTelemetry.TurbineId != null)
                HandleTelemetry(source.LatestTelemetry);
        }

        private void OnDisable()
        {
            if (source != null)
            {
                source.TelemetryUpdated -= HandleTelemetry;
                source = null;
            }
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }

        private void HandleTelemetry(TurbineTelemetry telemetry)
        {
            const TurbineAlarms caution = TurbineAlarms.GeneratorTemperatureWarning | TurbineAlarms.GeneratorTemperatureAlarm;
            bool derated = telemetry.PowerLimitMW < simulator.Specs.RatedPowerMW * 0.999f;

            switch (telemetry.State)
            {
                case TurbineOperatingState.FaultStop:
                    targetColor = faultColor;
                    break;
                case TurbineOperatingState.StormShutdown:
                    targetColor = cautionColor;
                    break;
                case TurbineOperatingState.Idle:
                    targetColor = idleColor;
                    break;
                default:
                    targetColor = derated || (telemetry.Alarms & caution) != 0 ? cautionColor : producingColor;
                    break;
            }

            bool producing = telemetry.State == TurbineOperatingState.Producing ||
                             telemetry.State == TurbineOperatingState.RatedPower;
            targetSweep = producing ? baseSweepStrength : 0f;

            if (!hasTelemetry)
            {
                // First sample: snap instead of fading in from the default (like a particle system Prewarm).
                hasTelemetry = true;
                currentColor = targetColor;
                currentSweep = targetSweep;
            }
        }

        private void Update()
        {
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / transitionTime);
            currentColor = Color.Lerp(currentColor, targetColor, blend);
            currentSweep = Mathf.Lerp(currentSweep, targetSweep, blend);
            material.SetColor(ColorId, currentColor);
            material.SetFloat(SweepStrengthId, currentSweep);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            simulator = FindAnyObjectByType<TurbineDataSimulator>();
        }
#endif
    }
}
