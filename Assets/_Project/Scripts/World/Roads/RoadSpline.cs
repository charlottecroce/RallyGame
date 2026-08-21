using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using RallyGame.Core;

namespace RallyGame.World.Roads
{
    /// One road network. Draw splines, pick a surface, press Rebuild.
    ///
    /// The generated meshes and props live as children named RoadMesh_* / RoadProps_*,
    /// are rebuilt from scratch every time, and are never hand-edited — the splines and
    /// this component are the only source of truth. That is what makes a hundred roads
    /// maintainable: change the width on the surface asset, re-bake, done.
    ///
    /// A SplineContainer can hold many strands, and this component bakes all of them,
    /// which is why junction finding and road clearing live here: both are properties
    /// of the network, not of any one spline.
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

        [Header("Ground fit")]
        [Tooltip("Probes across the road per cross-section. One probe only reads the centreline, " +
                 "so an edge can sink on a cross-slope. Forced odd. 5 is plenty.")]
        [Range(1, 9)][SerializeField] private int crossProbes = 5;
        [Tooltip("Also probe halfway between cross-sections. This is what stops a crest between " +
                 "two samples cutting through the flat span of mesh joining them.")]
        [SerializeField] private bool probeMidRings = true;
        [Tooltip("Vertical smoothing iterations. Higher = a road that ignores small terrain noise " +
                 "instead of tracking every bump. Clearance is re-applied after every pass, so this " +
                 "never sinks the road.")]
        [Range(0, 12)][SerializeField] private int smoothingPasses = 4;
        [Range(0f, 1f)][SerializeField] private float smoothingStrength = 0.5f;
        [Tooltip("How much the road banks with the hillside. 1 = follow the cross-slope exactly, " +
                 "0 = never bank. Has no effect unless Max Camber Degrees below is greater than 0.")]
        [Range(0f, 1f)][SerializeField] private float bankBlend = 1f;

        [Header("Camber & Skirt")]
        [Tooltip("Hard ceiling on how far the road rolls to follow a cross-slope, in degrees. Past " +
                 "this the road stops tilting and stands proud of the downhill side — that's exactly " +
                 "when the skirt below has to do the most work. 0 disables banking entirely.")]
        [Range(0f, 45f)][SerializeField] private float maxCamberDegrees = 12f;
        [Tooltip("How far under the terrain the skirt's ground-contact vertex is buried, so a sliver " +
                 "of mesh never peeks out on the far side of a bump.")]
        [SerializeField] private float skirtBurial = 0.15f;
        [Tooltip("Vertical wall height before the skirt stops digging straight down and battens " +
                 "outward instead. Keeps a steep hillside from turning into a towering wall rather " +
                 "than a proper embankment.")]
        [SerializeField] private float skirtMaxDrop = 2f;
        [Tooltip("Horizontal metres the skirt ramps outward for every extra metre it still needs to " +
                 "drop once Skirt Max Drop is hit — like ballast under a rail bed. Bigger = a gentler, " +
                 "wider batter. 0 falls back to a plain capped wall, which CAN leave a gap on a steep " +
                 "camber — leave this above 0 unless you have a reason not to.")]
        [SerializeField] private float skirtBatterSlope = 1.5f;
        [Tooltip("Skirt material's texture repeats across its own width (wall + ramp), independent " +
                 "of how the road surface tiles along its length.")]
        [SerializeField] private float skirtUvTilesAcross = 0.5f;

        [Header("Junctions")]
        [Tooltip("Two strands whose centrelines come this close (metres, horizontal) are meeting.")]
        [SerializeField] private float junctionJoinDistance = 6f;
        [Tooltip("Vertical gap above which a crossing is an overpass, not a junction.")]
        [SerializeField] private float junctionHeightTolerance = 3f;
        [Tooltip("Touch points within this distance collapse into one junction.")]
        [SerializeField] private float junctionMergeRadius = 12f;
        [Tooltip("Working radius of a junction: cones go inside it, bollards stay out.")]
        [SerializeField] private float junctionRadius = 10f;

        [Header("Bollards")]
        [Tooltip("Any prefab. Placed on the shoulder, away from junctions. Its own root rotation " +
                 "and scale are kept — the road only adds the yaw needed to line it up.")]
        [SerializeField] private GameObject bollardPrefab;
        [SerializeField] private float bollardInterval = 20f;
        [SerializeField] private RoadSide bollardSides = RoadSide.Both;
        [Tooltip("Metres outboard of the road edge. Keep it under the shoulder width to stay on the skirt.")]
        [SerializeField] private float bollardEdgeOffset = 0.35f;
        [SerializeField] private float bollardHeightOffset;
        [SerializeField] private float bollardStartOffset;
        [SerializeField] private PropFacing bollardFacing = PropFacing.AlongRoad;
        [Tooltip("Extra rotation in the prop's own frame, on top of the prefab's. For a model whose " +
                 "front is not +Z.")]
        [SerializeField] private Vector3 bollardRotationOffset;
        [Range(0f, 30f)][SerializeField] private float bollardYawJitter = 3f;
        [Tooltip("Extra keep-out beyond the junction radius, so the last bollard is clear of the cones.")]
        [SerializeField] private float bollardJunctionMargin = 6f;

        [Header("Junction cones")]
        [Tooltip("Any prefab. Placed on the shoulder, only within a junction radius.")]
        [SerializeField] private GameObject conePrefab;
        [SerializeField] private float coneInterval = 4f;
        [SerializeField] private RoadSide coneSides = RoadSide.Both;
        [SerializeField] private float coneEdgeOffset = 0.35f;
        [SerializeField] private float coneHeightOffset;
        [SerializeField] private PropFacing coneFacing = PropFacing.AlongRoad;
        [SerializeField] private Vector3 coneRotationOffset;
        [Range(0f, 30f)][SerializeField] private float coneYawJitter = 8f;

        [Header("Trash")]
        [Tooltip("Roadside litter prefabs. One is picked at random for each spawn point, so mix " +
                 "a few different pieces in here for variety.")]
        [SerializeField] private GameObject[] trashPrefabs;
        [Tooltip("Baseline average pieces per metre of road, away from junctions. Try something " +
                 "small like 0.03-0.08 — this is a scatter, not a carpet.")]
        [SerializeField] private float trashDensityPerMetre = 0.05f;
        [Tooltip("Closest a piece of trash can land from the road edge, metres. Can be 0 or even " +
                 "slightly negative to let a few pieces sit right on the shoulder.")]
        [SerializeField] private float trashMinOffset = 0.2f;
        [Tooltip("Furthest a piece of trash can land from the road edge, metres. Each piece rolls " +
                 "an independent random distance between Min and Max, which is what gives the " +
                 "'some right alongside, some further out' spread instead of a neat line.")]
        [SerializeField] private float trashMaxOffset = 5f;
        [SerializeField] private RoadSide trashSides = RoadSide.Both;
        [Tooltip("Density is multiplied by this within range of a junction, so intersections read " +
                 "as noticeably messier than open road.")]
        [SerializeField] private float trashJunctionDensityMultiplier = 4f;
        [Tooltip("Extra radius beyond a junction's own radius where the density boost applies.")]
        [SerializeField] private float trashJunctionBoostRadius = 8f;
        [Tooltip("Different seeds give different scatters without touching density or offsets.")]
        [SerializeField] private int trashSeed = 54321;
        [SerializeField] private int maxTrash = 3000;

        [Header("Props (shared)")]
        [Tooltip("Raycast each prop onto the terrain instead of leaving it on the road plane.")]
        [SerializeField] private bool propsSnapToGround = true;
        [Tooltip("Tilt props with the ground. Off = they stay vertical, which usually looks better.")]
        [SerializeField] private bool propsAlignToGround;
        [Tooltip("Safety net against a 0.5 m interval on a 20 km network.")]
        [SerializeField] private int maxProps = 4000;
        [SerializeField] private int propSeed = 12345;

        [Header("Clear items from road")]
        [Tooltip("Run the clear pass at the end of every rebuild. This is what removes the cones " +
                 "that land on the tarmac where two splines overlap at a junction.")]
        [SerializeField] private bool clearAfterRebuild = true;
        [Tooltip("Pulled in from each road edge, so the shoulder — where props belong — survives. " +
                 "Raise it if bollards on the verge are being eaten.")]
        [SerializeField] private float clearEdgeInset = 0.2f;
        [Tooltip("Height band above the road surface that counts as 'on the road'. Anything higher " +
                 "is treated as an overpass and left alone.")]
        [SerializeField] private float clearAbove = 8f;
        [SerializeField] private float clearBelow = 2f;
        [Tooltip("Layers scanned for scene objects to delete — trees, rocks, fences. Leave empty to " +
                 "clear only this road's own props.")]
        [SerializeField] private LayerMask clutterMask = 0;
        [Tooltip("Also delete Unity terrain trees standing on the road. This edits the TerrainData " +
                 "asset, so it is permanent (undoable, but it dirties the terrain).")]
        [SerializeField] private bool removeTerrainTrees;
        [Tooltip("Extra clearance for terrain trees — their pivot is the trunk centre but the trunk " +
                 "has width.")]
        [SerializeField] private float terrainTreeMargin = 0.5f;

        [Header("Baked")]
        [Tooltip("Filled by Rebuild. Read-only — this is what respawn queries walk.")]
        [SerializeField] private List<RoadSample> centreline = new List<RoadSample>();
        [SerializeField] private List<RoadJunction> junctions = new List<RoadJunction>();
        [SerializeField] private Bounds bakedBounds;

        private const string MeshPrefix = "RoadMesh_";
        private const string SkirtPrefix = "RoadSkirt_";
        private const string PropPrefix = "RoadProps_";

        public RoadSurface Surface => surface;
        public IReadOnlyList<RoadSample> Centreline => centreline;
        public IReadOnlyList<RoadJunction> Junctions => junctions;
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
            if (!Ready(out var container)) return;

            var settings = Settings();
            ClearMeshes();
            ClearProps();
            centreline.Clear();
            junctions.Clear();

            var strands = new List<List<RoadSample>>(container.Splines.Count);
            int chunks = 0, missed = 0;

            for (int i = 0; i < container.Splines.Count; i++)
            {
                var samples = RoadMeshBuilder.Sample(container, i, settings, out int miss);
                missed += miss;
                strands.Add(samples);
                if (samples.Count < 2) continue;

                if (i == 0) centreline.AddRange(samples);   // strand 0 is the one queries use

                var meshChunks = RoadMeshBuilder.Build(samples, container.Splines[i].Closed, settings, $"{name}_S{i}");
                foreach (var meshChunk in meshChunks) CreateChunk(meshChunk, chunks++);
            }

            junctions.AddRange(RoadJunctions.Find(strands, JunctionSettings()));
            PlaceProps(container, strands);
            if (clearAfterRebuild) RemoveItems(strands);

            bakedBounds = WorldBounds();
            RoadSurfaceTag.ClearCache();

            GameLog.Action(LogCat.World, "Road rebuilt",
                           $"'{name}' on {surface.displayName}: {chunks} chunk(s), " +
                           $"{centreline.Count} cross-section(s), {junctions.Count} junction(s), " +
                           $"{Width:0.0} m wide", this);

            if (missed > 0)
                GameLog.Warn(LogCat.World,
                    $"Road '{name}': {missed} sample(s) found no ground below them. The road floats there. " +
                    "Check that the terrain's layer is in this road's Ground Mask.", this);

#if UNITY_EDITOR
            SaveMeshAssets();
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// Props and junctions only. Re-samples the splines (cheap next to mesh
        /// building) so junction positions stay honest, and leaves the meshes alone —
        /// this is the button for tuning intervals and rotations without a full bake.
        [ContextMenu("Rebuild Props")]
        public void RebuildProps()
        {
            if (!Sample(out var container, out var strands)) return;

            ClearProps();
            junctions.Clear();
            junctions.AddRange(RoadJunctions.Find(strands, JunctionSettings()));

            PlaceProps(container, strands);
            if (clearAfterRebuild) RemoveItems(strands);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// Standalone clear pass. Re-samples first, so it is honest even if the splines
        /// moved since the last bake.
        [ContextMenu("Remove Items From Road")]
        public void RemoveItemsFromRoad()
        {
            if (!Sample(out _, out var strands)) return;
            RemoveItems(strands);
        }

        [ContextMenu("Clear Road")]
        public void Clear()
        {
            ClearMeshes();
            ClearProps();
            centreline.Clear();
            junctions.Clear();
            GameLog.Action(LogCat.World, "Road cleared", $"'{name}'", this);
        }

        private bool Ready(out SplineContainer container)
        {
            container = GetComponent<SplineContainer>();
            if (container == null || container.Splines.Count == 0)
            {
                GameLog.Error(LogCat.World, $"Road '{name}' has no spline to build from.", this);
                return false;
            }
            if (surface == null)
            {
                GameLog.Error(LogCat.World, $"Road '{name}' has no RoadSurface assigned — nothing to texture it with.", this);
                return false;
            }
            return true;
        }

        private bool Sample(out SplineContainer container, out List<List<RoadSample>> strands)
        {
            strands = null;
            if (!Ready(out container)) return false;

            var settings = Settings();
            strands = new List<List<RoadSample>>(container.Splines.Count);
            for (int i = 0; i < container.Splines.Count; i++)
                strands.Add(RoadMeshBuilder.Sample(container, i, settings, out _));

            return true;
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
            maxRingsPerChunk = ringsPerChunk,
            crossProbes = crossProbes,
            probeMidRings = probeMidRings,
            smoothingPasses = smoothingPasses,
            smoothingStrength = smoothingStrength,
            bankBlend = bankBlend,
            // These four were previously never copied across, which silently zeroed
            // them out (C# struct default): camber was permanently clamped to 0
            // regardless of Bank Blend, and the skirt's adaptive drop collapsed to a
            // flat constant equal to Shoulder Drop no matter how far the real ground
            // was — that mismatch is what left gaps under the road on anything but a
            // dead-flat cross-slope.
            maxCamberDegrees = maxCamberDegrees,
            skirtBurial = skirtBurial,
            skirtMaxDrop = skirtMaxDrop,
            skirtBatterSlope = skirtBatterSlope,
            skirtUvTilesAcross = skirtUvTilesAcross
        };

        private RoadJunctionSettings JunctionSettings() => new RoadJunctionSettings
        {
            joinDistance = junctionJoinDistance,
            maxHeightDelta = junctionHeightTolerance,
            mergeRadius = junctionMergeRadius,
            radius = junctionRadius,
            // Two samples must be this far apart along one strand before their
            // closeness counts as a crossing rather than just being neighbours.
            minSampleGap = Mathf.Max(4, Mathf.CeilToInt(junctionJoinDistance * 3f / Mathf.Max(0.25f, metresPerSample)))
        };

        private RoadPropSettings PropSettings(float interval, float edgeOffset, float lift, float startOffset,
                                              RoadSide sides, PropFacing facing, Vector3 rotationOffset,
                                              float jitter, int budget)
            => new RoadPropSettings
            {
                roadHalfWidth = Width * 0.5f,
                lateralOffset = edgeOffset,
                verticalOffset = lift,
                interval = interval,
                startOffset = startOffset,
                sides = sides,
                facing = facing,
                rotationOffset = rotationOffset,
                snapToGround = propsSnapToGround,
                alignToGroundNormal = propsAlignToGround,
                groundMask = groundMask,
                probeUp = probeUp,
                probeDown = probeDown,
                yawJitter = jitter,
                seed = propSeed,
                maxCount = budget
            };

        /// Roadside litter scatter settings. Reuses the shared ground-snap config so
        /// trash beds into the terrain the same way bollards and cones do.
        private RoadTrashSettings TrashSettings(int budget) => new RoadTrashSettings
        {
            roadHalfWidth = Width * 0.5f,
            minLateralOffset = trashMinOffset,
            maxLateralOffset = trashMaxOffset,
            baseDensityPerMetre = trashDensityPerMetre,
            junctionDensityMultiplier = trashJunctionDensityMultiplier,
            junctionBoostRadius = trashJunctionBoostRadius,
            sides = trashSides,
            snapToGround = propsSnapToGround,
            alignToGroundNormal = propsAlignToGround,
            groundMask = groundMask,
            probeUp = probeUp,
            probeDown = probeDown,
            prefabCount = trashPrefabs != null ? trashPrefabs.Length : 0,
            seed = trashSeed,
            maxCount = Mathf.Max(0, budget)
        };

        // ---- props ---------------------------------------------------------

        private void PlaceProps(SplineContainer container, List<List<RoadSample>> strands)
        {
            bool anyTrash = trashPrefabs != null && trashPrefabs.Length > 0 && trashDensityPerMetre > 0.0001f
                            && trashSides != RoadSide.None;

            if (bollardPrefab == null && conePrefab == null && !anyTrash) return;

            Transform bollardRoot = null, coneRoot = null, trashRoot = null;
            int bollards = 0, cones = 0, trash = 0;
            int budget = Mathf.Max(0, maxProps);
            int trashBudget = Mathf.Max(0, maxTrash);

            for (int i = 0; i < strands.Count; i++)
            {
                var strand = strands[i];
                if (strand == null || strand.Count < 2) continue;

                bool closed = i < container.Splines.Count && container.Splines[i].Closed;
                int left = budget - bollards - cones;
                if (left <= 0 && !anyTrash) break;

                if (bollardPrefab && bollardInterval > 0.1f && bollardSides != RoadSide.None && left > 0)
                {
                    var placed = RoadPropPlacer.Place(
                        strand, closed,
                        PropSettings(bollardInterval, bollardEdgeOffset, bollardHeightOffset, bollardStartOffset,
                                     bollardSides, bollardFacing, bollardRotationOffset, bollardYawJitter, left),
                        junctions, JunctionFilter.AwayFromJunctions, bollardJunctionMargin);

                    if (placed.Count > 0)
                    {
                        if (bollardRoot == null) bollardRoot = MakeRoot("Bollards");
                        foreach (var p in placed) Spawn(bollardPrefab, bollardRoot, p, bollards++, "Bollard_");
                    }
                    left = budget - bollards - cones;
                }

                if (conePrefab && coneInterval > 0.1f && coneSides != RoadSide.None &&
                    junctions.Count > 0 && left > 0)
                {
                    var placed = RoadPropPlacer.Place(
                        strand, closed,
                        PropSettings(coneInterval, coneEdgeOffset, coneHeightOffset, 0f,
                                     coneSides, coneFacing, coneRotationOffset, coneYawJitter, left),
                        junctions, JunctionFilter.AtJunctionsOnly, 0f);

                    if (placed.Count > 0)
                    {
                        if (coneRoot == null) coneRoot = MakeRoot("Cones");
                        foreach (var p in placed) Spawn(conePrefab, coneRoot, p, cones++, "Cone_");
                    }
                }

                // Trash has its own budget, independent of the bollard/cone budget,
                // and is scattered rather than evenly spaced — see PlaceTrash.
                if (anyTrash && trash < trashBudget)
                {
                    var placed = RoadPropPlacer.PlaceTrash(strand, closed, TrashSettings(trashBudget - trash), junctions);

                    if (placed.Count > 0)
                    {
                        if (trashRoot == null) trashRoot = MakeRoot("Trash");
                        foreach (var p in placed)
                        {
                            var prefab = trashPrefabs[((p.variant % trashPrefabs.Length) + trashPrefabs.Length) % trashPrefabs.Length];
                            if (prefab) Spawn(prefab, trashRoot, p, trash++, "Trash_");
                        }
                    }
                }
            }

            GameLog.Action(LogCat.World, "Road props placed",
                           $"'{name}': {bollards} bollard(s), {cones} cone(s), {trash} trash piece(s) " +
                           $"over {junctions.Count} junction(s)", this);

            if (bollards + cones >= budget && budget > 0)
                GameLog.Warn(LogCat.World,
                    $"Road '{name}' hit the {budget}-prop cap — raise Max Props or widen the intervals.", this);

            if (anyTrash && trash >= trashBudget && trashBudget > 0)
                GameLog.Warn(LogCat.World,
                    $"Road '{name}' hit the {trashBudget}-piece trash cap — raise Max Trash or lower the density.", this);
        }

        private Transform MakeRoot(string label)
        {
            var go = new GameObject(PropPrefix + label);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return go.transform;
        }

        /// The placement supplies the ALIGNMENT rotation only. Whatever the prefab root
        /// is authored with — the 90 degree correction on a Z-up export, say — is
        /// composed underneath it, so a spawned prop stands exactly as it does when you
        /// drag the prefab into the scene, just yawed to follow the road.
        private void Spawn(GameObject prefab, Transform parent, in RoadPropPlacement p, int index, string prefix)
        {
            GameObject go;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            else
                go = Instantiate(prefab, parent);
#else
            go = Instantiate(prefab, parent);
#endif
            if (go == null) return;

            go.transform.SetPositionAndRotation(p.position, p.rotation * prefab.transform.localRotation);
            SetWorldScale(go.transform, prefab.transform.localScale);
            go.name = $"{prefix}{index:000}";
            go.isStatic = true;
        }

        private static void SetWorldScale(Transform t, Vector3 world)
        {
            var parent = t.parent;
            if (parent == null) { t.localScale = world; return; }

            Vector3 l = parent.lossyScale;
            t.localScale = new Vector3(
                Mathf.Abs(l.x) > 1e-5f ? world.x / l.x : world.x,
                Mathf.Abs(l.y) > 1e-5f ? world.y / l.y : world.y,
                Mathf.Abs(l.z) > 1e-5f ? world.z / l.z : world.z);
        }

        // ---- clearing ------------------------------------------------------

        /// Delete anything standing on the driving surface. Three sources, in order of
        /// how likely they are to be the thing you noticed:
        ///   1. This road's own props — cones from one spline landing on the tarmac of
        ///      the spline it crosses, which is exactly what happens at a junction.
        ///   2. Scene objects on the clutter mask — imported trees, rocks, fences.
        ///   3. Unity terrain trees, if enabled.
        /// The shoulder is deliberately excluded: props live there on purpose.
        private void RemoveItems(List<List<RoadSample>> strands)
        {
            float half = Mathf.Max(0.1f, Width * 0.5f - clearEdgeInset);
            float cell = Mathf.Max(half + terrainTreeMargin + 2f, metresPerSample * 2f);

            var field = RoadClearance.Build(strands, cell);
            if (field.SampleCount == 0) return;

            int props = ClearOwnProps(field, half);
            int clutter = ClearSceneClutter(field, half);
            int trees = removeTerrainTrees ? ClearTerrainTrees(field, half + terrainTreeMargin) : 0;

            GameLog.Action(LogCat.World, "Road cleared of items",
                           $"'{name}': {props} prop(s), {clutter} scene object(s), {trees} terrain tree(s) " +
                           $"removed from a {half * 2f:0.0} m surface", this);
        }

        private int ClearOwnProps(RoadClearance field, float half)
        {
            int removed = 0;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var root = transform.GetChild(i);
                if (!root.name.StartsWith(PropPrefix)) continue;

                for (int j = root.childCount - 1; j >= 0; j--)
                {
                    var prop = root.GetChild(j);
                    if (!field.IsOnRoad(prop.position, half, clearAbove, clearBelow)) continue;
                    DestroyObject(prop.gameObject);
                    removed++;
                }
            }

            return removed;
        }

        private int ClearSceneClutter(RoadClearance field, float half)
        {
            if (clutterMask.value == 0) return 0;

            var candidates = new HashSet<GameObject>();
            float radius = half + 1f;
            var buffer = new Collider[32];

            // Sweep along the centrelines rather than one huge overlap: the road is a
            // thin ribbon and its bounding box is mostly not road.
            foreach (var strand in field.Sweep(radius))
            {
                int count = UnityEngine.Physics.OverlapSphereNonAlloc(
                    strand, radius, buffer, clutterMask, QueryTriggerInteraction.Collide);

                for (int i = 0; i < count; i++)
                {
                    var col = buffer[i];
                    if (col == null) continue;

                    var go = Outermost(col.gameObject);
                    if (go == null || go == gameObject) continue;
                    if (go.GetComponent<Terrain>()) continue;                      // never the terrain itself
                    if (go.GetComponentInParent<RoadSpline>() != null) continue;   // never another road's meshes
                    if (go.transform.IsChildOf(transform)) continue;               // own props handled above

                    if (!field.IsOnRoad(go.transform.position, half, clearAbove, clearBelow) &&
                        !field.IsOnRoad(col.bounds.center, half, clearAbove, clearBelow)) continue;

                    candidates.Add(go);
                }
            }

            foreach (var go in candidates) DestroyObject(go);
            return candidates.Count;
        }

        /// The whole prefab instance, not the one child that happened to carry the
        /// collider — deleting a tree's trunk collider and leaving its canopy behind
        /// would be worse than doing nothing.
        private static GameObject Outermost(GameObject go)
        {
#if UNITY_EDITOR
            var prefabRoot = UnityEditor.PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (prefabRoot != null) return prefabRoot;
#endif
            return go;
        }

        private int ClearTerrainTrees(RoadClearance field, float half)
        {
            int removed = 0;

            foreach (var terrain in Terrain.activeTerrains)
            {
                var data = terrain ? terrain.terrainData : null;
                if (data == null) continue;

                var trees = data.treeInstances;
                if (trees == null || trees.Length == 0) continue;

                var kept = new List<TreeInstance>(trees.Length);
                Vector3 origin = terrain.transform.position;
                Vector3 size = data.size;
                int cut = 0;

                foreach (var tree in trees)
                {
                    Vector3 world = origin + Vector3.Scale(tree.position, size);
                    if (field.IsOnRoad(world, half, clearAbove, clearBelow)) { cut++; continue; }
                    kept.Add(tree);
                }

                if (cut == 0) continue;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEditor.Undo.RegisterCompleteObjectUndo(data, "Remove trees from road");
#endif
                data.treeInstances = kept.ToArray();
                terrain.Flush();

                // Rebinding the collider is what makes the removed trunks stop being
                // solid; without it the trees are invisible but still there.
                var collider = terrain.GetComponent<TerrainCollider>();
                if (collider) collider.terrainData = data;

                removed += cut;
            }

            return removed;
        }

        private void DestroyObject(GameObject go)
        {
            if (Application.isPlaying) { Destroy(go); return; }
#if UNITY_EDITOR
            UnityEditor.Undo.DestroyObjectImmediate(go);
#else
            DestroyImmediate(go);
#endif
        }

        // ---- children ------------------------------------------------------

        /// Builds the road chunk GameObject and, if this chunk has one, its skirt
        /// child — the embankment mesh that runs down and outward from the shoulder
        /// to the real ground. Parented under the road chunk (not a sibling of it),
        /// so it is literally "under the road" in the hierarchy as well as in space,
        /// and gets cleared automatically whenever the road chunk does.
        private GameObject CreateChunk(in RoadMeshChunk chunk, int index)
        {
            var go = new GameObject($"{MeshPrefix}{index:00}");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            go.isStatic = true;

            go.AddComponent<MeshFilter>().sharedMesh = chunk.Road;
            go.AddComponent<MeshRenderer>().sharedMaterial = surface.material;
            go.AddComponent<RoadSurfaceTag>().SetSurface(surface);

            if (generateCollider)
            {
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = chunk.Road;
                if (surface.physicsMaterial) col.sharedMaterial = surface.physicsMaterial;
            }

            if (chunk.Skirt != null) CreateSkirtChild(go.transform, chunk.Skirt, index);

            return go;
        }

        private void CreateSkirtChild(Transform parent, Mesh mesh, int index)
        {
            var go = new GameObject($"{SkirtPrefix}{index:00}");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            go.isStatic = true;

            var mat = surface.skirtMaterial ? surface.skirtMaterial : surface.material;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<RoadSurfaceTag>().SetSurface(surface);

            if (generateCollider)
            {
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;
                if (surface.physicsMaterial) col.sharedMaterial = surface.physicsMaterial;
            }
        }

        private void ClearMeshes() => ClearChildren(MeshPrefix);
        private void ClearProps() => ClearChildren(PropPrefix);

        private void ClearChildren(string prefix)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(prefix)) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        // Chunk meshes are built in world space and parented at identity, so renderer
        // bounds and mesh bounds agree. Props are skipped — a tall sign would otherwise
        // inflate the bounds that nearest-road queries reject against. Skirt renderers
        // are picked up automatically here since GetComponentsInChildren recurses into
        // each RoadMesh_* chunk, where the skirt now lives.
        private Bounds WorldBounds()
        {
            bool any = false;
            var result = new Bounds(transform.position, Vector3.one);

            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(MeshPrefix)) continue;

                foreach (var renderer in child.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!any) { result = renderer.bounds; any = true; }
                    else result.Encapsulate(renderer.bounds);
                }
            }

            if (any) result.Expand(Width);   // pad so nearest-road queries do not miss at the edge
            return result;
        }

        // ---- editor --------------------------------------------------------
#if UNITY_EDITOR
        /// Generated meshes must become real assets, or they vanish on the next domain
        /// reload and every road turns invisible. Same reasoning as StageBaker. Skirt
        /// meshes are included here too — they live on child objects named RoadSkirt_*
        /// under each RoadMesh_* chunk, and would otherwise never get saved.
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
                bool isRoadMesh = filter.name.StartsWith(MeshPrefix) || filter.name.StartsWith(SkirtPrefix);
                if (!isRoadMesh) continue;   // never re-asset a prop's mesh

                var mesh = filter.sharedMesh;
                if (mesh == null || UnityEditor.AssetDatabase.Contains(mesh)) continue;

                string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{folder}/{mesh.name}.asset");
                UnityEditor.AssetDatabase.CreateAsset(mesh, path);
            }
            UnityEditor.AssetDatabase.SaveAssets();
        }

        private void OnDrawGizmosSelected()
        {
            if (centreline.Count >= 2)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                for (int i = 1; i < centreline.Count; i++)
                    Gizmos.DrawLine(centreline[i - 1].position, centreline[i].position);
            }

            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.8f);
            foreach (var j in junctions) Gizmos.DrawWireSphere(j.position, j.radius);

            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.25f);
            foreach (var j in junctions) Gizmos.DrawWireSphere(j.position, j.radius + bollardJunctionMargin);
        }
#endif
    }
}