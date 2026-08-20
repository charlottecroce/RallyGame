using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace RallyGame.World.Roads
{
    /// One cross-section of road: where it sits, which way it runs, which way is up.
    [System.Serializable]
    public struct RoadSample
    {
        public Vector3 position;
        public Vector3 forward;
        public Vector3 up;
        public float distance;     // metres from the start of the spline
    }

    /// Everything the builder needs, in one struct so callers pass a value, not
    /// twenty arguments.
    public struct RoadBuildSettings
    {
        public float width;
        public float shoulderWidth;
        public float shoulderDrop;
        public float heightOffset;
        public float metresPerSample;
        public float uvTilesPerMetre;
        public bool conformToGround;
        public LayerMask groundMask;
        public float probeUp;
        public float probeDown;
        public int maxRingsPerChunk;

        // ---- ground fit (see Sample) ----
        public int crossProbes;         // probes across the width per ring; forced odd
        public bool probeMidRings;      // also probe halfway to the next ring
        public int smoothingPasses;     // vertical relaxation iterations
        public float smoothingStrength; // 0..1 blend toward the neighbour average
        public float bankBlend;         // 0 = spline up, 1 = full terrain cross-slope
    }

    /// Pure functions: spline in, samples and meshes out. No MonoBehaviour, no scene
    /// state, so it runs identically in the editor and at runtime and can be unit
    /// tested. RoadSpline is the only thing that knows about GameObjects.
    public static class RoadMeshBuilder
    {
        private const int MaxProbes = 9;

        // Baking is single-threaded main-thread work, so shared scratch is safe and
        // keeps a 2500-ring road from allocating three arrays per cross-section.
        private static readonly float[] probeOffset = new float[MaxProbes];
        private static readonly Vector3[] probePoint = new Vector3[MaxProbes];
        private static readonly bool[] probeFound = new bool[MaxProbes];

        /// Walk the spline at a fixed spacing and fit each cross-section to the ground.
        ///
        /// Three things are done here that a single centreline raycast cannot do, and
        /// between them they are what stops the road burying itself on hilly terrain
        /// WITHOUT raising heightOffset:
        ///
        ///  1. A fan of probes across the width, not one down the middle. The ring is
        ///     a straight bar; on a cross-slope its outer edge sinks even though the
        ///     centre is clear. The fan gives the ring the terrain's cross-slope and,
        ///     more importantly, tells us how high the centre must sit for the WORST
        ///     probe to still clear the ground by heightOffset.
        ///  2. A probe halfway between rings. The mesh between two cross-sections is a
        ///     flat chord, so a crest that falls between samples cuts straight through
        ///     it. That deficit is measured and the two ends are lifted by exactly the
        ///     sag, which is what removes the "road disappears over the brow" artefact.
        ///  3. A vertical relaxation pass. Following every terrain wrinkle is what
        ///     makes the surface feel bumpy to drive. Heights are smoothed toward their
        ///     neighbours, then clamped back up against the clearances from (1) and (2)
        ///     — smoothing can only ever give back height, never push the road under.
        ///
        /// missedProbes reports how many cross-sections found no ground under the
        /// centreline — a non-zero count almost always means the terrain is not on the
        /// ground mask.
        public static List<RoadSample> Sample(SplineContainer container, int splineIndex,
                                              in RoadBuildSettings s, out int missedProbes)
        {
            missedProbes = 0;
            var samples = new List<RoadSample>();

            var spline = container.Splines[splineIndex];
            if (spline == null || spline.Count < 2) return samples;

            float length = container.CalculateLength(splineIndex);
            if (length <= 0.01f) return samples;

            bool closed = spline.Closed;
            int steps = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.25f, s.metresPerSample)));
            int rings = closed ? steps : steps + 1;

            var pos = new Vector3[rings];
            var fwd = new Vector3[rings];
            var up = new Vector3[rings];
            var floor = new float[rings];      // lowest this ring's centre may sit
            var midFloor = new float[rings];   // same, for the chord from ring i to i+1

            // ---- pass 1: frame each ring and measure the ground under it ----
            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / steps;
                container.Evaluate(splineIndex, t, out float3 p, out float3 tan, out float3 upv);

                Vector3 point = p;
                Vector3 f = Dir(tan, Vector3.forward);
                Vector3 u = Dir(upv, Vector3.up);

                floor[i] = float.NegativeInfinity;
                midFloor[i] = float.NegativeInfinity;

                if (!s.conformToGround)
                {
                    point += u * s.heightOffset;
                }
                else if (Fan(point, f, u, s, out float centreY, out Vector3 fitUp, out float need))
                {
                    point.y = centreY;
                    u = fitUp;
                    floor[i] = need;
                }
                else
                {
                    missedProbes++;
                    point += u * s.heightOffset;       // no ground here: leave it on the spline
                }

                pos[i] = point;
                fwd[i] = f;
                up[i] = u;

                if (s.conformToGround && s.probeMidRings && (closed || i < rings - 1))
                {
                    float tm = (i + 0.5f) / steps;     // always < 1, so no wrap needed
                    container.Evaluate(splineIndex, tm, out float3 pm, out float3 tm2, out float3 um2);
                    if (Fan(pm, Dir(tm2, Vector3.forward), Dir(um2, Vector3.up), s, out _, out _, out float midNeed))
                        midFloor[i] = midNeed;
                }
            }

            // ---- pass 2: relax the heights, then clamp them back above the terrain ----
            if (s.conformToGround)
            {
                var y = new float[rings];
                var tmp = new float[rings];
                for (int i = 0; i < rings; i++) y[i] = pos[i].y;

                int passes = Mathf.Max(0, s.smoothingPasses);
                float k = Mathf.Clamp01(s.smoothingStrength);
                int chords = closed ? rings : rings - 1;

                for (int pass = 0; pass <= passes; pass++)
                {
                    if (pass < passes && k > 0f)
                    {
                        for (int i = 0; i < rings; i++)
                        {
                            int a = Prev(i, rings, closed), b = Next(i, rings, closed);
                            tmp[i] = Mathf.Lerp(y[i], (y[a] + y[b]) * 0.5f, k);
                        }
                        var swap = y; y = tmp; tmp = swap;
                    }

                    // Clearance runs last in every pass, so the result is always above
                    // ground no matter how hard the smoothing pulled.
                    for (int i = 0; i < rings; i++)
                        if (floor[i] > y[i]) y[i] = floor[i];

                    for (int i = 0; i < chords; i++)
                    {
                        if (float.IsNegativeInfinity(midFloor[i])) continue;
                        int j = (i + 1) % rings;
                        float sag = midFloor[i] - (y[i] + y[j]) * 0.5f;
                        if (sag > 0f) { y[i] += sag; y[j] += sag; }   // lift by the exact sag
                    }
                }

                for (int i = 0; i < rings; i++) pos[i].y = y[i];

                // Re-derive forward from the settled polyline — a smoothed height
                // changes the slope, and a stale tangent tilts the ring against it.
                for (int i = 0; i < rings; i++)
                {
                    int a = Prev(i, rings, closed), b = Next(i, rings, closed);
                    Vector3 f = pos[b] - pos[a];
                    if (f.sqrMagnitude > 1e-6f) fwd[i] = f.normalized;
                }

                if (passes > 0 && k > 0f) SmoothUp(up, rings, closed);
            }

            // ---- pass 3: orthonormal frames and arc length ----
            Vector3 previous = Vector3.zero;
            float travelled = 0f;

            for (int i = 0; i < rings; i++)
            {
                Vector3 f = fwd[i], u = up[i];

                Vector3 right = Vector3.Cross(u, f);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, f);
                right.Normalize();
                f = Vector3.Cross(right, u).normalized;
                u = Vector3.Cross(f, right).normalized;

                if (i > 0) travelled += Vector3.Distance(pos[i], previous);
                previous = pos[i];

                samples.Add(new RoadSample { position = pos[i], forward = f, up = u, distance = travelled });
            }

            return samples;
        }

        // ---- ground fitting helpers ----------------------------------------

        /// Cast a fan of probes across the driving surface at one cross-section.
        /// Returns false when there is no ground under the centreline at all.
        private static bool Fan(Vector3 centre, Vector3 fwd, Vector3 splineUp, in RoadBuildSettings s,
                                out float centreY, out Vector3 up, out float requiredY)
        {
            centreY = centre.y;
            up = splineUp;
            requiredY = float.NegativeInfinity;

            int n = Mathf.Clamp(s.crossProbes | 1, 1, MaxProbes);   // odd, so one probe is the centre
            int mid = n / 2;
            float half = s.width * 0.5f;

            Vector3 right = Vector3.Cross(splineUp, fwd);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, fwd);
            right.Normalize();

            int lo = -1, hi = -1;
            for (int i = 0; i < n; i++)
            {
                float d = n == 1 ? 0f : Mathf.Lerp(-half, half, (float)i / (n - 1));
                Vector3 at = centre + right * d;

                probeOffset[i] = d;
                probeFound[i] = Ground(at, s, out Vector3 hit);
                probePoint[i] = probeFound[i] ? hit : at;

                if (!probeFound[i]) continue;
                if (lo < 0) lo = i;
                hi = i;
            }

            if (!probeFound[mid]) return false;

            // The line through the outermost hits is the ground the bar has to lie
            // along. Without this the ring stays level across and one edge digs into a
            // side slope however high the centre sits.
            if (hi > lo)
            {
                Vector3 across = probePoint[hi] - probePoint[lo];
                Vector3 terrainUp = Vector3.Cross(fwd, across);
                if (terrainUp.y < 0f) terrainUp = -terrainUp;
                if (terrainUp.sqrMagnitude > 1e-6f)
                    up = Vector3.Slerp(splineUp.normalized, terrainUp.normalized,
                                       Mathf.Clamp01(s.bankBlend)).normalized;
            }

            Vector3 fitRight = Vector3.Cross(up, fwd);
            if (fitRight.sqrMagnitude < 1e-6f) fitRight = right;
            fitRight.Normalize();

            // Given that tilt, the road surface at lateral offset d sits at
            // centreY + fitRight.y * d, so solve each probe for the centre height it
            // demands and keep the worst one.
            for (int i = 0; i < n; i++)
            {
                if (!probeFound[i]) continue;
                float need = probePoint[i].y + s.heightOffset - fitRight.y * probeOffset[i];
                if (need > requiredY) requiredY = need;
            }

            centreY = probePoint[mid].y + s.heightOffset;
            return true;
        }

        private static bool Ground(Vector3 at, in RoadBuildSettings s, out Vector3 point)
        {
            if (UnityEngine.Physics.Raycast(at + Vector3.up * s.probeUp, Vector3.down, out var hit,
                                            s.probeUp + s.probeDown, s.groundMask,
                                            QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }
            point = at;
            return false;
        }

        /// One weighted average pass over the up vectors — stops the banking flicking
        /// between cross-sections on noisy terrain.
        private static void SmoothUp(Vector3[] up, int n, bool closed)
        {
            if (n < 3) return;
            var src = (Vector3[])up.Clone();
            for (int i = 0; i < n; i++)
            {
                int a = Prev(i, n, closed), b = Next(i, n, closed);
                Vector3 avg = src[a] + src[i] * 2f + src[b];
                if (avg.sqrMagnitude > 1e-6f) up[i] = avg.normalized;
            }
        }

        private static Vector3 Dir(float3 v, Vector3 fallback)
        {
            Vector3 x = v;
            return x.sqrMagnitude > 1e-6f ? x.normalized : fallback;
        }

        private static int Prev(int i, int n, bool closed) => i > 0 ? i - 1 : (closed ? n - 1 : 0);
        private static int Next(int i, int n, bool closed) => i < n - 1 ? i + 1 : (closed ? 0 : n - 1);

        // ---- mesh ----------------------------------------------------------

        /// Ribbon-extrude the samples into one or more meshes.
        ///
        /// Chunking is not an optimisation afterthought — a 5 km road at 2 m spacing is
        /// 2500 rings, and one mesh that size culls as a single unit and blows past the
        /// 16-bit index limit. Consecutive chunks share their boundary ring, so there
        /// is no seam.
        public static List<Mesh> Build(List<RoadSample> samples, bool closed, in RoadBuildSettings s, string name)
        {
            var meshes = new List<Mesh>();
            if (samples == null || samples.Count < 2) return meshes;

            bool skirt = s.shoulderWidth > 0.001f;
            int perRing = skirt ? 4 : 2;
            int ringsPerChunk = Mathf.Max(2, s.maxRingsPerChunk);

            int total = closed ? samples.Count + 1 : samples.Count;   // closed loops repeat ring 0
            int chunk = 0;

            for (int start = 0; start < total - 1; start += ringsPerChunk - 1)
            {
                int end = Mathf.Min(start + ringsPerChunk - 1, total - 1);
                meshes.Add(BuildChunk(samples, start, end, perRing, skirt, s, $"{name}_Chunk{chunk:00}"));
                chunk++;
            }

            return meshes;
        }

        private static Mesh BuildChunk(List<RoadSample> samples, int start, int end,
                                       int perRing, bool skirt, in RoadBuildSettings s, string name)
        {
            int ringCount = end - start + 1;
            int vertCount = ringCount * perRing;

            var verts = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var tris = new int[(ringCount - 1) * (perRing - 1) * 6];

            float half = s.width * 0.5f;
            int ti = 0;

            for (int r = 0; r < ringCount; r++)
            {
                var sample = samples[(start + r) % samples.Count];
                Vector3 right = Vector3.Cross(sample.up, sample.forward).normalized;
                float v = sample.distance * s.uvTilesPerMetre;
                int b = r * perRing;

                if (skirt)
                {
                    // Skirt verts sit outboard and lower so the road edge buries itself
                    // in the terrain instead of hovering over it.
                    verts[b + 0] = sample.position - right * (half + s.shoulderWidth) - sample.up * s.shoulderDrop;
                    verts[b + 1] = sample.position - right * half;
                    verts[b + 2] = sample.position + right * half;
                    verts[b + 3] = sample.position + right * (half + s.shoulderWidth) - sample.up * s.shoulderDrop;

                    uvs[b + 0] = new Vector2(0f, v);
                    uvs[b + 1] = new Vector2(0f, v);
                    uvs[b + 2] = new Vector2(1f, v);
                    uvs[b + 3] = new Vector2(1f, v);
                }
                else
                {
                    verts[b + 0] = sample.position - right * half;
                    verts[b + 1] = sample.position + right * half;
                    uvs[b + 0] = new Vector2(0f, v);
                    uvs[b + 1] = new Vector2(1f, v);
                }

                for (int k = 0; k < perRing; k++) normals[b + k] = sample.up;

                if (r == 0) continue;

                // Winding: columns run along +right, rows along +forward, so
                // (a, c, b) / (b, c, d) faces up. Same pattern as a terrain grid.
                int prev = (r - 1) * perRing;
                for (int k = 0; k < perRing - 1; k++)
                {
                    int a = prev + k, bb = prev + k + 1, c = b + k, d = b + k + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = bb;
                    tris[ti++] = bb; tris[ti++] = c; tris[ti++] = d;
                }
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = vertCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}