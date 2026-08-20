using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace RallyGame.World.Roads
{
    /// One cross-section of road: where it sits, which way it runs, which way is up,
    /// and how far each shoulder has to reach down to meet the ground.
    [System.Serializable]
    public struct RoadSample
    {
        public Vector3 position;
        public Vector3 forward;
        public Vector3 up;
        public float distance;     // metres from the start of the spline

        // ---- skirt profile, per side ----
        // The skirt drops from the shoulder edge in up to two segments: a near-
        // vertical "wall" up to skirtMaxDrop, then — if that alone doesn't reach the
        // ground — a "ramp" that spreads outward instead of continuing straight down.
        public float skirtLeft;         // wall drop, left (capped at skirtMaxDrop)
        public float skirtRight;        // wall drop, right
        public float skirtSpreadLeft;   // extra horizontal run of the ramp, left (0 = no ramp needed)
        public float skirtSpreadRight;  // extra horizontal run of the ramp, right
        public float skirtFarDropLeft;  // TOTAL drop from the shoulder edge to the ramp's outer vertex, left
        public float skirtFarDropRight; // TOTAL drop from the shoulder edge to the ramp's outer vertex, right
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
        public float bankBlend;         // 0 = never bank, 1 = follow the cross-slope

        // ---- camber ----
        public float maxCamberDegrees;  // hard ceiling on how far the road rolls

        // ---- skirt ----
        public float skirtBurial;        // how far under the terrain the skirt's ground vertex sinks
        public float skirtMaxDrop;       // vertical wall height before the skirt battens outward instead of digging deeper
        public float skirtBatterSlope;   // horizontal run per extra metre of drop once skirtMaxDrop is hit. 0 = old capped-wall behaviour (can leave a gap)
        public float skirtUvTilesAcross; // skirt material's texture repeats across its own width (wall + ramp)
    }

    /// One mesh chunk: the road surface, and — when the road has a shoulder — the
    /// embankment skirt that runs under and beside it. Skirt is null when there's no
    /// shoulder to skirt (shoulderWidth <= 0), so callers can check for that directly.
    public struct RoadMeshChunk
    {
        public Mesh Road;
        public Mesh Skirt;
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
        /// Four things are done here that a single centreline raycast cannot do, and
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
        ///     sag, which removes the "road disappears over the brow" artefact.
        ///  3. A vertical relaxation pass. Following every terrain wrinkle is what
        ///     makes the surface feel bumpy to drive. Heights are smoothed toward their
        ///     neighbours, then clamped back up against the clearances from (1) and (2)
        ///     — smoothing can only ever give back height, never push the road under.
        ///  4. A camber limit. Copying a steep hillside's cross-slope makes a road no
        ///     one can drive, so the roll is clamped. Past the limit the road stops
        ///     tilting and instead sits proud of the downhill side, and the shoulder
        ///     skirt is extended downwards to reach the ground — a cut-and-fill shelf,
        ///     which is what a real road does there.
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

            // ---- pass 3: orthonormal frames, arc length, and the skirt profile ----
            Vector3 previous = Vector3.zero;
            float travelled = 0f;
            float outer = s.width * 0.5f + s.shoulderWidth;

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

                float dropL = s.shoulderDrop, dropR = s.shoulderDrop;
                float spreadL = 0f, spreadR = 0f;
                float farDropL = s.shoulderDrop, farDropR = s.shoulderDrop;

                if (s.conformToGround && s.shoulderWidth > 0.001f)
                {
                    SkirtProfile(pos[i] - right * outer, -right, s, out dropL, out spreadL, out farDropL);
                    SkirtProfile(pos[i] + right * outer, right, s, out dropR, out spreadR, out farDropR);
                }

                samples.Add(new RoadSample
                {
                    position = pos[i],
                    forward = f,
                    up = u,
                    distance = travelled,
                    skirtLeft = dropL,
                    skirtRight = dropR,
                    skirtSpreadLeft = spreadL,
                    skirtSpreadRight = spreadR,
                    skirtFarDropLeft = farDropL,
                    skirtFarDropRight = farDropR
                });
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

            // The zero-camber reference: right held horizontal, so this frame is
            // pitched with the road but not rolled at all.
            Vector3 flatRight = Vector3.Cross(Vector3.up, fwd);
            if (flatRight.sqrMagnitude < 1e-6f) flatRight = right;
            flatRight.Normalize();
            Vector3 flatUp = Vector3.Cross(fwd, flatRight).normalized;

            // The line through the outermost hits is the ground the bar would lie along
            // if it copied the hillside exactly. Measure that as a roll about the
            // forward axis, scale it by the bank blend, then clamp it: past the limit
            // the road stops leaning and starts standing proud of the downhill side.
            float roll = 0f;
            if (hi > lo)
            {
                Vector3 across = probePoint[hi] - probePoint[lo];
                Vector3 terrainUp = Vector3.Cross(fwd, across);
                if (terrainUp.y < 0f) terrainUp = -terrainUp;

                if (terrainUp.sqrMagnitude > 1e-6f)
                    roll = Vector3.SignedAngle(flatUp, terrainUp.normalized, fwd) * Mathf.Clamp01(s.bankBlend);
            }

            float limit = Mathf.Max(0f, s.maxCamberDegrees);
            roll = Mathf.Clamp(roll, -limit, limit);
            up = (Quaternion.AngleAxis(roll, fwd) * flatUp).normalized;

            Vector3 fitRight = Vector3.Cross(up, fwd);
            if (fitRight.sqrMagnitude < 1e-6f) fitRight = right;
            fitRight.Normalize();

            // Given that tilt, the road surface at lateral offset d sits at
            // centreY + fitRight.y * d, so solve each probe for the centre height it
            // demands and keep the worst one. On a clamped cross-slope this is what
            // lifts the whole ring until the uphill edge is clear.
            for (int i = 0; i < n; i++)
            {
                if (!probeFound[i]) continue;
                float need = probePoint[i].y + s.heightOffset - fitRight.y * probeOffset[i];
                if (need > requiredY) requiredY = need;
            }

            centreY = probePoint[mid].y + s.heightOffset;
            return true;
        }

        /// How far the shoulder edge has to fall to meet the ground beneath it, split
        /// into a near-vertical wall (up to skirtMaxDrop) and, if that alone isn't
        /// enough, a batter that spreads outward at skirtBatterSlope run-per-drop until
        /// it actually finds ground. This is the fix for the road-floats-on-a-camber
        /// bug: the old version simply clamped the wall's height and stopped, which on
        /// a clamped or partial camber left the wall short of the terrain — a visible
        /// gap. Here the outer vertex always ends up at the real ground height (minus
        /// burial); only its shape (tall wall vs. wide ramp) changes.
        private static void SkirtProfile(Vector3 edge, Vector3 outward, in RoadBuildSettings s,
                                         out float wallDrop, out float spread, out float farDrop)
        {
            wallDrop = s.shoulderDrop;
            spread = 0f;
            farDrop = s.shoulderDrop;

            if (!Ground(edge, s, out Vector3 hit)) return;   // no ground found: leave the authored default

            float needed = Mathf.Max(s.shoulderDrop, edge.y - (hit.y - Mathf.Max(0f, s.skirtBurial)));
            float cap = Mathf.Max(s.shoulderDrop, s.skirtMaxDrop);

            if (needed <= cap)
            {
                wallDrop = needed;
                farDrop = needed;
                return;
            }

            // Past the cap, stop digging straight down and batten outward instead —
            // a shallow ramp reaches the same ground without a cliff-height wall.
            wallDrop = cap;

            float run = (needed - cap) * Mathf.Max(0f, s.skirtBatterSlope);
            if (run < 0.01f) { farDrop = needed; return; }   // batter disabled: dig straight down, same as before

            Vector3 far = edge + outward * run;
            if (Ground(far, s, out Vector3 farHit))
            {
                farDrop = edge.y - (farHit.y - Mathf.Max(0f, s.skirtBurial));
                spread = run;
            }
            else
            {
                // Ground vanished past the ramp (a cliff edge, a hole) — stop where we
                // last had solid footing rather than reaching for nothing.
                farDrop = cap;
            }

            farDrop = Mathf.Max(farDrop, wallDrop);   // the ramp never climbs back above the wall
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

        /// Ribbon-extrude the samples into one or more mesh chunks. Each chunk is the
        /// road surface plus — when the road has a shoulder — a separate skirt mesh
        /// for the embankment underneath it, so the two can carry different materials
        /// (tarmac on top, gravel/dirt on the skirt) and the skirt can be parented
        /// visibly "under" the road in the hierarchy.
        ///
        /// Chunking is not an optimisation afterthought — a 5 km road at 2 m spacing is
        /// 2500 rings, and one mesh that size culls as a single unit and blows past the
        /// 16-bit index limit. Consecutive chunks share their boundary ring, so there
        /// is no seam.
        public static List<RoadMeshChunk> Build(List<RoadSample> samples, bool closed, in RoadBuildSettings s, string name)
        {
            var chunks = new List<RoadMeshChunk>();
            if (samples == null || samples.Count < 2) return chunks;

            bool skirt = s.shoulderWidth > 0.001f;
            int ringsPerChunk = Mathf.Max(2, s.maxRingsPerChunk);

            int total = closed ? samples.Count + 1 : samples.Count;   // closed loops repeat ring 0
            int chunk = 0;

            for (int start = 0; start < total - 1; start += ringsPerChunk - 1)
            {
                int end = Mathf.Min(start + ringsPerChunk - 1, total - 1);
                string chunkName = $"{name}_Chunk{chunk:00}";

                var road = BuildRoadMesh(samples, start, end, s, chunkName);
                var skirtMesh = skirt ? BuildSkirtMesh(samples, start, end, s, chunkName + "_Skirt") : null;

                chunks.Add(new RoadMeshChunk { Road = road, Skirt = skirtMesh });
                chunk++;
            }

            return chunks;
        }

        /// The drivable surface only — two verts per ring, unchanged shape whether or
        /// not the road has a shoulder. The skirt (if any) is a separate mesh built by
        /// BuildSkirtMesh below.
        private static Mesh BuildRoadMesh(List<RoadSample> samples, int start, int end,
                                          in RoadBuildSettings s, string name)
        {
            int ringCount = end - start + 1;
            int vertCount = ringCount * 2;

            var verts = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var tris = new int[(ringCount - 1) * 6];

            float half = s.width * 0.5f;
            int ti = 0;

            for (int r = 0; r < ringCount; r++)
            {
                var sample = samples[(start + r) % samples.Count];
                Vector3 right = Vector3.Cross(sample.up, sample.forward).normalized;
                float v = sample.distance * s.uvTilesPerMetre;
                int b = r * 2;

                verts[b + 0] = sample.position - right * half;
                verts[b + 1] = sample.position + right * half;
                uvs[b + 0] = new Vector2(0f, v);
                uvs[b + 1] = new Vector2(1f, v);
                normals[b + 0] = sample.up;
                normals[b + 1] = sample.up;

                if (r == 0) continue;

                int prev = (r - 1) * 2;
                AddQuad(tris, ref ti, prev + 0, prev + 1, b + 0, b + 1);
            }

            return FinishMesh(verts, normals, uvs, tris, name);
        }

        /// The embankment mesh: for each side, a wall segment (shoulder edge down to
        /// skirtMaxDrop) and a ramp segment (spreading outward the rest of the way to
        /// the ground). The two columns nearest the road (index 2 and 3) sit at exactly
        /// the road mesh's own edge positions, so the two meshes share an edge with no
        /// seam — but the quad BETWEEN them is deliberately skipped, since that patch
        /// of ground is already covered by the road surface sitting directly above it.
        private static Mesh BuildSkirtMesh(List<RoadSample> samples, int start, int end,
                                           in RoadBuildSettings s, string name)
        {
            int ringCount = end - start + 1;
            const int perRing = 6;   // far-left, bend-left, top-left, top-right, bend-right, far-right
            int vertCount = ringCount * perRing;

            var verts = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var tris = new int[(ringCount - 1) * 4 * 6];   // 4 quads per ring step (2-3 is skipped)

            float half = s.width * 0.5f;
            float outer = half + s.shoulderWidth;
            float uScale = s.skirtUvTilesAcross;
            int ti = 0;

            for (int r = 0; r < ringCount; r++)
            {
                var sample = samples[(start + r) % samples.Count];
                Vector3 right = Vector3.Cross(sample.up, sample.forward).normalized;
                Vector3 up = sample.up;
                float v = sample.distance * s.uvTilesPerMetre;
                int b = r * perRing;

                Vector3 roadLeft = sample.position - right * half;
                Vector3 roadRight = sample.position + right * half;
                Vector3 leftBend = sample.position - right * outer - up * sample.skirtLeft;
                Vector3 rightBend = sample.position + right * outer - up * sample.skirtRight;
                Vector3 leftFar = leftBend - right * sample.skirtSpreadLeft
                                            - up * (sample.skirtFarDropLeft - sample.skirtLeft);
                Vector3 rightFar = rightBend + right * sample.skirtSpreadRight
                                              - up * (sample.skirtFarDropRight - sample.skirtRight);

                verts[b + 0] = leftFar;
                verts[b + 1] = leftBend;
                verts[b + 2] = roadLeft;
                verts[b + 3] = roadRight;
                verts[b + 4] = rightBend;
                verts[b + 5] = rightFar;

                uvs[b + 0] = new Vector2(-(outer + sample.skirtSpreadLeft) * uScale, v);
                uvs[b + 1] = new Vector2(-outer * uScale, v);
                uvs[b + 2] = new Vector2(-half * uScale, v);
                uvs[b + 3] = new Vector2(half * uScale, v);
                uvs[b + 4] = new Vector2(outer * uScale, v);
                uvs[b + 5] = new Vector2((outer + sample.skirtSpreadRight) * uScale, v);

                Vector3 outwardL = -right;
                Vector3 outwardR = right;
                Vector3 nTopBendL = SegmentNormal(sample.skirtLeft, s.shoulderWidth, outwardL, up);
                Vector3 nBendFarL = SegmentNormal(sample.skirtFarDropLeft - sample.skirtLeft, sample.skirtSpreadLeft, outwardL, up);
                Vector3 nTopBendR = SegmentNormal(sample.skirtRight, s.shoulderWidth, outwardR, up);
                Vector3 nBendFarR = SegmentNormal(sample.skirtFarDropRight - sample.skirtRight, sample.skirtSpreadRight, outwardR, up);

                normals[b + 0] = nBendFarL;
                normals[b + 1] = Vector3.Slerp(nTopBendL, nBendFarL, 0.5f).normalized;
                normals[b + 2] = up;
                normals[b + 3] = up;
                normals[b + 4] = Vector3.Slerp(nTopBendR, nBendFarR, 0.5f).normalized;
                normals[b + 5] = nBendFarR;

                if (r == 0) continue;

                int prev = (r - 1) * perRing;
                AddQuad(tris, ref ti, prev + 0, prev + 1, b + 0, b + 1);   // left ramp
                AddQuad(tris, ref ti, prev + 1, prev + 2, b + 1, b + 2);   // left wall
                // (2, 3) skipped — the road mesh already covers that span from above.
                AddQuad(tris, ref ti, prev + 3, prev + 4, b + 3, b + 4);   // right wall
                AddQuad(tris, ref ti, prev + 4, prev + 5, b + 4, b + 5);   // right ramp
            }

            return FinishMesh(verts, normals, uvs, tris, name);
        }

        /// 0 for a shallow ramp (mostly facing up, like ground), approaching 1 for a
        /// near-vertical wall (facing outward). Driven by the segment's own rise-over-
        /// run rather than a fixed constant, so it looks right whether the segment ends
        /// up as a tall cliff-face or a wide gravel batter.
        private static Vector3 SegmentNormal(float verticalDrop, float horizontalRun, Vector3 outward, Vector3 up)
        {
            float lean = Mathf.Clamp01(verticalDrop / Mathf.Max(0.05f, horizontalRun + verticalDrop));
            Vector3 dir = outward.sqrMagnitude > 1e-6f ? outward.normalized : up;
            Vector3 n = Vector3.Slerp(up, dir, lean);
            return n.sqrMagnitude > 1e-6f ? n.normalized : up;
        }

        private static void AddQuad(int[] tris, ref int ti, int a, int b, int c, int d)
        {
            // Winding: (a, c, b) / (b, c, d) faces up, matching a terrain grid.
            tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
            tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
        }

        private static Mesh FinishMesh(Vector3[] verts, Vector3[] normals, Vector2[] uvs, int[] tris, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = verts.Length > 65000
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