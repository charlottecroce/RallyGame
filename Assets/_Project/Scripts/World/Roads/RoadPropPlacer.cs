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