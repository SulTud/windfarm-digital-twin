using UnityEngine;
using WindFarm.Simulation;

namespace WindFarm.Visuals
{
    /// <summary>
    /// Drives the 3D turbine model from telemetry:
    ///   RotorRpm             -> Rotor spin (deg/s = RPM x 6)
    ///   State (+ wind speed) -> Blade pitch (feather to 90 deg on storm shutdown)
    ///   GeneratorTemperature -> Generator / Cooler heat color
    ///
    /// Telemetry arrives at 5 Hz with sensor noise, so every value is smoothed each frame (exponential smoothing)
    /// and pitch is additionally rate limited like a real pitch drive.
    ///
    /// Spin and pitch are applied on top of the cached initial local rotations, so the imported 6 deg rotor tilt and
    /// the 120 / 240 deg blade offsets are preserved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurbineVisualController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private const string EmissionKeyword = "_EMISSION";

        // Unity cannot serialize interfaces; take the concrete component and use it through the interface.
        [SerializeField] private TurbineDataSimulator simulator;

        [Header("Rotor")]
        [SerializeField, Tooltip("Spinner object. Spins around its local +Z axis (shaft axis, includes the tilt).")]
        private Transform rotor;

        [SerializeField, Min(0.01f), Tooltip("Smoothing time constant for the measured RPM (s). Hides 5 Hz steps and sensor noise.")]
        private float rpmSmoothingTime = 0.6f;

        [SerializeField, Tooltip("Flip if the rotor turns counter-clockwise seen from upwind (it should turn clockwise).")]
        private bool invertSpinDirection;

        [Header("Blade Pitch")]
        [SerializeField, Tooltip("Blade objects. Each pitches around its local +Y axis (along the blade).")]
        private Transform[] blades = new Transform[0];

        [SerializeField, Range(0f, 90f), Tooltip("Pitch while idle below cut-in (deg). Real turbines park near feather.")]
        private float idlePitch = 70f;

        [SerializeField, Range(0f, 90f), Tooltip("Pitch during storm shutdown (deg). 90 = fully feathered, chord parallel to the wind.")]
        private float featherPitch = 90f;

        [SerializeField, Min(0f), Tooltip("Wind speed at which rated power is reached (m/s). Pitch starts rising above it.")]
        private float ratedWindSpeed = 11f;

        [SerializeField, Range(0f, 45f), Tooltip("Pitch at cut-out wind speed while at rated power (deg). " +
                                                 "Visual approximation: the power model caps power but does not simulate pitch.")]
        private float pitchAtCutOut = 25f;

        [SerializeField, Min(0.1f), Tooltip("Maximum pitch rate (deg/s). Real pitch drives move about 5-10 deg/s.")]
        private float pitchRate = 8f;

        [SerializeField, Min(0.01f), Tooltip("Smoothing time constant for the wind speed used by the rated-power pitch (s).")]
        private float windSmoothingTime = 2f;

        [SerializeField, Tooltip("Flip if feathering turns the trailing edge (instead of the leading edge) into the wind.")]
        private bool invertPitchDirection;

        [Header("Heat Color")]
        [SerializeField, Tooltip("Renderers tinted by the generator temperature (Generator, Cooler). Their first material is instanced.")]
        private Renderer[] heatRenderers = new Renderer[0];

        [SerializeField, Tooltip("Temperature mapped to the left end of the gradient (deg C).")]
        private float coldTemperature = 20f;

        [SerializeField, Tooltip("Temperature mapped to the right end of the gradient (deg C). ~76 deg C is the equilibrium at rated power.")]
        private float hotTemperature = 100f;

        [SerializeField, Tooltip("Base color over the temperature range.")]
        private Gradient heatGradient = CreateDefaultHeatGradient();

        [SerializeField, Tooltip("Temperature at which the emissive glow starts (deg C). " +
                                 "Glow only works if Emission is enabled on the material (keeps the shader variant in WebGL builds).")]
        private float glowStartTemperature = 70f;

        [SerializeField, Min(0f), Tooltip("Emission intensity multiplier at the hot temperature.")]
        private float maxGlowIntensity = 1.5f;

        [SerializeField, Min(0.01f), Tooltip("Smoothing time constant for the measured temperature (s).")]
        private float temperatureSmoothingTime = 1f;

        [Header("X-Ray")]
        [SerializeField, Tooltip("Nacelle housing renderer that fades out to reveal the drivetrain.")]
        private Renderer housingRenderer;

        [SerializeField, Tooltip("Transparent copy of the housing material (URP Lit, Surface Type = Transparent). " +
                                 "Must be an asset so WebGL builds keep the transparent shader variant.")]
        private Material xRayMaterial;

        [SerializeField, Range(0f, 1f), Tooltip("Housing opacity while X-Ray is fully on.")]
        private float xRayOpacity = 0.12f;

        [SerializeField, Min(0.01f), Tooltip("Fade duration when X-Ray is toggled (s).")]
        private float xRayFadeTime = 0.5f;

        [SerializeField, Tooltip("Toggle in Play mode to test. The dashboard sets XRayEnabled later.")]
        private bool xRayEnabled;

        private ITurbineTelemetrySource source;
        private TurbineTelemetry latest;
        private bool hasTelemetry;

        private Quaternion rotorInitialRotation;
        private Quaternion[] bladeInitialRotations;
        private Material[] heatMaterials;

        private float smoothedRpm;
        private float smoothedWindSpeed;
        private float smoothedTemperature;
        private float spinAngle;
        private float currentPitch;

        private Material housingOpaqueMaterial;
        private Material xRayInstance;
        private Color housingBaseColor;
        private UnityEngine.Rendering.ShadowCastingMode housingShadowMode;
        private float xRayBlend;

        /// <summary>Fades the nacelle housing to transparent to reveal the drivetrain and the heat colors.</summary>
        public bool XRayEnabled
        {
            get => xRayEnabled;
            set => xRayEnabled = value;
        }

        private void Awake()
        {
            if (rotor != null)
                rotorInitialRotation = rotor.localRotation;

            bladeInitialRotations = new Quaternion[blades.Length];
            for (int i = 0; i < blades.Length; i++)
            {
                if (blades[i] != null)
                    bladeInitialRotations[i] = blades[i].localRotation;
            }

            // Per-instance copies so each turbine of a future wind farm can show its own temperature.
            heatMaterials = new Material[heatRenderers.Length];
            for (int i = 0; i < heatRenderers.Length; i++)
            {
                if (heatRenderers[i] != null)
                    heatMaterials[i] = heatRenderers[i].material;
            }

            if (housingRenderer != null && xRayMaterial != null)
            {
                housingOpaqueMaterial = housingRenderer.sharedMaterial;
                housingShadowMode = housingRenderer.shadowCastingMode;
                xRayInstance = new Material(xRayMaterial);
                housingBaseColor = xRayMaterial.GetColor(BaseColorId);
            }
        }

        private void OnEnable()
        {
            // Unity's null check (destroyed / unassigned object) must be done on the concrete type, not through the interface.
            if (simulator == null)
            {
                Debug.LogWarning($"{nameof(TurbineVisualController)}: simulator is not assigned.", this);
                return;
            }

            source = simulator;
            source.TelemetryUpdated += HandleTelemetry;

            // The simulator may not have run Awake yet (a default snapshot has no id); then the first event initializes us.
            if (source.LatestTelemetry.TurbineId != null)
                HandleTelemetry(source.LatestTelemetry);
        }

        private void OnDisable()
        {
            if (source == null)
                return;

            source.TelemetryUpdated -= HandleTelemetry;
            source = null;
            hasTelemetry = false;
        }

        private void OnDestroy()
        {
            if (xRayInstance != null)
                Destroy(xRayInstance);

            if (heatMaterials == null)
                return;

            foreach (Material material in heatMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }

        private void HandleTelemetry(TurbineTelemetry telemetry)
        {
            latest = telemetry;

            if (hasTelemetry)
                return;

            // First sample: snap instead of animating from zero (like a particle system Prewarm).
            hasTelemetry = true;
            smoothedRpm = telemetry.RotorRpm;
            smoothedWindSpeed = telemetry.WindSpeed;
            smoothedTemperature = telemetry.GeneratorTemperature;
            currentPitch = CalculateTargetPitch(telemetry.State, smoothedWindSpeed);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            // X-Ray is a view setting, independent of telemetry.
            UpdateXRay(deltaTime);

            if (!hasTelemetry)
                return;

            smoothedRpm = Smooth(smoothedRpm, latest.RotorRpm, rpmSmoothingTime, deltaTime);
            smoothedWindSpeed = Smooth(smoothedWindSpeed, latest.WindSpeed, windSmoothingTime, deltaTime);
            smoothedTemperature = Smooth(smoothedTemperature, latest.GeneratorTemperature, temperatureSmoothingTime, deltaTime);

            UpdateRotor(deltaTime);
            UpdatePitch(deltaTime);
            UpdateHeatColor();
        }

        private void UpdateRotor(float deltaTime)
        {
            if (rotor == null)
                return;

            // 1 RPM = 360 deg / 60 s = 6 deg/s. Repeat keeps the angle small so float precision never degrades.
            float direction = invertSpinDirection ? -1f : 1f;
            spinAngle = Mathf.Repeat(spinAngle + smoothedRpm * 6f * direction * deltaTime, 360f);

            rotor.localRotation = rotorInitialRotation * Quaternion.AngleAxis(spinAngle, Vector3.forward);
        }

        private void UpdatePitch(float deltaTime)
        {
            float targetPitch = CalculateTargetPitch(latest.State, smoothedWindSpeed);
            currentPitch = Mathf.MoveTowards(currentPitch, targetPitch, pitchRate * deltaTime);

            float signedPitch = invertPitchDirection ? -currentPitch : currentPitch;
            Quaternion pitchRotation = Quaternion.AngleAxis(signedPitch, Vector3.up);

            for (int i = 0; i < blades.Length; i++)
            {
                if (blades[i] != null)
                    blades[i].localRotation = bladeInitialRotations[i] * pitchRotation;
            }
        }

        private float CalculateTargetPitch(TurbineOperatingState state, float windSpeed)
        {
            switch (state)
            {
                case TurbineOperatingState.Idle:
                    return idlePitch;

                case TurbineOperatingState.StormShutdown:
                    return featherPitch;

                case TurbineOperatingState.RatedPower:
                    // Above rated wind the blades pitch out to shed the excess power and hold rated speed.
                    float cutOut = simulator != null ? simulator.Specs.CutOutWindSpeed : 25f;
                    return Mathf.Lerp(0f, pitchAtCutOut, Mathf.InverseLerp(ratedWindSpeed, cutOut, windSpeed));

                default:
                    return 0f; // Producing: fine pitch, maximum aerodynamic efficiency
            }
        }

        private void UpdateHeatColor()
        {
            float heat = Mathf.InverseLerp(coldTemperature, hotTemperature, smoothedTemperature);
            float glow = Mathf.InverseLerp(glowStartTemperature, hotTemperature, smoothedTemperature) * maxGlowIntensity;
            Color color = heatGradient.Evaluate(heat);

            foreach (Material material in heatMaterials)
            {
                if (material == null)
                    continue;

                material.SetColor(BaseColorId, color);
                if (material.IsKeywordEnabled(EmissionKeyword))
                    material.SetColor(EmissionColorId, color * glow);
            }
        }

        /// <remarks>
        /// Swaps between two material assets instead of switching one material to transparent at runtime:
        /// WebGL builds strip shader variants that no material asset uses, so a runtime switch would silently fail there.
        /// </remarks>
        private void UpdateXRay(float deltaTime)
        {
            if (xRayInstance == null)
                return;

            float target = xRayEnabled ? 1f : 0f;
            if (Mathf.Approximately(xRayBlend, target))
                return;

            xRayBlend = Mathf.MoveTowards(xRayBlend, target, deltaTime / xRayFadeTime);

            if (xRayBlend <= 0f)
            {
                // Fully opaque again: back to the original material (writes depth, casts shadows).
                housingRenderer.sharedMaterial = housingOpaqueMaterial;
                housingRenderer.shadowCastingMode = housingShadowMode;
                return;
            }

            Color color = housingBaseColor;
            color.a = Mathf.Lerp(1f, xRayOpacity, Mathf.SmoothStep(0f, 1f, xRayBlend));
            xRayInstance.SetColor(BaseColorId, color);

            housingRenderer.sharedMaterial = xRayInstance;
            // A transparent housing still casts a full shadow and would darken the drivetrain inside.
            housingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Frame-rate independent exponential smoothing toward a moving target.</summary>
        private static float Smooth(float current, float target, float timeConstant, float deltaTime) =>
            current + (target - current) * (1f - Mathf.Exp(-deltaTime / timeConstant));

        private static Gradient CreateDefaultHeatGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.35f, 0.50f, 0.70f), 0f),    // cold: steel blue
                    new GradientColorKey(new Color(0.70f, 0.70f, 0.70f), 0.45f), // warm: neutral grey
                    new GradientColorKey(new Color(1.00f, 0.55f, 0.10f), 0.8f),  // hot: orange
                    new GradientColorKey(new Color(1.00f, 0.10f, 0.05f), 1f),    // overheating: red
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

#if UNITY_EDITOR
        /// <summary>Auto-wires references by the model's object names when the component is added or reset.</summary>
        private void Reset()
        {
            simulator = FindAnyObjectByType<TurbineDataSimulator>();
            rotor = FindChildRecursive(transform, "Rotor");
            blades = new[]
            {
                FindChildRecursive(transform, "Blade_1"),
                FindChildRecursive(transform, "Blade_2"),
                FindChildRecursive(transform, "Blade_3"),
            };

            Transform generator = FindChildRecursive(transform, "Generator");
            Transform cooler = FindChildRecursive(transform, "Cooler");
            heatRenderers = new[]
            {
                generator != null ? generator.GetComponent<Renderer>() : null,
                cooler != null ? cooler.GetComponent<Renderer>() : null,
            };

            Transform nacelle = FindChildRecursive(transform, "Nacelle");
            housingRenderer = nacelle != null ? nacelle.GetComponent<Renderer>() : null;
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
