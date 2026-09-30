using UnityEngine;

namespace WindFarm.Cameras
{
    /// <summary>
    /// Orbit camera around one turbine, framed into the free screen area between the dashboard panels.
    ///
    ///   Framing: the whole turbine (tower base to the top blade tip, rotor width) fits into the viewport rectangle
    ///            the dashboard reports (<see cref="SetViewport"/>), with a shifted projection (see CameraFraming).
    ///   Orbit:   yaw is free, elevation is limited, and the camera never goes below the ground or into the rotor.
    ///   Zoom:    a multiple of the framing distance, so a layout change (phone rotation) keeps the same view.
    ///            Zooming in moves the pivot from the turbine's middle to the hub, where the X-Ray view is.
    ///   Idle:    after a while without input the camera sways slowly from one side of the rotor front to the other
    ///            (attract mode) and returns to the home zoom and height.
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
        private const int EnvelopeSegments = 8;
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
        private float framingMargin = 0.1f;

        [SerializeField, Min(0f), Tooltip("Far clip plane distance beyond the pivot (m). Room for the sky, ground and distant turbines.")]
        private float farClipMargin = 1500f;

        [Header("Orbit")]
        [SerializeField, Range(-180f, 180f), Tooltip("Home view: angle around the turbine from straight in front of the rotor (deg).")]
        private float homeYawOffset = 35f;

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

        [SerializeField, Range(0f, 90f), Tooltip("Sway amplitude to each side of the home view (deg).")]
        private float swayAmplitude = 35f;

        [SerializeField, Min(1f), Tooltip("Duration of one full back-and-forth sway (s).")]
        private float swayPeriod = 60f;

        [SerializeField, Min(0.1f), Tooltip("Time to blend from the last view into the sway (s).")]
        private float swayBlendTime = 8f;

        private readonly Vector3[] envelope = new Vector3[EnvelopeSegments * 2];

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
        private float swayStartOffset;      // yaw offset from home when the sway began (deg, -180..180)
        private float swayBaseYaw;          // unwrapped home yaw near the camera's yaw when the sway began
        private float swayBaseHomeYaw;      // home yaw (wrapped) at that moment, to follow the nacelle afterwards
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
            targetZoom = 1f;
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
            zoom = targetZoom = 1f;
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
        /// Rotor radius from the mesh bounds (the blades may point anywhere when this runs) and a vertical cylinder
        /// around the tower axis that holds the rotor in every yaw position: framing does not change while orbiting.
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

            Vector3 basePosition = turbine.position;
            Vector3 hubOffset = hub - basePosition;
            float radius = rotorRadius + new Vector2(hubOffset.x, hubOffset.z).magnitude;
            envelopeHalfHeight = (hubOffset.y + rotorRadius) * 0.5f;

            for (int i = 0; i < EnvelopeSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / EnvelopeSegments;
                var ring = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                envelope[i] = ring + Vector3.down * envelopeHalfHeight;
                envelope[i + EnvelopeSegments] = ring + Vector3.up * envelopeHalfHeight;
            }
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
                swaying = true;
                swayTime = 0f;
                swayBaseHomeYaw = HomeYaw();
                swayStartOffset = Mathf.DeltaAngle(swayBaseHomeYaw, targetYaw);
                swayBaseYaw = targetYaw - swayStartOffset;
                swayStartElevation = targetElevation;
                swayStartZoom = targetZoom;
            }

            swayTime += deltaTime;
            float blend = Mathf.SmoothStep(0f, 1f, swayTime / swayBlendTime);
            float swayOffset = swayAmplitude * Mathf.Sin(swayTime * Mathf.PI * 2f / swayPeriod);
            float homeYaw = swayBaseYaw + Mathf.DeltaAngle(swayBaseHomeYaw, HomeYaw());   // follows nacelle yaw

            targetYaw = homeYaw + Mathf.Lerp(swayStartOffset, swayOffset, blend);
            targetElevation = Mathf.Lerp(swayStartElevation, homeElevation, blend);
            targetZoom = Mathf.Lerp(swayStartZoom, 1f, blend);
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

        /// <summary>Camera yaw that looks at the rotor from its front, turned by the home offset.</summary>
        private float HomeYaw()
        {
            // The camera looks along -forward of the rotor (it stands upwind, facing the spinner nose).
            Vector3 front = rotor.forward;
            return Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg + homeYawOffset;
        }

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
