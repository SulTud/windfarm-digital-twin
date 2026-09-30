using UnityEngine;

namespace WindFarm.Cameras
{
    /// <summary>
    /// Orbit camera around one turbine, framed into the free screen area between the dashboard panels.
    ///
    ///   Framing: the whole turbine (tower base to the top blade tip, rotor diameter wide) fits into the viewport
    ///            rectangle the dashboard reports (<see cref="SetViewport"/>), with a shifted projection (see CameraFraming).
    ///   Orbit:   yaw is free, elevation is limited, and the camera never goes below the ground or into the rotor.
    ///   Zoom:    a multiple of the framing distance, so a layout change (phone rotation) keeps the same view.
    ///            Zooming in moves the pivot from the turbine's middle to the hub, where the X-Ray view is.
    ///   Idle:    after a while without input the camera sways slowly to both sides of the rotor front (attract
    ///            mode, centered on the front, not on the slightly diagonal home view) and returns to the home zoom
    ///            and height.
    ///
    /// The front of the turbine is the rotor's forward axis, not a world direction, so the home view and the sway
    /// follow the nacelle once yaw is simulated. Manual orbit stays in world space: the nacelle visibly turns.
    /// All motion eases toward targets with exponential smoothing (frame-rate independent, like SmoothDamp) on
    /// unscaled time, so the simulation speed does not change the camera feel.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class TurbineOrbitCamera : MonoBehaviour
    {
        private const float FallbackRotorRadius = 50f;
        private const float FitSmoothingTime = 0.25f;     // s; layout changes re-frame smoothly
        private const float NearClipFraction = 0.02f;     // near plane at 2 % of the distance: depth precision far away

        [Header("Target")]
        [SerializeField, Tooltip("Turbine root. Its position is the tower base on the ground.")]
        private Transform turbine;

        [SerializeField, Tooltip("Rotor (hub). Its forward axis points upwind and defines the front of the turbine. " +
            "The rotor radius is measured from the mesh bounds below it.")]
        private Transform rotor;

        [Header("Framing")]
        [SerializeField, Range(20f, 70f), Tooltip("Vertical field of view (deg). Narrow keeps the tall tower undistorted.")]
        private float fieldOfView = 40f;

        [SerializeField, Range(0f, 0.4f), Tooltip("Free border around the turbine, as a fraction of the free screen area's half size.")]
        private float framingMargin = 0.08f;

        [SerializeField, Min(0f), Tooltip("Far clip plane distance beyond the pivot (m). Room for the sky, ground and distant turbines.")]
        private float farClipMargin = 1500f;

        [Header("Orbit")]
        [SerializeField, Range(-180f, 180f), Tooltip("Home view: angle around the turbine from straight in front of the " +
            "rotor (deg). A small angle shows the nacelle side too; 0 looks flat.")]
        private float homeYawOffset = 20f;

        [SerializeField, Range(0.5f, 1.3f), Tooltip("Home view distance as a multiple of the framing distance (1 = the " +
            "whole turbine just fits). Below 1 the view also rises toward the hub.")]
        private float homeZoom = 1f;

        [SerializeField, Range(-30f, 80f), Tooltip("Home view: camera angle above the pivot (deg). Negative looks up at the turbine.")]
        private float homeElevation = -4f;

        [SerializeField, Range(-30f, 0f), Tooltip("Lowest camera angle (deg). The ground clearance may raise it further.")]
        private float minElevation = -10f;

        [SerializeField, Range(0f, 85f), Tooltip("Highest camera angle (deg).")]
        private float maxElevation = 60f;

        [SerializeField, Min(0.01f), Tooltip("Orbit angle per logical pixel of drag (deg).")]
        private float orbitSensitivity = 0.3f;

        [SerializeField, Min(0.01f), Tooltip("Smoothing time constant for orbit and zoom (s).")]
        private float smoothingTime = 0.12f;

        [Header("Zoom and clearance")]
        [SerializeField, Range(1f, 2f), Tooltip("Farthest zoom as a multiple of the framing distance.")]
        private float maxZoomOut = 1.3f;

        [SerializeField, Min(1f), Tooltip("Closest distance to the pivot (m). Up close the camera looks at the hub.")]
        private float minDistance = 30f;

        [SerializeField, Min(0f), Tooltip("Distance kept in front of / behind the rotor plane (m). The 6 deg tilt swings " +
            "the blade tips ~6 m out of the hub plane.")]
        private float rotorPlaneClearance = 15f;

        [SerializeField, Min(0f), Tooltip("Distance kept outside the blade tips when the camera is beside the rotor (m).")]
        private float bladeTipClearance = 10f;

        [SerializeField, Min(0f), Tooltip("Lowest camera height above the turbine base (m).")]
        private float minCameraHeight = 5f;

        [Header("Idle sway (attract mode)")]
        [SerializeField, Min(1f), Tooltip("Seconds without any touch, click or wheel before the camera starts to sway.")]
        private float idleDelay = 20f;

        [SerializeField, Range(0f, 90f), Tooltip("Sway amplitude to each side of the rotor front (deg).")]
        private float swayAmplitude = 35f;

        [SerializeField, Min(1f), Tooltip("Duration of one full back-and-forth sway (s).")]
        private float swayPeriod = 60f;

        [SerializeField, Min(0.1f), Tooltip("Time to blend from the last view into the sway (s).")]
        private float swayBlendTime = 8f;

        private readonly Vector3[] envelope = new Vector3[4];

        private Camera targetCamera;
        private Rect viewport = new Rect(0f, 0f, 1f, 1f);
        private bool hasViewport;
        private float rotorRadius;
        private float envelopeHalfHeight;
        private float fitDistance;
        private bool hasFit;

        private float yaw;
        private float elevation;
        private float zoom = 1f;
        private float targetYaw;
        private float targetElevation;
        private float targetZoom = 1f;

        private float idleTime;
        private bool swaying;
        private float swayTime;
        private float swayStartOffset;      // yaw offset from the rotor front when the sway began (deg, -180..180)
        private float swayPhase;            // sine phase that starts the sway at that offset (rad)
        private float swayBaseYaw;          // unwrapped front yaw near the camera's yaw when the sway began
        private float swayBaseFrontYaw;     // front yaw (wrapped) at that moment, to follow the nacelle afterwards
        private float swayStartElevation;
        private float swayStartZoom;
        private bool initialized;

        /// <summary>Current distance from the camera to its pivot (m). Useful for fog and level of detail.</summary>
        public float Distance { get; private set; }

        /// <summary>Farthest distance the camera can reach in the current layout (m).</summary>
        public float MaxDistance => fitDistance * maxZoomOut;

        /// <summary>Sets the free screen rectangle to frame the turbine into (normalized, origin bottom-left).</summary>
        public void SetViewport(Rect normalized)
        {
            // A collapsed area (hidden layout, first frame) would make the distance explode: keep the last one.
            if (normalized.width < 0.05f || normalized.height < 0.05f || normalized == viewport)
                return;

            viewport = normalized;
            if (!hasViewport)
            {
                hasViewport = true;
                hasFit = false;   // the first real layout snaps instead of zooming in from the full screen
            }
        }

        /// <summary>Turns the camera around the turbine by a drag in logical pixels (x right, y down).</summary>
        public void Orbit(Vector2 dragPixels)
        {
            NotifyUserInput();
            targetYaw += dragPixels.x * orbitSensitivity;
            targetElevation = Mathf.Clamp(targetElevation + dragPixels.y * orbitSensitivity, minElevation, maxElevation);
        }

        /// <summary>Multiplies the distance (factor above 1 zooms out).</summary>
        public void Zoom(float factor)
        {
            NotifyUserInput();
            if (factor > 0f)
                targetZoom = ClampZoom(targetZoom * factor);
        }

        /// <summary>Glides back to the home view in front of the rotor.</summary>
        public void ResetView()
        {
            NotifyUserInput();
            targetYaw = UnwrapNear(HomeYaw(), targetYaw);
            targetElevation = homeElevation;
            targetZoom = homeZoom;
        }

        /// <summary>Any user input (also on the dashboard) restarts the idle timer and stops the sway where it is.</summary>
        public void NotifyUserInput()
        {
            idleTime = 0f;
            swaying = false;
        }

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
        }

        private void Start()
        {
            if (turbine == null || rotor == null)
            {
                Debug.LogWarning($"{nameof(TurbineOrbitCamera)}: turbine or rotor is not assigned.", this);
                enabled = false;
                return;
            }

            MeasureTurbine();
            yaw = targetYaw = HomeYaw();
            elevation = targetElevation = homeElevation;
            zoom = targetZoom = homeZoom;
            initialized = true;
        }

        private void OnDisable()
        {
            if (targetCamera != null)
                targetCamera.ResetProjectionMatrix();
        }

        private void LateUpdate()
        {
            if (!initialized)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            UpdateFitDistance(deltaTime);
            UpdateIdleSway(deltaTime);

            targetZoom = ClampZoom(targetZoom);
            float blend = 1f - Mathf.Exp(-deltaTime / smoothingTime);
            yaw += (targetYaw - yaw) * blend;
            elevation += (targetElevation - elevation) * blend;
            zoom += (targetZoom - zoom) * blend;

            ApplyPose();
        }

        /// <summary>
        /// Rotor radius from the mesh bounds (the blades may point anywhere when this runs) and the framing envelope:
        /// a flat upright rectangle through the tower axis, rotor diameter wide, from the base to the top blade tip.
        /// It is the turbine's silhouette seen from the front. A cylinder around the tower (every yaw position) was too
        /// cautious: its near edge pushed the camera back and the turbine filled only ~2/3 of the free area.
        /// </summary>
        private void MeasureTurbine()
        {
            Vector3 hub = rotor.position;
            rotorRadius = 0f;
            foreach (MeshFilter filter in rotor.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                    continue;

                Bounds bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    rotorRadius = Mathf.Max(rotorRadius, Vector3.Distance(filter.transform.TransformPoint(local), hub));
                }
            }

            if (rotorRadius <= 0f)
            {
                Debug.LogWarning($"{nameof(TurbineOrbitCamera)}: no meshes below the rotor, using {FallbackRotorRadius} m.", this);
                rotorRadius = FallbackRotorRadius;
            }

            envelopeHalfHeight = (hub.y - turbine.position.y + rotorRadius) * 0.5f;

            // Fitted with the camera looking along world +Z (UpdateFitDistance), so the rectangle spans world X.
            envelope[0] = new Vector3(-rotorRadius, -envelopeHalfHeight, 0f);
            envelope[1] = new Vector3(rotorRadius, -envelopeHalfHeight, 0f);
            envelope[2] = new Vector3(-rotorRadius, envelopeHalfHeight, 0f);
            envelope[3] = new Vector3(rotorRadius, envelopeHalfHeight, 0f);
        }

        /// <summary>Distance at which the turbine fills the viewport, seen from the home elevation.</summary>
        private void UpdateFitDistance(float deltaTime)
        {
            Quaternion view = Quaternion.Euler(homeElevation, 0f, 0f);
            float distance = CameraFraming.FitDistance(envelope, view * Vector3.right, view * Vector3.up,
                view * Vector3.forward, fieldOfView, targetCamera.aspect, viewport, framingMargin);
            if (distance <= 0f)
                return;

            if (!hasFit)
            {
                hasFit = true;
                fitDistance = distance;
                return;
            }

            fitDistance += (distance - fitDistance) * (1f - Mathf.Exp(-deltaTime / FitSmoothingTime));
        }

        private float ClampZoom(float value) =>
            Mathf.Clamp(value, fitDistance > 0f ? Mathf.Min(1f, minDistance / fitDistance) : 1f, maxZoomOut);

        private void UpdateIdleSway(float deltaTime)
        {
            if (!swaying)
            {
                idleTime += deltaTime;
                if (idleTime < idleDelay)
                    return;

                // Start from wherever the user left the camera; the offset keeps the shorter way back to the front.
                // Inside the sway range the sine starts right at that offset (the home view is), so it just carries on.
                swaying = true;
                swayTime = 0f;
                swayBaseFrontYaw = FrontYaw();
                swayStartOffset = Mathf.DeltaAngle(swayBaseFrontYaw, targetYaw);
                swayBaseYaw = targetYaw - swayStartOffset;
                swayPhase = swayAmplitude > 0f ? Mathf.Asin(Mathf.Clamp(swayStartOffset / swayAmplitude, -1f, 1f)) : 0f;
                swayStartElevation = targetElevation;
                swayStartZoom = targetZoom;
            }

            swayTime += deltaTime;
            float blend = Mathf.SmoothStep(0f, 1f, swayTime / swayBlendTime);
            float swayOffset = swayAmplitude * Mathf.Sin(swayPhase + swayTime * Mathf.PI * 2f / swayPeriod);
            float frontYaw = swayBaseYaw + Mathf.DeltaAngle(swayBaseFrontYaw, FrontYaw());   // follows nacelle yaw

            targetYaw = frontYaw + Mathf.Lerp(swayStartOffset, swayOffset, blend);
            targetElevation = Mathf.Lerp(swayStartElevation, homeElevation, blend);
            targetZoom = Mathf.Lerp(swayStartZoom, homeZoom, blend);
        }

        private void ApplyPose()
        {
            Vector3 hub = rotor.position;
            Vector3 center = turbine.position + Vector3.up * envelopeHalfHeight;

            // Zooming in shifts the focus from the whole turbine to the hub and nacelle.
            float focus = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.5f, zoom));
            Vector3 pivot = Vector3.Lerp(center, hub, focus);

            Quaternion rotation = Quaternion.Euler(elevation, yaw, 0f);
            Vector3 forward = rotation * Vector3.forward;
            float distance = Mathf.Max(zoom * fitDistance, RotorClearanceDistance(-forward));

            // Ground clearance: raise the camera angle rather than move the pivot. Also stops the target from winding up.
            float lowest = Mathf.Asin(Mathf.Clamp((turbine.position.y + minCameraHeight - pivot.y) / distance, -1f, 1f)) *
                Mathf.Rad2Deg;
            if (elevation < lowest)
            {
                elevation = lowest;
                targetElevation = Mathf.Max(targetElevation, lowest);
                rotation = Quaternion.Euler(elevation, yaw, 0f);
                forward = rotation * Vector3.forward;
            }

            transform.SetPositionAndRotation(pivot - forward * distance, rotation);
            Distance = distance;

            float near = Mathf.Clamp(distance * NearClipFraction, 0.3f, 10f);
            float far = distance + farClipMargin;
            targetCamera.fieldOfView = fieldOfView;
            targetCamera.nearClipPlane = near;
            targetCamera.farClipPlane = far;
            targetCamera.projectionMatrix = CameraFraming.ShiftedProjection(
                Matrix4x4.Perspective(fieldOfView, targetCamera.aspect, near, far), viewport);
        }

        /// <summary>
        /// Closest safe distance in a direction from the pivot: in front of or behind the rotor the camera may come
        /// close, beside it the blades sweep through, so it stays outside the tips.
        /// </summary>
        private float RotorClearanceDistance(Vector3 directionFromPivot)
        {
            Vector3 axis = rotor.forward;
            float along = Mathf.Abs(Vector3.Dot(directionFromPivot, axis));
            float beside = rotorRadius + bladeTipClearance;
            float inFront = along > 0.001f ? rotorPlaneClearance / along : beside;
            return Mathf.Max(minDistance, Mathf.Min(inFront, beside));
        }

        /// <summary>Camera yaw that looks at the rotor straight from its front.</summary>
        private float FrontYaw()
        {
            // The camera looks along -forward of the rotor (it stands upwind, facing the spinner nose).
            Vector3 front = rotor.forward;
            return Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;
        }

        private float HomeYaw() => FrontYaw() + homeYawOffset;

        private static float UnwrapNear(float angle, float reference) => reference + Mathf.DeltaAngle(reference, angle);

#if UNITY_EDITOR
        /// <summary>Auto-wires the turbine by the model's object names when the component is added or reset.</summary>
        private void Reset()
        {
            var visuals = FindAnyObjectByType<WindFarm.Visuals.TurbineVisualController>();
            if (visuals == null)
                return;

            turbine = visuals.transform;
            rotor = FindChildRecursive(turbine, "Rotor");
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                    return child;

                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }
#endif
    }
}
