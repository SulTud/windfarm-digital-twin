using UnityEngine;

namespace WindFarm.Visuals
{
    /// <summary>
    /// One or two distant turbines on the horizon behind the simulated one, purely for atmosphere: flat dark
    /// silhouettes (WindFarm/Silhouette, no lighting, no lights on the roof), each turning at its own steady speed.
    ///
    /// Deliberately minimal (user decision): a first version with six lit, telemetry-driven turbines in two loose rows
    /// read as clutter, and the far ones dissolved in the fog until only their lights showed. These are not part of
    /// the simulation, so they do not follow the telemetry. Cheap: no shadows, one material, one loop for the rotors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WindFarmBackdrop : MonoBehaviour
    {
        [SerializeField, Tooltip("Turbine model asset to place (Art/Models/WTG.fbx).")]
        private GameObject turbineModel;

        [SerializeField, Tooltip("The simulated turbine (WTG_01_Model): position, facing and the front.")]
        private Transform mainTurbine;

        [SerializeField, Tooltip("The simulated turbine's rotor. Its forward axis points upwind, toward the viewer's home view.")]
        private Transform mainRotor;

        [SerializeField, Tooltip("Material with the WindFarm/Silhouette shader; replaces every material of the copies.")]
        private Material silhouetteMaterial;

        [SerializeField, Range(1, 3), Tooltip("Number of silhouettes.")]
        private int count = 2;

        [SerializeField, Min(100f), Tooltip("Mean distance behind the main turbine, downwind (m). Keep the farthest one " +
                                            "inside the camera's far clip (camera distance + its Far Clip Margin), also " +
                                            "for the close-up views.")]
        private float distance = 2200f;

        [SerializeField, Min(0f), Tooltip("Mean spacing between the silhouettes across the wind (m).")]
        private float spacing = 1300f;

        // A straight, evenly spaced row at one distance looked artificial (second test): real turbines on the horizon
        // never line up.
        [SerializeField, Range(0f, 0.5f), Tooltip("Random scatter of each silhouette's distance and side position, as a " +
                                                  "fraction of the distance / spacing. Fixed by the seed.")]
        private float scatter = 0.3f;

        [SerializeField, Tooltip("Shifts them sideways (m).")]
        private float sideOffset;

        [SerializeField, Min(0f), Tooltip("Rotor speed (rpm). Not linked to the simulation.")]
        private float rotorRpm = 11f;

        [SerializeField, Range(0f, 0.3f), Tooltip("Rotor speed difference between the silhouettes (fraction).")]
        private float speedVariation = 0.12f;

        [SerializeField, Tooltip("Seed for the start angles and speed differences (same picture on every run).")]
        private int seed = 17;

        private static readonly int GroundHeightId = Shader.PropertyToID("_GroundHeight");

        private Material material;
        private Transform[] rotors = new Transform[0];
        private Quaternion[] rotorStartRotations = new Quaternion[0];
        private float[] angles = new float[0];
        private float[] speedFactors = new float[0];

        private void Start()
        {
            if (turbineModel == null || mainTurbine == null || mainRotor == null || silhouetteMaterial == null)
            {
                Debug.LogWarning($"{nameof(WindFarmBackdrop)}: model, main turbine, main rotor or silhouette material is not assigned.", this);
                enabled = false;
                return;
            }

            // One instance for all copies, so setting the ground height does not write into the material asset.
            material = new Material(silhouetteMaterial) { name = silhouetteMaterial.name + " (Instance)" };
            material.SetFloat(GroundHeightId, mainTurbine.position.y);

            // "Behind" = downwind = away from the rotor's front.
            Vector3 front = Vector3.ProjectOnPlane(mainRotor.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, front);
            var random = new System.Random(seed);

            rotors = new Transform[count];
            rotorStartRotations = new Quaternion[count];
            angles = new float[count];
            speedFactors = new float[count];

            for (int i = 0; i < count; i++)
            {
                float jitterAcross = ((float)random.NextDouble() * 2f - 1f) * scatter * spacing;
                float jitterDistance = ((float)random.NextDouble() * 2f - 1f) * scatter * distance;
                float across = (i - (count - 1) * 0.5f) * spacing + sideOffset + jitterAcross;
                Vector3 position = mainTurbine.position + right * across - front * (distance + jitterDistance);
                GameObject turbine = Instantiate(turbineModel, position, mainTurbine.rotation, transform);
                turbine.name = $"Silhouette_WTG_{i + 1}";
                turbine.transform.localScale = mainTurbine.lossyScale;

                foreach (Renderer part in turbine.GetComponentsInChildren<Renderer>())
                {
                    var materials = new Material[part.sharedMaterials.Length];
                    for (int m = 0; m < materials.Length; m++)
                        materials[m] = material;
                    part.sharedMaterials = materials;
                    part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    part.receiveShadows = false;
                    part.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    part.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }

                Transform rotor = FindChildRecursive(turbine.transform, "Rotor");
                rotors[i] = rotor;
                rotorStartRotations[i] = rotor != null ? rotor.localRotation : Quaternion.identity;
                angles[i] = (float)random.NextDouble() * 360f;
                speedFactors[i] = 1f + ((float)random.NextDouble() * 2f - 1f) * speedVariation;
            }
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null)
                    continue;

                // Same direction as the main rotor: 1 rpm = 6 deg/s.
                angles[i] = Mathf.Repeat(angles[i] + rotorRpm * speedFactors[i] * 6f * deltaTime, 360f);
                rotors[i].localRotation = rotorStartRotations[i] * Quaternion.AngleAxis(angles[i], Vector3.forward);
            }
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

#if UNITY_EDITOR
        private void Reset()
        {
            var visuals = FindAnyObjectByType<TurbineVisualController>();
            if (visuals != null)
            {
                mainTurbine = visuals.transform;
                mainRotor = visuals.Rotor;
            }
        }
#endif
    }
}
