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
    /// twelve arguments.
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
    }

    /// Pure functions: spline in, samples and meshes out. No MonoBehaviour, no scene
    /// state, so it runs identically in the editor and at runtime and can be unit
    /// tested. RoadSpline is the only thing that knows about GameObjects.
    public static class RoadMeshBuilder
    {
        /// Walk the spline at a fixed spacing and drop each sample onto the ground.
        /// missedProbes reports how many samples found no ground — a non-zero count
        /// almost always means the terrain is not on the ground mask.
        public static List<RoadSample> Sample(SplineContainer container, int splineIndex,
                                              in RoadBuildSettings s, out int missedProbes)
        {
            missedProbes = 0;
            var samples = new List<RoadSample>();

            var spline = container.Splines[splineIndex];
            if (spline == null || spline.Count < 2) return samples;

            float length = container.CalculateLength(splineIndex);
            if (length <= 0.01f) return samples;

            int steps = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.25f, s.metresPerSample)));
            int rings = spline.Closed ? steps : steps + 1;

            Vector3 previous = Vector3.zero;
            float travelled = 0f;

            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / steps;
                container.Evaluate(splineIndex, t, out float3 p, out float3 tan, out float3 upv);

                Vector3 pos = p;
                Vector3 fwd = ((Vector3)tan).sqrMagnitude > 1e-6f ? ((Vector3)tan).normalized : Vector3.forward;
                Vector3 up = ((Vector3)upv).sqrMagnitude > 1e-6f ? ((Vector3)upv).normalized : Vector3.up;

                if (s.conformToGround)
                {
                    var origin = pos + Vector3.up * s.probeUp;
                    if (UnityEngine.Physics.Raycast(origin, Vector3.down, out var hit,
                                                    s.probeUp + s.probeDown, s.groundMask,
                                                    QueryTriggerInteraction.Ignore))
                    {
                        pos = hit.point;
                        up = hit.normal;                       // road banks with the hillside
                    }
                    else missedProbes++;
                }

                pos += up * s.heightOffset;

                // Re-orthogonalise: the ground normal and the spline tangent are not
                // perpendicular, and a skewed frame twists the ribbon.
                Vector3 right = Vector3.Cross(up, fwd);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, fwd);
                right.Normalize();
                fwd = Vector3.Cross(right, up).normalized;

                if (i > 0) travelled += Vector3.Distance(pos, previous);
                previous = pos;

                samples.Add(new RoadSample { position = pos, forward = fwd, up = up, distance = travelled });
            }

            return samples;
        }

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