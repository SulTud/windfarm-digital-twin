using UnityEngine;

namespace WindFarm.Visuals
{
    /// <summary>
    /// Red aviation obstruction lights on a turbine's nacelle roof (ICAO: a pair, so at least one is visible from
    /// every direction). Built at runtime from the model's own bounds: the two rear corners of the roof, just above
    /// the cooler. <see cref="Attach"/> is also used for the distant turbines, so every light in the scene shares one
    /// material and, through the shader time, flashes in sync like a real wind farm.
    ///
    /// The lights are drawn by the WindFarm/Obstruction Light shader (camera-facing glow, no Bloom needed).
    /// <see cref="LightsOn"/> switches every light at once through a global shader value; off by default, so the
    /// red flashing is not mistaken for a fault (the dashboard toggle is for the curious).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObstructionLights : MonoBehaviour
    {
        private static readonly int LightsOnId = Shader.PropertyToID("_WindFarmObstructionLights");
        private const float RoofClearance = 0.35f;   // m above the highest roof part
        private const float CornerInset = 0.5f;      // m in from the side and rear edges
        private static Mesh quad;
        private static bool lightsOn;

        [SerializeField, Tooltip("Nacelle (housing) of this turbine. The lights go on its roof.")]
        private Transform nacelle;

        [SerializeField, Tooltip("Rotor below the nacelle. Tells which end of the nacelle is the front.")]
        private Transform rotor;

        [SerializeField, Tooltip("Material with the WindFarm/Obstruction Light shader (shared by all lights).")]
        private Material lightMaterial;

        /// <summary>Switches every obstruction light in the scene (global shader value).</summary>
        public static bool LightsOn
        {
            get => lightsOn;
            set
            {
                lightsOn = value;
                Shader.SetGlobalFloat(LightsOnId, value ? 1f : 0f);
            }
        }

        private void Awake()
        {
            if (nacelle == null || rotor == null || lightMaterial == null)
            {
                Debug.LogWarning($"{nameof(ObstructionLights)}: nacelle, rotor or light material is not assigned.", this);
                return;
            }

            LightsOn = lightsOn;   // writes the global value once, also after a domain reload
            Attach(nacelle, rotor, lightMaterial);
        }

        /// <summary>Adds the two roof lights to a nacelle (rotor = its child that marks the front).</summary>
        public static void Attach(Transform nacelle, Transform rotor, Material material)
        {
            MeshFilter housing = nacelle.GetComponent<MeshFilter>();
            if (housing == null || housing.sharedMesh == null)
                return;

            Bounds bounds = housing.sharedMesh.bounds;

            // The roof is the highest of the housing and the parts on it (the cooler), but not the rotor and blades.
            float roof = bounds.max.y;
            foreach (MeshFilter part in nacelle.GetComponentsInChildren<MeshFilter>())
            {
                if (part == housing || part.sharedMesh == null || part.transform.IsChildOf(rotor))
                    continue;

                Bounds partBounds = part.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = new Vector3(
                        (corner & 1) == 0 ? partBounds.min.x : partBounds.max.x,
                        (corner & 2) == 0 ? partBounds.min.y : partBounds.max.y,
                        (corner & 4) == 0 ? partBounds.min.z : partBounds.max.z);
                    roof = Mathf.Max(roof, nacelle.InverseTransformPoint(part.transform.TransformPoint(local)).y);
                }
            }

            // The model's root is scaled (the FBX parts are in 1/100 m with a x100 root), so meters are converted to
            // the nacelle's local units. The first version used meters directly: the lights flew 50 m out.
            float metersToLocal = 1f / Mathf.Max(Mathf.Abs(nacelle.lossyScale.y), 1e-6f);
            float inset = CornerInset * metersToLocal;

            // Rear = the end of the housing away from the rotor.
            float rotorZ = nacelle.InverseTransformPoint(rotor.position).z;
            float rear = rotorZ < bounds.center.z ? bounds.max.z - inset : bounds.min.z + inset;
            float side = Mathf.Max(bounds.extents.x - inset, 0f);
            float height = roof + RoofClearance * metersToLocal;

            CreateLight(nacelle, material, new Vector3(bounds.center.x - side, height, rear), "ObstructionLight_L");
            CreateLight(nacelle, material, new Vector3(bounds.center.x + side, height, rear), "ObstructionLight_R");
        }

        private static void CreateLight(Transform parent, Material material, Vector3 localPosition, string name)
        {
            var light = new GameObject(name);
            light.transform.SetParent(parent, false);
            light.transform.localPosition = localPosition;
            light.transform.localScale = Vector3.one;   // the glow size is set in world meters by the shader

            light.AddComponent<MeshFilter>().sharedMesh = Quad;
            var renderer = light.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// Quad with all four vertices at the pivot and the corners in the UV; the shader expands it into a
        /// camera-facing glow. Keeps working when Unity batches it into world space. Generous bounds for culling.
        /// </summary>
        private static Mesh Quad
        {
            get
            {
                if (quad != null)
                    return quad;

                quad = new Mesh { name = "ObstructionLightQuad" };
                quad.SetVertices(new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero });
                quad.SetUVs(0, new[]
                {
                    new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
                    new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                });
                quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
                quad.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
                return quad;
            }
        }

#if UNITY_EDITOR
        /// <summary>Auto-wires the parts by the model's object names.</summary>
        private void Reset()
        {
            nacelle = FindChildRecursive(transform, "Nacelle");
            rotor = FindChildRecursive(transform, "Rotor");
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
