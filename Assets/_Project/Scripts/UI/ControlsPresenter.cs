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
    /// floating button on phones), the fault scenario toggle (cooling fan failure / repair; dock on PC, floating
    /// button on phones) and the reset button (restarts the simulation, turns X-Ray off, camera home).
    ///
    /// These drive the mock simulator directly (SetMeanWindSpeed / SimulationSpeed / InjectFault / ResetSimulation), so they take the concrete
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
        private readonly Button faultButton;
        private readonly Button faultFab;
        private readonly Button lightsButton;

        private float shownWind = float.NaN;
        private float shownSpeed = float.NaN;
        private int shownXRay = -1;
        private int shownFault = -1;
        private int shownLights = -1;

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

            faultButton = root.Require<Button>("fault-button");
            faultFab = root.Require<Button>("fault-fab");
            faultButton.clicked += ToggleFault;
            faultFab.clicked += ToggleFault;

            // Scene decoration, not a simulator control: all obstruction lights through one global shader value.
            lightsButton = root.Require<Button>("lights-button");
            lightsButton.clicked += ToggleLights;

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
            faultButton.clicked -= ToggleFault;
            faultFab.clicked -= ToggleFault;
            lightsButton.clicked -= ToggleLights;
        }

        private static void ToggleLights() => ObstructionLights.LightsOn = !ObstructionLights.LightsOn;

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

        /// <summary>Stops the cooling fan, or repairs it (the technician's visit). Everything after follows from physics.</summary>
        private void ToggleFault()
        {
            if ((simulator.ActiveFaults & TurbineFaults.CoolingFanFailure) != 0)
                simulator.ClearFault(TurbineFaults.CoolingFanFailure);
            else
                simulator.InjectFault(TurbineFaults.CoolingFanFailure);
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

            int fault = (simulator.ActiveFaults & TurbineFaults.CoolingFanFailure) != 0 ? 1 : 0;
            if (fault != shownFault)
            {
                shownFault = fault;
                // While the fault is active the same button repairs it.
                faultButton.text = fault == 1 ? "REPAIR FAN" : "FAN FAILURE";
                faultFab.text = fault == 1 ? "REPAIR" : "FAULT";
                faultButton.EnableInClassList("segmented__item--alert", fault == 1);
                faultFab.EnableInClassList("fab--alert", fault == 1);
            }

            int lights = ObstructionLights.LightsOn ? 1 : 0;
            if (lights != shownLights)
            {
                shownLights = lights;
                lightsButton.EnableInClassList("segmented__item--on", lights == 1);
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
