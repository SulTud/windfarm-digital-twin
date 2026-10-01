using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Cameras;
using WindFarm.Simulation;
using WindFarm.Visuals;

namespace WindFarm.UI
{
    /// <summary>
    /// 3D callouts over the turbine, in the <c>callout-layer</c> (between the scene gestures and the dashboard, so
    /// every card and control draws on top of them):
    ///   - Fault: on the roof cooler (the heat exchanger the fan pushes air through; visible without X-Ray) while the
    ///     controller reports a cooling fan fault, with the live winding temperature it causes. Shown at any zoom:
    ///     from far away it says where the fault is.
    ///   - Blade pitch: at the hub once the camera is close (below <see cref="PitchZoomLevel"/>), with the angle and
    ///     what it means (full power / shedding power / feathered).
    ///
    /// Both read only telemetry, so they work for a real source too; the 3D anchors come from the visual model.
    /// Every frame the anchors are projected with the orbit camera's shifted projection
    /// (<c>RuntimePanelUtils.CameraTransformWorldToPanel</c> uses the camera's real projection matrix) and the
    /// callouts are moved with a translate. A callout flips to the other side when its label would leave the screen.
    ///
    /// Also owns the banner's SHOW action: X-Ray on and the camera's drivetrain focus.
    /// </summary>
    internal sealed class SceneCalloutsPresenter
    {
        private const float PitchZoomLevel = 0.7f;      // zoom multiple below which the pitch callout fades in
        private const float FeatheredPitch = 85f;       // deg
        private const float FinePitch = 0.5f;           // deg
        private const float EdgeMargin = 8f;            // px kept between a label and the screen edge
        private const float StackGap = 6f;              // px between two stacked labels

        private readonly VisualElement layer;
        private readonly TurbineSpecs specs;
        private readonly TurbineOrbitCamera orbitCamera;
        private readonly TurbineVisualController visuals;
        private readonly Camera camera;
        private readonly Renderer cooler;
        private readonly Transform rotor;

        private readonly SceneCallout faultCallout;
        private readonly SceneCallout pitchCallout;

        private bool hasTelemetry;
        private TurbineTelemetry latest;
        private int shownTemperature = int.MinValue;
        private int shownPitchTenths = int.MinValue;

        public SceneCalloutsPresenter(VisualElement root, TurbineSpecs specs, TurbineOrbitCamera orbitCamera,
            TurbineVisualController visuals)
        {
            this.specs = specs;
            this.orbitCamera = orbitCamera;
            this.visuals = visuals;
            camera = orbitCamera.GetComponent<Camera>();
            cooler = visuals != null ? visuals.CoolerRenderer : null;
            rotor = visuals != null ? visuals.Rotor : null;

            layer = root.Require<VisualElement>("callout-layer");

            faultCallout = new SceneCallout("COOLING FAN FAILED", "callout--warn");
            pitchCallout = new SceneCallout("BLADE PITCH") { PointsLeft = true };
            layer.Add(faultCallout);
            layer.Add(pitchCallout);
        }

        /// <summary>True if the SHOW action has something to show (the 3D model and the camera are there).</summary>
        public bool CanShowFault => visuals != null;

        public void Dispose()
        {
            faultCallout.RemoveFromHierarchy();
            pitchCallout.RemoveFromHierarchy();
        }

        public void Show(in TurbineTelemetry telemetry)
        {
            latest = telemetry;
            hasTelemetry = true;

            int temperature = Mathf.RoundToInt(telemetry.GeneratorTemperature);
            if (temperature != shownTemperature)
            {
                shownTemperature = temperature;
                faultCallout.SetValue(UiFormat.Format("Generator {0} °C", temperature));
            }

            int pitchTenths = Mathf.RoundToInt(telemetry.BladePitch * 10f);
            if (pitchTenths != shownPitchTenths)
            {
                shownPitchTenths = pitchTenths;
                float pitch = pitchTenths / 10f;
                pitchCallout.SetValue(UiFormat.Format("{0:0.0}°", pitch));
                pitchCallout.SetNote(pitch >= FeatheredPitch ? "feathered · braking"
                    : pitch <= FinePitch ? "fine pitch · full grip"
                    : telemetry.State == TurbineOperatingState.Idle ? "parked"
                    : "shedding power");
            }
        }

        /// <summary>Glides to the drivetrain with X-Ray on (banner SHOW button). The user's next touch ends the hold.</summary>
        public void FocusFault()
        {
            if (visuals != null)
                visuals.XRayEnabled = true;
            orbitCamera.FocusDrivetrain();
        }

        /// <summary>Called every frame from LateUpdate after the camera moved (DashboardController runs late), so the callouts do not trail it.</summary>
        /// <remarks>
        /// Collision rules, found necessary in Play mode tests (the labels overlapped at some angles):
        ///   1. With both shown, the anchor further left points left, the other right, so the lines diverge.
        ///   2. A label flips only if its measured box would leave the screen.
        ///   3. If the two boxes still overlap (same side, or facing each other on a narrow phone screen), the label
        ///      of the higher anchor gets a longer line and stacks above the other.
        /// </remarks>
        public void Tick()
        {
            if (!hasTelemetry || layer.panel == null)
                return;

            bool faultWanted = (latest.Alarms & TurbineAlarms.CoolingFanFault) != 0 && cooler != null;
            bool faultVisible = Project(faultWanted, cooler != null ? cooler.bounds.center : Vector3.zero, out Vector2 faultAt);

            bool pitchWanted = orbitCamera.ZoomLevel < PitchZoomLevel && rotor != null;
            bool pitchVisible = Project(pitchWanted, rotor != null ? rotor.position : Vector3.zero, out Vector2 pitchAt);

            // Preferred sides: alone, the pitch label points left and the fault label right; together they diverge.
            bool pitchLeft = !faultVisible || pitchAt.x <= faultAt.x;
            bool faultLeft = pitchVisible && !pitchLeft;
            pitchLeft = FitSide(pitchCallout, pitchAt, pitchLeft);
            faultLeft = FitSide(faultCallout, faultAt, faultLeft);

            float pitchLift = 0f;
            float faultLift = 0f;
            if (faultVisible && pitchVisible)
            {
                Rect faultBox = faultCallout.BoxRect(faultAt, faultLeft, 0f);
                Rect pitchBox = pitchCallout.BoxRect(pitchAt, pitchLeft, 0f);
                if (Overlaps(faultBox, pitchBox))
                {
                    // Lift the label of the higher anchor (smaller y; the fault on a tie) until its box clears the other.
                    if (faultAt.y <= pitchAt.y + 1f)
                        faultLift = faultBox.yMax - pitchBox.yMin + StackGap;
                    else
                        pitchLift = pitchBox.yMax - faultBox.yMin + StackGap;
                }
            }

            Apply(faultCallout, faultVisible, faultAt, faultLeft, faultLift);
            Apply(pitchCallout, pitchVisible, pitchAt, pitchLeft, pitchLift);
        }

        private bool Project(bool wanted, Vector3 world, out Vector2 panelPosition)
        {
            panelPosition = default;
            if (!wanted || camera.WorldToViewportPoint(world).z <= 0f)
                return false;

            panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(layer.panel, world, camera);
            return true;
        }

        /// <summary>Keeps the preferred side unless the measured box would leave the screen there and fits on the other.</summary>
        private bool FitSide(SceneCallout callout, Vector2 position, bool preferLeft)
        {
            float width = layer.resolvedStyle.width;
            bool roomLeft = callout.BoxRect(position, true, 0f).xMin >= EdgeMargin;
            bool roomRight = callout.BoxRect(position, false, 0f).xMax <= width - EdgeMargin;
            return preferLeft ? roomLeft || !roomRight : !roomRight && roomLeft;
        }

        private static bool Overlaps(Rect a, Rect b) =>
            a.xMin < b.xMax + StackGap && b.xMin < a.xMax + StackGap &&
            a.yMin < b.yMax + StackGap && b.yMin < a.yMax + StackGap;

        private static void Apply(SceneCallout callout, bool visible, Vector2 position, bool pointsLeft, float lift)
        {
            if (!visible)
            {
                callout.SetShown(false);
                return;
            }

            callout.PointsLeft = pointsLeft;
            callout.Lift = lift;
            callout.MoveTo(position);
            callout.SetShown(true);
        }
    }
}
