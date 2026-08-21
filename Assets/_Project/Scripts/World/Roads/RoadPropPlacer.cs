using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.World.Roads
{
    /// Where one prop goes. Prefab-agnostic: RoadSpline decides what gets spawned.
    /// Rotation here is the ALIGNMENT only — the prefab's own root rotation is
    /// composed on top of it at spawn time, so what you author still counts.
    public struct RoadPropPlacement
    {
        public Vector3 position;
        public Quaternion rotation;
        [Tooltip("Which prefab variant this point picked, for callers with more than one prop mesh (trash).")]
        public int variant;
    }

    public enum RoadSide { Both = 0, Left = 1, Right = 2, None = 3 }

    /// Which way the prop's local +Z ends up pointing. A model authored with its
    /// front on +X, or exported Z-up, lands sideways under AlongRoad — fix those with
    /// the rotation offset rather than by re-exporting.
    public enum PropFacing
    {
        AlongRoad,      // down the road; both sides identical
        AcrossRoad,     // perpendicular; both sides identical
        TowardRoad,     // faces the centreline (mirrored per side)
        AwayFromRoad,   // faces the verge (mirrored per side)
        None            // upright only, no yaw applied
    }

    public enum JunctionFilter
    {
        Ignore,
        AwayFromJunctions,   // bollards: everywhere except the junctions
        AtJunctionsOnly      // cones: only inside them
    }

    public struct RoadPropSettings
    {
        public float roadHalfWidth;
        public float lateralOffset;    // outward from the road edge, into the shoulder
        public float verticalOffset;   // lift off the ground, for prefabs with a buried pivot
        public float interval;
        public float startOffset;
        public RoadSide sides;
        public PropFacing facing;
        public Vector3 rotationOffset; // degrees, applied in the prop's own frame
        public bool snapToGround;
        public bool alignToGroundNormal;
        public LayerMask groundMask;
        public float probeUp;
        public float probeDown;
        public float yawJitter;        // degrees, seeded so a rebake reproduces the same scene
        public int seed;
        public int maxCount;
    }

    /// Settings for the randomised roadside litter scatter. Unlike the interval-based
    /// bollard/cone placement above, trash is a Poisson-style scatter: irregular
    /// spacing along the road, a random distance from the edge (some right up against
    /// the tarmac, some well into the verge), and denser near junctions where traffic
    /// actually stops and idles.
    ///
    /// Trash carries its own alignment settings rather than sharing the props ones: a
    /// bollard should stand vertical on a slope, a crushed can should lie flat on it.
    public struct RoadTrashSettings
    {
        public float roadHalfWidth;
        [Tooltip("Nearest a piece can land from the road edge, metres.")]
        public float minLateralOffset;
        [Tooltip("Furthest a piece can land from the road edge, metres.")]
        public float maxLateralOffset;
        [Tooltip("Average pieces per metre of road, baseline (away from junctions).")]
        public float baseDensityPerMetre;
        [Tooltip("Density is multiplied by this near a junction.")]
        public float junctionDensityMultiplier;
        [Tooltip("Extra radius beyond a junction's own radius where the density boost applies.")]
        public float junctionBoostRadius;
        public RoadSide sides;
        public LayerMask groundMask;
        public float probeUp;
        public float probeDown;
        public int prefabCount;   // how many prefab variants the caller has, for picking one per point
        public int seed;
        public int maxCount;

        // ---- bedding into the ground ----
        public bool snapToGround;
        [Tooltip("Lie each piece on the terrain under it instead of leaving it level.")]
        public bool alignToGroundNormal;
        [Tooltip("How much of the ground's tilt to take. 1 = lie flat on the slope.")]
        public float normalBlend;
        [Tooltip("Never tilt past this, whatever the ground does. 0 = no limit.")]
        public float maxTiltDegrees;
        [Tooltip("Radius of the probe triangle used to read the slope. 0 = single-raycast normal.")]
        public float footprintRadius;
        [Tooltip("Moved along the ground normal after snapping. Negative beds the piece in.")]
        public float verticalOffset;
        [Tooltip("Skip ground steeper than this. 0 = place anywhere.")]
        public float maxSlopeDegrees;
        [Tooltip("Drop pieces that found no ground, instead of leaving them at road height.")]
        public bool requireGround;
    }

    /// Pure placement maths: samples in, transforms out. Same shape as
    /// RoadMeshBuilder so it can be tested without a scene.
    ///
    /// Props are placed by arc length rather than by sample index, so the spacing is
    /// the spacing you typed no matter what metresPerSample is set to.
    public static class RoadPropPlacer
    {
        public static List<RoadPropPlacement> Place(List<RoadSample> samples, bool closed,
                                                    in RoadPropSettings s,
                                                    IReadOnlyList<RoadJunction> junctions,
                                                    JunctionFilter filter, float margin)
        {
            var result = new List<RoadPropPlacement>();
            if (samples == null || samples.Count < 2 || s.sides == RoadSide.None) return result;

            float total = samples[samples.Count - 1].distance;
            if (total <= 0.01f) return result;

            float step = Mathf.Max(0.5f, s.interval);
            int max = s.maxCount > 0 ? s.maxCount : int.MaxValue;

            // A closed loop's last slot would land on top of the first one.
            float end = closed ? total - step * 0.5f : total;

            var rng = new System.Random(s.seed);
            int cursor = 1;

            bool left = s.sides == RoadSide.Both || s.sides == RoadSide.Left;
            bool right = s.sides == RoadSide.Both || s.sides == RoadSide.Right;

            for (float d = Mathf.Max(0f, s.startOffset); d <= end && result.Count < max; d += step)
            {
                Evaluate(samples, d, ref cursor, out Vector3 pos, out Vector3 fwd, out Vector3 up);

                Vector3 across = Vector3.Cross(up, fwd);
                if (across.sqrMagnitude < 1e-6f) continue;
                across.Normalize();

                float offset = s.roadHalfWidth + s.lateralOffset;

                if (left) TryAdd(pos - across * offset, fwd, across, -1f, up, s, junctions, filter, margin, rng, result);
                if (right) TryAdd(pos + across * offset, fwd, across, 1f, up, s, junctions, filter, margin, rng, result);
            }

            return result;
        }

        /// Randomised roadside litter scatter. Walks the strand in small fixed steps
        /// and rolls, at every step, whether a piece spawns there (a Poisson-style
        /// process) — this is what makes the result look scattered rather than
        /// ruler-straight like the bollards. The per-step spawn probability increases
        /// near junctions, and each spawned piece lands at a random distance between
        /// minLateralOffset and maxLateralOffset from the road edge (so some sit right
        /// on the shoulder and some are flung well into the verge) with a fully random
        /// yaw.
        public static List<RoadPropPlacement> PlaceTrash(List<RoadSample> samples, bool closed,
                                                          in RoadTrashSettings s,
                                                          IReadOnlyList<RoadJunction> junctions)
        {
            var result = new List<RoadPropPlacement>();
            if (samples == null || samples.Count < 2 || s.sides == RoadSide.None) return result;
            if (s.baseDensityPerMetre <= 0f) return result;

            float total = samples[samples.Count - 1].distance;
            if (total <= 0.01f) return result;

            int max = s.maxCount > 0 ? s.maxCount : int.MaxValue;
            const float scanStep = 1f;   // metres between spawn rolls; irregular result comes from the RNG, not this

            var rng = new System.Random(s.seed);
            int cursor = 1;

            bool left = s.sides == RoadSide.Both || s.sides == RoadSide.Left;
            bool right = s.sides == RoadSide.Both || s.sides == RoadSide.Right;

            for (float d = 0f; d < total && result.Count < max; d += scanStep)
            {
                Evaluate(samples, d, ref cursor, out Vector3 pos, out Vector3 fwd, out Vector3 up);

                Vector3 across = Vector3.Cross(up, fwd);
                if (across.sqrMagnitude < 1e-6f) continue;
                across.Normalize();

                float density = s.baseDensityPerMetre * JunctionDensityMultiplier(pos, junctions,
                    s.junctionBoostRadius, s.junctionDensityMultiplier);

                // Expected count this step; can exceed 1 near a junction, in which
                // case multiple pieces can land in the same short span.
                float expected = density * scanStep;

                while (expected > 0f && result.Count < max)
                {
                    if (rng.NextDouble() > System.Math.Min(1.0, expected)) break;
                    expected -= 1f;

                    bool useLeft = left && (!right || rng.NextDouble() < 0.5);
                    if (!useLeft && !right) continue;
                    float side = useLeft ? -1f : 1f;

                    // Random distance from the edge — this is the "some right
                    // alongside, some further away" spread.
                    float lateral = s.roadHalfWidth +
                        Mathf.Lerp(s.minLateralOffset, s.maxLateralOffset, (float)rng.NextDouble());
                    Vector3 at = pos + across * (lateral * side);

                    TryAddTrash(at, s, rng, result);
                }
            }

            return result;
        }

        private static void TryAdd(Vector3 at, Vector3 fwd, Vector3 across, float side, Vector3 up,
                                   in RoadPropSettings s, IReadOnlyList<RoadJunction> junctions,
                                   JunctionFilter filter, float margin,
                                   System.Random rng, List<RoadPropPlacement> into)
        {
            if (filter != JunctionFilter.Ignore)
            {
                bool near = RoadJunctions.IsNear(junctions, at, margin);
                if (filter == JunctionFilter.AwayFromJunctions && near) return;
                if (filter == JunctionFilter.AtJunctionsOnly && !near) return;
            }

            // The road plane at the shoulder is not the ground at the shoulder — on a
            // side slope they differ by a lot, so props get their own probe.
            Vector3 normal = up;
            if (s.snapToGround &&
                UnityEngine.Physics.Raycast(at + Vector3.up * s.probeUp, Vector3.down, out var hit,
                                            s.probeUp + s.probeDown, s.groundMask,
                                            QueryTriggerInteraction.Ignore))
            {
                at = hit.point;
                if (s.alignToGroundNormal) normal = hit.normal;
            }

            if (!s.alignToGroundNormal) normal = Vector3.up;   // stand upright on a slope
            at += normal * s.verticalOffset;

            Vector3 look;
            switch (s.facing)
            {
                case PropFacing.AcrossRoad: look = across; break;
                case PropFacing.TowardRoad: look = -across * side; break;
                case PropFacing.AwayFromRoad: look = across * side; break;
                case PropFacing.None: look = Vector3.zero; break;
                default: look = fwd; break;
            }

            Quaternion rot;
            Vector3 flat = Vector3.ProjectOnPlane(look, normal);
            if (flat.sqrMagnitude > 1e-6f) rot = Quaternion.LookRotation(flat.normalized, normal);
            else rot = Quaternion.FromToRotation(Vector3.up, normal);   // facing None: tilt only

            if (s.yawJitter > 0.01f)
                rot = Quaternion.AngleAxis((float)(rng.NextDouble() * 2.0 - 1.0) * s.yawJitter, normal) * rot;

            // Right-multiplied, so the offset is in the prop's own frame: "yaw it 90"
            // means the same thing whichever direction the road happens to run.
            rot *= Quaternion.Euler(s.rotationOffset);

            into.Add(new RoadPropPlacement { position = at, rotation = rot });
        }

        /// Ground-snaps litter and lies it down on the terrain underneath it.
        ///
        /// The anchor is the centre hit, but the ORIENTATION comes from a plane fitted
        /// through three probes a footprint apart. A single raycast returns the normal of
        /// ONE triangle, which flips hard from facet to facet on a low-poly terrain and
        /// leaves a flat piece hovering on one corner. A bottle lying across a rut should
        /// sit on the rut, not on whichever facet its pivot happened to land on.
        private static void TryAddTrash(Vector3 at, in RoadTrashSettings s, System.Random rng,
                                        List<RoadPropPlacement> into)
        {
            Vector3 normal = Vector3.up;

            if (s.snapToGround)
            {
                if (Probe(at, s, out Vector3 centre, out Vector3 hitNormal))
                {
                    at = centre;
                    normal = s.footprintRadius > 0.01f ? FitNormal(centre, s, hitNormal) : hitNormal;
                }
                else if (s.requireGround) return;   // a floating crisp packet is worse than none
            }

            if (!s.alignToGroundNormal) normal = Vector3.up;

            // Litter does not sit on cliffs, and an aligned piece on a steep face reads
            // as a decal stuck to a wall. Cheaper to not place it at all.
            if (s.maxSlopeDegrees > 0.01f && Vector3.Angle(normal, Vector3.up) > s.maxSlopeDegrees) return;

            // Blend first, then clamp: "mostly follow the ground, but never past 40
            // degrees" without needing a second set of probes.
            Vector3 aligned = s.normalBlend < 0.999f
                ? Vector3.Slerp(Vector3.up, normal, Mathf.Clamp01(s.normalBlend)).normalized
                : normal;

            if (s.maxTiltDegrees > 0.01f && Vector3.Angle(Vector3.up, aligned) > s.maxTiltDegrees)
                aligned = Vector3.RotateTowards(Vector3.up, aligned, s.maxTiltDegrees * Mathf.Deg2Rad, 0f).normalized;

            at += aligned * s.verticalOffset;

            // Yaw is right-multiplied, so it turns about the piece's own (already tilted)
            // up axis — the piece spins in the ground plane instead of the world plane.
            float yaw = (float)(rng.NextDouble() * 360.0);
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, aligned) * Quaternion.Euler(0f, yaw, 0f);

            int variant = s.prefabCount > 0 ? rng.Next(s.prefabCount) : 0;

            into.Add(new RoadPropPlacement { position = at, rotation = rot, variant = variant });
        }

        private static bool Probe(Vector3 at, in RoadTrashSettings s, out Vector3 point, out Vector3 normal)
        {
            if (UnityEngine.Physics.Raycast(at + Vector3.up * s.probeUp, Vector3.down, out var hit,
                                            s.probeUp + s.probeDown, s.groundMask,
                                            QueryTriggerInteraction.Ignore))
            {
                point = hit.point; normal = hit.normal; return true;
            }

            point = at; normal = Vector3.up; return false;
        }

        // 120 degrees apart on a circle. World-axis aligned is fine: the piece gets a
        // fully random yaw afterwards anyway.
        private static readonly Vector3[] fitDirs =
        {
            new Vector3(0f, 0f, 1f),
            new Vector3(0.8660254f, 0f, -0.5f),
            new Vector3(-0.8660254f, 0f, -0.5f)
        };
        private static readonly Vector3[] fitPoints = new Vector3[3];

        /// Plane through three probes around the anchor. Falls back to the single-hit
        /// normal if any probe misses — a piece at the lip of a hole should follow the
        /// ground it is on, not tilt toward the void.
        private static Vector3 FitNormal(Vector3 centre, in RoadTrashSettings s, Vector3 fallback)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!Probe(centre + fitDirs[i] * s.footprintRadius, s, out fitPoints[i], out _))
                    return fallback;
            }

            Vector3 n = Vector3.Cross(fitPoints[1] - fitPoints[0], fitPoints[2] - fitPoints[0]);
            if (n.y < 0f) n = -n;
            return n.sqrMagnitude > 1e-8f ? n.normalized : fallback;
        }

        /// 1 everywhere except within (junction.radius + boostRadius) of a junction,
        /// where it returns the multiplier. Cheap linear scan — junction counts are
        /// tiny compared to prop counts.
        private static float JunctionDensityMultiplier(Vector3 pos, IReadOnlyList<RoadJunction> junctions,
                                                        float boostRadius, float multiplier)
        {
            if (junctions == null || junctions.Count == 0 || multiplier <= 1f) return 1f;

            for (int i = 0; i < junctions.Count; i++)
            {
                float r = junctions[i].radius + boostRadius;
                float dx = junctions[i].position.x - pos.x;
                float dz = junctions[i].position.z - pos.z;
                if (dx * dx + dz * dz <= r * r) return multiplier;
            }

            return 1f;
        }

        /// Position/frame at an arc length. The cursor only ever moves forward, so the
        /// whole walk stays linear in the number of samples.
        private static void Evaluate(List<RoadSample> samples, float d, ref int cursor,
                                     out Vector3 pos, out Vector3 fwd, out Vector3 up)
        {
            if (cursor < 1) cursor = 1;
            while (cursor < samples.Count - 1 && samples[cursor].distance < d) cursor++;

            var a = samples[cursor - 1];
            var b = samples[cursor];

            float span = b.distance - a.distance;
            float t = span > 1e-4f ? Mathf.Clamp01((d - a.distance) / span) : 0f;

            pos = Vector3.Lerp(a.position, b.position, t);
            fwd = Vector3.Slerp(a.forward, b.forward, t).normalized;
            up = Vector3.Slerp(a.up, b.up, t).normalized;
        }
    }
}