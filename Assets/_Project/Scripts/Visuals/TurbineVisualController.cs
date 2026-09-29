using UnityEngine;
using WindFarm.Simulation;

namespace WindFarm.Visuals
{
    /// <summary>
    /// Drives the 3D turbine model from telemetry:
    ///   RotorRpm             -> Rotor spin (deg/s = RPM x 6)
    ///   BladePitch           -> Blade pitch (the simulated pitch controller: fine pitch, rated-power pitching,
    ///                           park near feather when idle, full feather on storm shutdown)
    ///   GeneratorTemperature -> Generator / Cooler heat color
    ///   RotorRpm             -> MainShaft spin (same angle as the rotor, no gearbox in between)
    ///   State + RotorRpm     -> Brake caliper glows red while the mechanical brake holds the rotor
    ///
    /// Telemetry arrives at 5 Hz with sensor noise, so every value is smoothed each frame (exponential smoothing).
    /// The pitch rate limit lives in the simulator now, where it also affects the physics.
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

        [SerializeField, Min(0.01f), Tooltip("Smoothing time constant for the measured pitch (s). Short: the simulator already " +
                                             "limits the pitch rate, this only hides the 5 Hz steps.")]
        private float pitchSmoothingTime = 0.25f;

        [SerializeField, Tooltip("Flip if feathering turns the trailing edge (instead of the leading edge) into the wind.")]
        private bool invertPitchDirection;

        [Header("Heat Color")]
        [SerializeField, Tooltip("Renderers tinted by the generator temperature (Generator, Cooler). Their first material is instanced.")]
        private Renderer[] heatRenderers = new Renderer[0];

        [SerializeField, Tooltip("At and below this temperature the parts show the cold color (deg C). " +
                                 "The other color stops come from the simulator specs: rated equilibrium, warning and alarm.")]
        private float coldTemperature = 20f;

        [SerializeField, Tooltip("Color at the cold temperature.")]
        private Color coldColor = new Color(0.35f, 0.50f, 0.70f);

        [SerializeField, Tooltip("Color halfway between the cold and the rated temperature.")]
        private Color warmColor = new Color(0.70f, 0.70f, 0.70f);

        [SerializeField, Tooltip("Color at the rated-power equilibrium. Golden so a busy generator looks alive, not alarming.")]
        private Color ratedColor = new Color(0.91f, 0.59f, 0.21f);

        [SerializeField, Tooltip("Color at the warning temperature.")]
        private Color warningColor = new Color(1.00f, 0.45f, 0.08f);

        [SerializeField, Tooltip("Color at and above the alarm temperature. Keep green and blue near zero: tonemapping " +
                                 "shifts a bright glowing red with even a little green toward orange.")]
        private Color alarmColor = new Color(0.80f, 0.02f, 0.01f);

        [SerializeField, Min(0f), Tooltip("Emission intensity at the rated temperature. Glow fades in from the warm stop. " +
                                          "Glow only works if Emission is enabled on the material (keeps the shader variant in WebGL builds).")]
        private float ratedGlowIntensity = 0.3f;

        [SerializeField, Min(0f), Tooltip("Emission intensity at the warning temperature.")]
        private float warningGlowIntensity = 0.6f;

        [SerializeField, Min(0f), Tooltip("Emission intensity at and above the alarm temperature.")]
        private float maxGlowIntensity = 1.5f;

        [SerializeField, Min(0f), Tooltip("Glow pulse frequency at and above the alarm temperature (Hz).")]
        private float alarmPulseFrequency = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("Glow pulse depth at and above the alarm temperature (fraction of the glow).")]
        private float alarmPulseAmount = 0.35f;

        [SerializeField, Min(0.01f), Tooltip("Smoothing time constant for the measured temperature (s).")]
        private float temperatureSmoothingTime = 1f;

        [Header("Drivetrain")]
        [SerializeField, Tooltip("Low-speed shaft. Spins with the rotor around its local +Z axis.")]
        private Transform mainShaft;

        [SerializeField, Tooltip("Brake caliper renderer. Its first material is instanced; enable Emission on it for the glow.")]
        private Renderer brakeCaliperRenderer;

        [SerializeField, Min(0f), Tooltip("During storm shutdown the mechanical brake engages below this rotor speed (RPM). " +
                                          "Real turbines brake aerodynamically (feathering) first; the disc brake only holds a slow rotor.")]
        private float brakeEngageRpm = 3f;

        [SerializeField, Tooltip("Caliper color while the brake is engaged.")]
        private Color brakeEngagedColor = new Color(1f, 0.08f, 0.04f);

        [SerializeField, Min(0f), Tooltip("Emission intensity multiplier while the brake is engaged.")]
        private float brakeGlowIntensity = 2f;

        [SerializeField, Min(0.01f), Tooltip("Fade duration when the brake engages or releases (s).")]
        private float brakeFadeTime = 0.4f;

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
        private Quaternion mainShaftInitialRotation;
        private Quaternion[] bladeInitialRotations;
        private Material[] heatMaterials;

        // Heat color stops: cold, warm, rated, warning, alarm. Refilled every frame (no allocation) so Inspector
        // changes to the colors or the simulator specs apply immediately.
        private readonly float[] heatStopTemperatures = new float[5];
        private readonly Color[] heatStopColors = new Color[5];
        private readonly float[] heatStopGlows = new float[5];

        private Material brakeMaterial;
        private Color brakeReleasedColor;
        private float brakeBlend;

        private float smoothedRpm;
        private float smoothedPitch;
        private float smoothedTemperature;
        private float spinAngle;

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

            if (mainShaft != null)
                mainShaftInitialRotation = mainShaft.localRotation;

            if (brakeCaliperRenderer != null)
            {
                brakeMaterial = brakeCaliperRenderer.material;
                brakeReleasedColor = brakeMaterial.GetColor(BaseColorId);
            }

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

            if (brakeMaterial != null)
                Destroy(brakeMaterial);

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
            smoothedPitch = telemetry.BladePitch;
            smoothedTemperature = telemetry.GeneratorTemperature;
            brakeBlend = IsBrakeEngaged() ? 1f : 0f;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            // X-Ray is a view setting, independent of telemetry.
            UpdateXRay(deltaTime);

            if (!hasTelemetry)
                return;

            smoothedRpm = Smooth(smoothedRpm, latest.RotorRpm, rpmSmoothingTime, deltaTime);
            smoothedPitch = Smooth(smoothedPitch, latest.BladePitch, pitchSmoothingTime, deltaTime);
            smoothedTemperature = Smooth(smoothedTemperature, latest.GeneratorTemperature, temperatureSmoothingTime, deltaTime);

            UpdateRotor(deltaTime);
            UpdatePitch();
            UpdateHeatColor();
            UpdateBrake(deltaTime);
        }

        private void UpdateRotor(float deltaTime)
        {
            // 1 RPM = 360 deg / 60 s = 6 deg/s. Repeat keeps the angle small so float precision never degrades.
            float direction = invertSpinDirection ? -1f : 1f;
            spinAngle = Mathf.Repeat(spinAngle + smoothedRpm * 6f * direction * deltaTime, 360f);
            Quaternion spin = Quaternion.AngleAxis(spinAngle, Vector3.forward);

            if (rotor != null)
                rotor.localRotation = rotorInitialRotation * spin;

            // The main shaft is bolted to the hub, so it turns at exactly the rotor speed.
            if (mainShaft != null)
                mainShaft.localRotation = mainShaftInitialRotation * spin;
        }

        private bool IsBrakeEngaged() =>
            latest.State == TurbineOperatingState.StormShutdown && smoothedRpm < brakeEngageRpm;

        private void UpdateBrake(float deltaTime)
        {
            if (brakeMaterial == null)
                return;

            float target = IsBrakeEngaged() ? 1f : 0f;
            brakeBlend = Mathf.MoveTowards(brakeBlend, target, deltaTime / brakeFadeTime);

            brakeMaterial.SetColor(BaseColorId, Color.Lerp(brakeReleasedColor, brakeEngagedColor, brakeBlend));
            if (brakeMaterial.IsKeywordEnabled(EmissionKeyword))
                brakeMaterial.SetColor(EmissionColorId, brakeEngagedColor * (brakeGlowIntensity * brakeBlend));
        }

        private void UpdatePitch()
        {
            float signedPitch = invertPitchDirection ? -smoothedPitch : smoothedPitch;
            Quaternion pitchRotation = Quaternion.AngleAxis(signedPitch, Vector3.up);

            for (int i = 0; i < blades.Length; i++)
            {
                if (blades[i] != null)
                    blades[i].localRotation = bladeInitialRotations[i] * pitchRotation;
            }
        }

        private void UpdateHeatColor()
        {
            FillHeatStops(simulator.Specs);
            EvaluateHeat(smoothedTemperature, out Color color, out float glow);

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

        /// <remarks>
        /// Stops are anchored to real temperatures instead of gradient percentages, so the generator is always golden at
        /// the rated equilibrium, orange at the warning limit and red at the alarm limit, whatever the specs say.
        /// </remarks>
        private void FillHeatStops(TurbineSpecs specs)
        {
            float rated = specs.RatedGeneratorTemperature;

            heatStopTemperatures[0] = coldTemperature;
            heatStopTemperatures[1] = (coldTemperature + rated) * 0.5f;
            heatStopTemperatures[2] = rated;
            heatStopTemperatures[3] = specs.GeneratorWarningTemperature;
            heatStopTemperatures[4] = specs.GeneratorAlarmTemperature;

            // Protection limits win: if a limit is set below the rated equilibrium (or cold stop), pull the earlier
            // stops down so the stops stay in ascending order and the limit colors are still reached.
            for (int i = heatStopTemperatures.Length - 2; i >= 0; i--)
                heatStopTemperatures[i] = Mathf.Min(heatStopTemperatures[i], heatStopTemperatures[i + 1]);

            heatStopColors[0] = coldColor;
            heatStopColors[1] = warmColor;
            heatStopColors[2] = ratedColor;
            heatStopColors[3] = warningColor;
            heatStopColors[4] = alarmColor;

            heatStopGlows[0] = 0f;
            heatStopGlows[1] = 0f;
            heatStopGlows[2] = ratedGlowIntensity;
            heatStopGlows[3] = warningGlowIntensity;
            heatStopGlows[4] = maxGlowIntensity;
        }

        private void EvaluateHeat(float temperature, out Color color, out float glow)
        {
            int last = heatStopTemperatures.Length - 1;

            if (temperature <= heatStopTemperatures[0])
            {
                color = heatStopColors[0];
                glow = heatStopGlows[0];
                return;
            }

            for (int i = 1; i <= last; i++)
            {
                if (temperature < heatStopTemperatures[i])
                {
                    float t = Mathf.InverseLerp(heatStopTemperatures[i - 1], heatStopTemperatures[i], temperature);
                    color = Color.Lerp(heatStopColors[i - 1], heatStopColors[i], t);
                    glow = Mathf.Lerp(heatStopGlows[i - 1], heatStopGlows[i], t);
                    return;
                }
            }

            // At or above the alarm limit: full red with a slow pulse, like a warning beacon.
            float pulse = Mathf.Sin(Time.time * alarmPulseFrequency * 2f * Mathf.PI);
            color = heatStopColors[last];
            glow = heatStopGlows[last] * (1f + alarmPulseAmount * pulse);
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

            mainShaft = FindChildRecursive(transform, "MainShaft");
            Transform caliper = FindChildRecursive(transform, "BrakeCaliper");
            brakeCaliperRenderer = caliper != null ? caliper.GetComponent<Renderer>() : null;
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
