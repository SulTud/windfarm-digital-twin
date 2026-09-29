using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>
    /// Top bar: turbine identity, operating state pill with a plain-language explanation, and the simulation clock.
    ///
    /// On PC the explanation is always visible next to the pill. On phones it is a bubble under the pill: it appears
    /// for a few seconds whenever the state changes (so a viewer sees why the turbine stopped) and on pill tap.
    /// </summary>
    internal sealed class StatusBarPresenter
    {
        private const string ExplainClass = "dashboard--explain";
        private const float ExplanationSeconds = 6f;

        // Loading hitches (first frames, tab switches) must not eat the display time: count frames, not wall time.
        private const float MaxTickDelta = 0.1f;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] PillClasses = { "pill--idle", "pill--good", "pill--rated", "pill--warn" };

        private readonly VisualElement root;
        private readonly TurbineSpecs specs;
        private readonly Label turbineId;
        private readonly VisualElement pill;
        private readonly Label stateLabel;
        private readonly Label explanation;
        private readonly Label simClock;

        private bool hasState;
        private TurbineOperatingState state;
        private float explanationRemaining;
        private long shownClockSecond = -1;
        private float shownSpeed = -1f;

        public StatusBarPresenter(VisualElement root, TurbineSpecs specs, string turbineClassText)
        {
            this.root = root;
            this.specs = specs;

            turbineId = root.Require<Label>("turbine-id");
            pill = root.Require<VisualElement>("state-pill");
            stateLabel = root.Require<Label>("state-label");
            explanation = root.Require<Label>("state-explanation");
            simClock = root.Require<Label>("sim-clock");

            root.Require<Label>("turbine-class").text = turbineClassText;
            pill.RegisterCallback<ClickEvent>(HandlePillClicked);
        }

        public void Dispose()
        {
            pill.UnregisterCallback<ClickEvent>(HandlePillClicked);
            root.RemoveFromClassList(ExplainClass);
        }

        public void Show(in TurbineTelemetry telemetry, float simulationSpeed)
        {
            turbineId.text = telemetry.TurbineId;

            if (!hasState || telemetry.State != state)
                ApplyState(telemetry.State);

            ShowClock(telemetry.SimulationTime, simulationSpeed);
        }

        /// <summary>Called every frame with the unscaled frame time.</summary>
        public void Tick(float deltaTime)
        {
            if (explanationRemaining <= 0f)
                return;

            explanationRemaining -= Mathf.Min(deltaTime, MaxTickDelta);
            if (explanationRemaining <= 0f)
                SetExplanationVisible(false);
        }

        private void ApplyState(TurbineOperatingState newState)
        {
            hasState = true;
            state = newState;

            string pillClass;
            switch (newState)
            {
                case TurbineOperatingState.Idle:
                    pillClass = "pill--idle";
                    stateLabel.text = "IDLE";
                    explanation.text = Format("Wind below cut-in ({0:0} m/s). Rotor idling, no power.", specs.CutInWindSpeed);
                    break;

                case TurbineOperatingState.RatedPower:
                    pillClass = "pill--rated";
                    stateLabel.text = "RATED POWER";
                    explanation.text = Format("Wind above rated (~{0:0} m/s). Blades pitch to hold {1:0.0} MW.",
                        specs.RatedWindSpeed, specs.RatedPowerMW);
                    break;

                case TurbineOperatingState.StormShutdown:
                    // A protective stop, not a fault: amber, not red.
                    pillClass = "pill--warn";
                    stateLabel.text = "STORM SHUTDOWN";
                    explanation.text = Format("Wind above cut-out ({0:0} m/s). Blades feathered, brake holding. Restarts below {1:0} m/s.",
                        specs.CutOutWindSpeed, specs.RestartWindSpeed);
                    break;

                default:
                    pillClass = "pill--good";
                    stateLabel.text = "PRODUCING";
                    explanation.text = Format("Wind between cut-in ({0:0}) and rated (~{1:0} m/s). Rotor follows the wind.",
                        specs.CutInWindSpeed, specs.RatedWindSpeed);
                    break;
            }

            foreach (string candidate in PillClasses)
                pill.EnableInClassList(candidate, candidate == pillClass);

            SetExplanationVisible(true);
        }

        private void HandlePillClicked(ClickEvent evt) =>
            SetExplanationVisible(!root.ClassListContains(ExplainClass));

        private void SetExplanationVisible(bool visible)
        {
            root.EnableInClassList(ExplainClass, visible);
            explanationRemaining = visible ? ExplanationSeconds : 0f;
        }

        private void ShowClock(double simulationTime, float simulationSpeed)
        {
            long second = (long)simulationTime;
            if (second == shownClockSecond && Mathf.Approximately(simulationSpeed, shownSpeed))
                return;

            shownClockSecond = second;
            shownSpeed = simulationSpeed;

            // Hours are not wrapped at 24: at 20x the clock passes a day in 72 real minutes.
            long hours = second / 3600;
            long minutes = second / 60 % 60;
            long seconds = second % 60;
            simClock.text = Format("SIM {0:0.#}× · {1:00}:{2:00}:{3:00}", simulationSpeed, hours, minutes, seconds);
        }

        // Invariant culture: the UI is English, and a Turkish/German system locale would print "1,5" instead of "1.5".
        private static string Format(string format, params object[] args) => string.Format(Invariant, format, args);
    }
}
