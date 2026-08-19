using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using RallyGame.Core;

namespace RallyGame.World.Roads
{
    /// One road. Draw a spline, pick a surface, press Rebuild.
    ///
    /// The generated meshes live as children named RoadMesh_*, are rebuilt from
    /// scratch every time, and are never hand-edited — the spline and this component
    /// are the only source of truth. That is what makes a hundred roads maintainable:
    /// change the width on the surface asset, re-bake, done.
    ///
    /// The sampled centreline is kept (and serialised) so RoadNetwork can answer
    /// "where is the nearest road" for respawns without touching the spline maths.
    [RequireComponent(typeof(SplineContainer))]
    public class RoadSpline : MonoBehaviour
    {
        [Header("Surface")]
        [SerializeField] private RoadSurface surface;
        [Tooltip("Registers this road for nearest-road queries (respawn, minimap, AI later).")]
        [SerializeField] private RoadNetwork network;

        [Header("Overrides")]
        [Tooltip("Off = use the width and skirt from the surface asset. On = the values below win.")]
        [SerializeField] private bool overrideShape;
        [SerializeField] private float width = 7f;
        [SerializeField] private float shoulderWidth = 0.6f;
        [SerializeField] private float shoulderDrop = 0.12f;
        [SerializeField] private float heightOffset = 0.06f;

        [Header("Build")]
        [Tooltip("Distance between cross-sections. Lower = smoother curves, more triangles. " +
                 "2 m is fine for open country, 1 m for hairpins.")]
        [SerializeField] private float metresPerSample = 2f;
        [Tooltip("Drop the road onto whatever is underneath. Off = the road floats exactly on the spline.")]
        [SerializeField] private bool conformToGround = true;
        [Tooltip("Layers counted as ground. Must include the terrain layer.")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("How far above and below each spline point to look for ground.")]
        [SerializeField] private float probeUp = 50f;
        [SerializeField] private float probeDown = 200f;
        [Tooltip("Cross-sections per mesh chunk. Smaller = better culling, more draw calls.")]
        [SerializeField] private int ringsPerChunk = 120;
        [SerializeField] private bool generateCollider = true;

        [Header("Baked")]
        [Tooltip("Filled by Rebuild. Read-only — this is what respawn queries walk.")]
        [SerializeField] private List<RoadSample> centreline = new List<RoadSample>();
        [SerializeField] private Bounds bakedBounds;

        private const string ChildPrefix = "RoadMesh_";

        public RoadSurface Surface => surface;
        public IReadOnlyList<RoadSample> Centreline => centreline;
        public Bounds BakedBounds => bakedBounds;
        public float Width => overrideShape ? width : (surface ? surface.defaultWidth : 7f);
        public bool HasBake => centreline.Count >= 2;

        private void OnEnable()
        {
            if (network) network.Register(this);
            if (!HasBake)
                GameLog.Warn(LogCat.World,
                    $"Road '{name}' has no baked centreline. Press Rebuild Road on the component — " +
                    "until then it has no mesh and respawn cannot use it.", this);
        }

        private void OnDisable() { if (network) network.Unregister(this); }

        // ---- build ---------------------------------------------------------

        [ContextMenu("Rebuild Road")]
        public void Rebuild()
        {
            var container = GetComponent<SplineContainer>();
            if (container == null || container.Splines.Count == 0)
            {
                GameLog.Error(LogCat.World, $"Road '{name}' has no spline to build from.", this);
                return;
            }
            if (surface == null)
            {
                GameLog.Error(LogCat.World, $"Road '{name}' has no RoadSurface assigned — nothing to texture it with.", this);
                return;
            }

            var settings = Settings();
            ClearChildren();
            centreline.Clear();

            int chunks = 0, missed = 0;

            for (int i = 0; i < container.Splines.Count; i++)
            {
                var samples = RoadMeshBuilder.Sample(container, i, settings, out int miss);
                missed += miss;
                if (samples.Count < 2) continue;

                if (i == 0) centreline.AddRange(samples);   // strand 0 is the one queries use

                var meshes = RoadMeshBuilder.Build(samples, container.Splines[i].Closed, settings, $"{name}_S{i}");
                foreach (var mesh in meshes) CreateChunk(mesh, chunks++);
            }

            bakedBounds = WorldBounds();
            RoadSurfaceTag.ClearCache();

            GameLog.Action(LogCat.World, "Road rebuilt",
                           $"'{name}' on {surface.displayName}: {chunks} chunk(s), " +
                           $"{centreline.Count} cross-section(s), {Width:0.0} m wide", this);

            if (missed > 0)
                GameLog.Warn(LogCat.World,
                    $"Road '{name}': {missed} sample(s) found no ground below them. The road floats there. " +
                    "Check that the terrain's layer is in this road's Ground Mask.", this);

#if UNITY_EDITOR
            SaveMeshAssets();
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [ContextMenu("Clear Road")]
        public void Clear()
        {
            ClearChildren();
            centreline.Clear();
            GameLog.Action(LogCat.World, "Road cleared", $"'{name}'", this);
        }

        private RoadBuildSettings Settings() => new RoadBuildSettings
        {
            width = Width,
            shoulderWidth = overrideShape ? shoulderWidth : surface.shoulderWidth,
            shoulderDrop = overrideShape ? shoulderDrop : surface.shoulderDrop,
            heightOffset = overrideShape ? heightOffset : surface.heightOffset,
            metresPerSample = metresPerSample,
            uvTilesPerMetre = surface.uvTilesPerMetre,
            conformToGround = conformToGround,
            groundMask = groundMask,
            probeUp = probeUp,
            probeDown = probeDown,
            maxRingsPerChunk = ringsPerChunk
        };

        private GameObject CreateChunk(Mesh mesh, int index)
        {
            var go = new GameObject($"{ChildPrefix}{index:00}");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            go.isStatic = true;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = surface.material;
            go.AddComponent<RoadSurfaceTag>().SetSurface(surface);

            if (generateCollider)
            {
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;
                if (surface.physicsMaterial) col.sharedMaterial = surface.physicsMaterial;
            }

            return go;
        }

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(ChildPrefix)) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        // Chunk meshes are built in world space and parented at identity, so renderer
        // bounds and mesh bounds agree. Nothing here needs a transform.
        private Bounds WorldBounds()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            b.Expand(Width);                     // pad so nearest-road queries do not miss at the edge
            return b;
        }

        // ---- editor --------------------------------------------------------
#if UNITY_EDITOR
        /// Generated meshes must become real assets, or they vanish on the next domain
        /// reload and every road turns invisible. Same reasoning as StageBaker.
        private void SaveMeshAssets()
        {
            const string root = "Assets/_Project/Generated";
            const string folder = root + "/Roads";

            if (!UnityEditor.AssetDatabase.IsValidFolder(root))
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project", "Generated");
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                UnityEditor.AssetDatabase.CreateFolder(root, "Roads");

            foreach (var filter in GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || UnityEditor.AssetDatabase.Contains(mesh)) continue;

                string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{folder}/{mesh.name}.asset");
                UnityEditor.AssetDatabase.CreateAsset(mesh, path);
            }
            UnityEditor.AssetDatabase.SaveAssets();
        }

        private void OnDrawGizmosSelected()
        {
            if (centreline.Count < 2) return;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            for (int i = 1; i < centreline.Count; i++)
                Gizmos.DrawLine(centreline[i - 1].position, centreline[i].position);
        }
#endif
    }
}