using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WindFarm.Cameras;

namespace WindFarm.Visuals
{
    /// <summary>
    /// One place for the look of the scene around the turbine, in the dashboard palette (dark dusk blue):
    ///   Sky:     gradient skybox material (zenith -> horizon + a soft glow band), no textures.
    ///   Ground:  matte plane color; it receives the turbine's shadow, which grounds the turbine.
    ///   Fog:     linear, in the sky's exact color at the horizon (horizon + glow), starting beyond the turbine and
    ///            ending before the ground's edge, so the ground melts into the sky. Follows the orbit camera distance: the turbine is never fogged.
    ///   Sun:     low directional light from the front side (shape and a long shadow on the white turbine).
    ///   Ambient: three colors (sky / horizon / ground) from the same palette.
    ///   Shadows: the pipeline's shadow distance follows the camera (the default 50 m drew no shadow at all at
    ///            200-600 m); close up the shadow map covers a small area and gets sharp.
    ///
    /// Runs in Edit mode too, so the scene shows and saves the look (fog must be on in the saved scene: builds strip
    /// the fog shader variants of scenes without fog). After changing the sky colors, press Generate Lighting once
    /// (Window -> Rendering -> Lighting): the reflections on the turbine are baked from the sky.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SceneEnvironment : MonoBehaviour
    {
        private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        private static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
        private static readonly int GradientHeightId = Shader.PropertyToID("_GradientHeight");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowWidthId = Shader.PropertyToID("_GlowWidth");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private const float ShadowDistanceStep = 50f;   // m; quantized so zooming does not re-fit the shadow map every frame

        [Header("References")]
        [SerializeField, Tooltip("Material with the WindFarm/Skybox Gradient shader.")]
        private Material skyMaterial;

        [SerializeField, Tooltip("Ground plane. Its material color is set from Ground Color.")]
        private Renderer ground;

        [SerializeField, Tooltip("Directional light used as the sun.")]
        private Light sun;

        [SerializeField, Tooltip("Orbit camera; fog and shadow distance follow its distance. Optional.")]
        private TurbineOrbitCamera orbitCamera;

        [Header("Sky")]
        [SerializeField] private Color zenithColor = new Color(0.106f, 0.204f, 0.275f);    // #1B3446
        [SerializeField] private Color horizonColor = new Color(0.235f, 0.353f, 0.420f);  // #3C5A6B
        [SerializeField, Range(0.05f, 1f), Tooltip("How high above the horizon the zenith color takes over (sine of " +
            "the angle; 0.35 ~ 20 deg reaches 63 %).")]
        private float gradientHeight = 0.35f;
        [SerializeField] private Color horizonGlowColor = new Color(0.361f, 0.471f, 0.522f); // #5C7885
        [SerializeField, Range(0.005f, 0.5f)] private float horizonGlowWidth = 0.08f;
        [SerializeField, Range(0f, 1f)] private float horizonGlowStrength = 0.5f;

        [Header("Ground")]
        [SerializeField, Tooltip("Ground albedo. Lit by the sun and the ambient, it reads lighter on screen.")]
        private Color groundColor = new Color(0.102f, 0.157f, 0.188f);                     // #1A2830

        [Header("Fog")]
        [SerializeField, Min(0f), Tooltip("Fog starts this far beyond the camera's distance to the turbine (m).")]
        private float fogStartBeyondTurbine = 150f;

        [SerializeField, Min(1f), Tooltip("Distance over which the fog goes from clear to full (m). Keep the ground " +
            "plane larger than camera distance + start + depth, or its edge shows.")]
        private float fogDepth = 1200f;

        [SerializeField, Min(0f), Tooltip("Camera distance assumed in Edit mode (m).")]
        private float editModeCameraDistance = 300f;

        [Header("Sun")]
        [SerializeField, Range(5f, 90f), Tooltip("Sun height above the horizon (deg). Low = long shadows, more shape.")]
        private float sunElevation = 35f;

        [SerializeField, Range(-180f, 180f), Tooltip("Sun direction around the vertical axis (deg, world).")]
        private float sunAzimuth = -40f;

        [SerializeField] private Color sunColor = new Color(0.949f, 0.925f, 0.886f);       // #F2ECE2, slightly warm
        [SerializeField, Min(0f)] private float sunIntensity = 1.6f;
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.75f;

        [SerializeField, Min(0f), Tooltip("Shadow distance beyond the camera's distance to the turbine (m): the turbine " +
            "plus its shadow on the ground.")]
        private float shadowReach = 350f;

        [Header("Ambient")]
        [SerializeField] private Color ambientSkyColor = new Color(0.235f, 0.353f, 0.420f);     // #3C5A6B
        [SerializeField] private Color ambientEquatorColor = new Color(0.180f, 0.271f, 0.314f); // #2E4550
        [SerializeField] private Color ambientGroundColor = new Color(0.102f, 0.157f, 0.188f);  // #1A2830

        private UniversalRenderPipelineAsset pipeline;
        private float originalShadowDistance;

        private void OnEnable()
        {
            Apply();

            // Only in Play mode: the pipeline asset is shared, and an Edit mode change would stay in the asset.
            if (Application.isPlaying)
            {
                pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (pipeline != null)
                    originalShadowDistance = pipeline.shadowDistance;
            }
        }

        private void OnDisable()
        {
            if (pipeline != null)
            {
                pipeline.shadowDistance = originalShadowDistance;
                pipeline = null;
            }
        }

        private void OnValidate() => Apply();

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            float distance = CameraDistance();
            ApplyFogDistance(distance);

            if (pipeline != null)
            {
                float shadowDistance = Mathf.Ceil((distance + shadowReach) / ShadowDistanceStep) * ShadowDistanceStep;
                if (!Mathf.Approximately(pipeline.shadowDistance, shadowDistance))
                    pipeline.shadowDistance = shadowDistance;
            }
        }

        private float CameraDistance() =>
            Application.isPlaying && orbitCamera != null && orbitCamera.Distance > 0f
                ? orbitCamera.Distance
                : editModeCameraDistance;

        private void Apply()
        {
            if (skyMaterial != null)
            {
                skyMaterial.SetColor(TopColorId, zenithColor);
                skyMaterial.SetColor(HorizonColorId, horizonColor);
                skyMaterial.SetFloat(GradientHeightId, gradientHeight);
                skyMaterial.SetColor(GlowColorId, horizonGlowColor);
                skyMaterial.SetFloat(GlowWidthId, horizonGlowWidth);
                skyMaterial.SetFloat(GlowStrengthId, horizonGlowStrength);
                RenderSettings.skybox = skyMaterial;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyColorAtHorizon();
            ApplyFogDistance(CameraDistance());

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSkyColor;
            RenderSettings.ambientEquatorColor = ambientEquatorColor;
            RenderSettings.ambientGroundColor = ambientGroundColor;

            if (ground != null)
            {
                if (ground.sharedMaterial != null)
                    ground.sharedMaterial.SetColor(BaseColorId, groundColor);
                ground.shadowCastingMode = ShadowCastingMode.Off;
                ground.receiveShadows = true;
            }

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
                sun.color = sunColor;
                sun.intensity = sunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = shadowStrength;
                RenderSettings.sun = sun;
            }
        }

        /// <summary>
        /// The sky's exact color at the horizon (horizon color with the full glow), used as the fog color: the fully
        /// fogged far ground then meets the sky with no step. Blended in linear space like the shader does.
        /// </summary>
        private Color SkyColorAtHorizon() =>
            Color.Lerp(horizonColor.linear, horizonGlowColor.linear, horizonGlowStrength).gamma;

        private void ApplyFogDistance(float cameraDistance)
        {
            float start = cameraDistance + fogStartBeyondTurbine;
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = start + fogDepth;
        }

#if UNITY_EDITOR
        /// <summary>Auto-wires the sun, the camera, an object named "Ground" and the M_SkyGradient material.</summary>
        private void Reset()
        {
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    sun = light;
                    break;
                }
            }

            orbitCamera = FindAnyObjectByType<TurbineOrbitCamera>();

            GameObject groundObject = GameObject.Find("Ground");
            ground = groundObject != null ? groundObject.GetComponent<Renderer>() : null;

            string[] guids = UnityEditor.AssetDatabase.FindAssets("M_SkyGradient t:Material");
            if (guids.Length > 0)
                skyMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }
#endif
    }
}
