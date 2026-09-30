using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Cameras;
using WindFarm.Simulation;
using WindFarm.Visuals;

namespace WindFarm.UI
{
    /// <summary>
    /// Binds the dashboard to a telemetry source. Owns one presenter per screen region (plain C# classes that know
    /// the UXML element names) and forwards telemetry events and per-frame ticks to them.
    ///
    /// Follows the project consumer pattern: concrete simulator serialized, used through ITurbineTelemetrySource,
    /// subscribed in OnEnable and unsubscribed in OnDisable.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class DashboardController : MonoBehaviour
    {
        // Unity cannot serialize interfaces; take the concrete component and use it through the interface.
        [SerializeField] private TurbineDataSimulator simulator;

        [SerializeField, Tooltip("3D turbine driven by the X-Ray buttons. Optional: without it the buttons are disabled.")]
        private TurbineVisualController turbineVisuals;

        [SerializeField, Tooltip("Orbit camera framed into the free scene area and driven by scene gestures. Optional.")]
        private TurbineOrbitCamera orbitCamera;

        [Header("Identity")]
        [SerializeField, Tooltip("Turbine model class shown next to the turbine id.")]
        private string turbineClass = "V112-class";

        [SerializeField, Min(0f), Tooltip("Hub height shown in the top bar (m).")]
        private float hubHeight = 94f;

        // Feeds the trend chart (the power curve keeps its own trail of the smoothed live point). Min 0.5 s of
        // simulation time apart, so a 5 min window holds ~600 samples at any simulation speed.
        private readonly TelemetryHistory history = new TelemetryHistory(1024, 0.5);

        private ITurbineTelemetrySource source;
        private StatusBarPresenter statusBar;
        private ValueCardsPresenter valueCards;
        private AlertsPresenter alerts;
        private TrendChartPresenter trendChart;
        private PowerCurvePresenter powerCurve;
        private ControlsPresenter controls;
        private SceneViewPresenter sceneView;

        private void OnEnable()
        {
            // Unity's null check (destroyed / unassigned object) must be done on the concrete type, not through the interface.
            if (simulator == null)
            {
                Debug.LogWarning($"{nameof(DashboardController)}: simulator is not assigned.", this);
                return;
            }

            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning($"{nameof(DashboardController)}: UIDocument has no root element (missing Source Asset?).", this);
                return;
            }

            TurbineSpecs specs = simulator.Specs;
            string classText = string.Format(CultureInfo.InvariantCulture, "{0} · {1:0.0} MW · hub {2:0} m",
                turbineClass, specs.RatedPowerMW, hubHeight);
            statusBar = new StatusBarPresenter(root, specs, classText);
            valueCards = new ValueCardsPresenter(root, specs);
            alerts = new AlertsPresenter(root, specs);
            trendChart = new TrendChartPresenter(root, specs, history);
            powerCurve = new PowerCurvePresenter(root, specs);
            controls = new ControlsPresenter(root, simulator, turbineVisuals, orbitCamera);
            if (orbitCamera != null)
                sceneView = new SceneViewPresenter(root, orbitCamera);

            source = simulator;
            source.TelemetryUpdated += HandleTelemetry;

            // The simulator may not have run Awake yet (a default snapshot has no id); then the first event initializes us.
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

            statusBar?.Dispose();
            statusBar = null;
            valueCards = null;
            alerts?.Dispose();
            alerts = null;
            trendChart?.Dispose();
            trendChart = null;
            powerCurve = null;
            controls?.Dispose();
            controls = null;
            sceneView?.Dispose();
            sceneView = null;
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            statusBar?.Tick(deltaTime);
            valueCards?.Tick(deltaTime);
            alerts?.Tick(deltaTime);
            trendChart?.Tick(deltaTime);
            powerCurve?.Tick(deltaTime);
            controls?.Tick();
            sceneView?.Tick();
        }

        private void HandleTelemetry(TurbineTelemetry telemetry)
        {
            history.Add(telemetry);
            statusBar.Show(telemetry, simulator.SimulationSpeed);
            valueCards.Show(telemetry);
            alerts.Show(telemetry);
            trendChart.Show(telemetry);
            powerCurve.Show(telemetry);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            simulator = FindAnyObjectByType<TurbineDataSimulator>();
            turbineVisuals = FindAnyObjectByType<TurbineVisualController>();
            orbitCamera = FindAnyObjectByType<TurbineOrbitCamera>();
        }
#endif
    }
}
