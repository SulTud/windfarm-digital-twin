using System;
using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Cameras;
using WindFarm.Simulation;
using WindFarm.Visuals;

namespace WindFarm.UI
{
    /// <summary>
    /// Demo controls: site mean wind slider, simulation speed buttons, the X-Ray toggles (dock button on PC,
    /// floating button on phones) and the reset button (restarts the simulation, turns X-Ray off, camera home).
    ///
    /// These drive the mock simulator directly (SetMeanWindSpeed / SimulationSpeed / ResetSimulation), so they take the concrete
    /// TurbineDataSimulator. With a real SCADA source there is nothing to control and this presenter would not be
    /// created. Every frame the controls re-sync from the simulator, so Inspector changes in Play mode show up too.
    /// </summary>
    internal sealed class ControlsPresenter
    {
        private const float WindStep = 0.5f;             // m/s; the slider snaps to half metres per second
        // Roughly x2 per step; at 10x the generator reaches its rated temperature in about half a minute.
        private static readonly float[] SpeedOptions = { 1f, 2f, 5f, 10f };
        private static readonly string[] SpeedButtonNames = { "sim-speed-1", "sim-speed-2", "sim-speed-5", "sim-speed-10" };

        private readonly TurbineDataSimulator simulator;
        private readonly TurbineVisualController visuals;
        private readonly TurbineOrbitCamera orbitCamera;

        private readonly Slider windSlider;
        private readonly Label windValue;
        private readonly Button[] speedButtons = new Button[SpeedOptions.Length];
        private readonly Action[] speedHandlers = new Action[SpeedOptions.Length];
        private readonly Button xRayButton;
        private readonly Button xRayFab;
        private readonly Button resetButton;

        private float shownWind = float.NaN;
        private float shownSpeed = float.NaN;
        private int shownXRay = -1;

        public ControlsPresenter(VisualElement root, TurbineDataSimulator simulator, TurbineVisualController visuals,
            TurbineOrbitCamera orbitCamera)
        {
            this.simulator = simulator;
            this.visuals = visuals;
            this.orbitCamera = orbitCamera;

            windSlider = root.Require<Slider>("wind-slider");
            windValue = root.Require<Label>("wind-setting-value");
            windSlider.RegisterValueChangedCallback(HandleWindChanged);
            PlaceWindTicks(root, simulator.Specs);

            for (int i = 0; i < speedButtons.Length; i++)
            {
                float speed = SpeedOptions[i];
                speedButtons[i] = root.Require<Button>(SpeedButtonNames[i]);
                speedHandlers[i] = () => simulator.SimulationSpeed = speed;
                speedButtons[i].clicked += speedHandlers[i];
            }

            xRayButton = root.Require<Button>("xray-button");
            xRayFab = root.Require<Button>("xray-fab");
            xRayButton.clicked += ToggleXRay;
            xRayFab.clicked += ToggleXRay;

            // Without the 3D model there is nothing to see through.
            bool hasVisuals = visuals != null;
            xRayButton.SetEnabled(hasVisuals);
            xRayFab.SetEnabled(hasVisuals);

            resetButton = root.Require<Button>("reset-button");
            resetButton.clicked += ResetAll;

            Sync();
        }

        public void Dispose()
        {
            windSlider.UnregisterValueChangedCallback(HandleWindChanged);
            for (int i = 0; i < speedButtons.Length; i++)
                speedButtons[i].clicked -= speedHandlers[i];
            xRayButton.clicked -= ToggleXRay;
            xRayFab.clicked -= ToggleXRay;
            resetButton.clicked -= ResetAll;
        }

        /// <summary>Called every frame: reflect the simulator state (it may also change from the Inspector).</summary>
        public void Tick()
        {
            Sync();
        }

        private void HandleWindChanged(ChangeEvent<float> evt)
        {
            float snapped = Mathf.Round(evt.newValue / WindStep) * WindStep;
            simulator.SetMeanWindSpeed(snapped);
            ShowWind(snapped);
        }

        private void ToggleXRay()
        {
            if (visuals != null)
                visuals.XRayEnabled = !visuals.XRayEnabled;
        }

        /// <summary>Back to the state a visitor sees on page load. The controls re-sync on the next Tick.</summary>
        private void ResetAll()
        {
            simulator.ResetSimulation();
            if (visuals != null)
                visuals.XRayEnabled = false;
            if (orbitCamera != null)
                orbitCamera.ResetView();
        }

        private void Sync()
        {
            float wind = simulator.MeanWindSpeed;
            if (wind != shownWind)
            {
                // WithoutNotify: moving the knob to match the simulator must not write the value back.
                windSlider.SetValueWithoutNotify(wind);
                ShowWind(wind);
            }

            float speed = simulator.SimulationSpeed;
            if (speed != shownSpeed)
            {
                shownSpeed = speed;
                for (int i = 0; i < speedButtons.Length; i++)
                    speedButtons[i].EnableInClassList("segmented__item--on", Mathf.Approximately(speed, SpeedOptions[i]));
            }

            int xRay = visuals != null && visuals.XRayEnabled ? 1 : 0;
            if (xRay != shownXRay)
            {
                shownXRay = xRay;
                xRayButton.EnableInClassList("segmented__item--on", xRay == 1);
                xRayFab.EnableInClassList("fab--on", xRay == 1);
            }
        }

        private void ShowWind(float wind)
        {
            shownWind = wind;
            windValue.text = UiFormat.Format("{0:0.0} m/s", wind);
        }

        /// <summary>Puts the cut-in / rated / cut-out marks under the slider at their real positions from the specs.</summary>
        private void PlaceWindTicks(VisualElement root, TurbineSpecs specs)
        {
            float range = windSlider.highValue - windSlider.lowValue;
            PlaceTick(root, "wind-tick--cut-in", specs.CutInWindSpeed, "{0:0} cut-in", range);
            PlaceTick(root, "wind-tick--rated", specs.RatedWindSpeed, "{0:0} rated", range);
            PlaceTick(root, "wind-tick--cut-out", specs.CutOutWindSpeed, "{0:0} cut-out", range);
        }

        private void PlaceTick(VisualElement root, string className, float wind, string format, float range)
        {
            Label tick = root.Q<Label>(className: className);
            if (tick == null)
                return;

            tick.text = UiFormat.Format(format, wind);
            tick.style.left = Length.Percent((wind - windSlider.lowValue) / range * 100f);
        }
    }
}
