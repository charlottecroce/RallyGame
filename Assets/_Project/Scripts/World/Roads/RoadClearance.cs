using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.World.Roads
{
    /// "Is this point on the tarmac?" for a whole network, answered fast.
    ///
    /// Built once per clear pass from the baked samples of every strand, so a cone
    /// dropped on the shoulder of one spline is correctly found sitting in the middle
    /// of the spline it crosses — which is the entire reason junction props end up on
    /// the road in the first place.
    ///
    /// The test is done in the road's own frame rather than in world XZ: lateral
    /// distance across the ring, and height along the ring's up. That keeps a bridge
    /// deck from clearing the road underneath it.
    public sealed class RoadClearance
    {
        private readonly Vector3[] pts;
        private readonly Vector3[] ups;
        private readonly int[] next;          // index of the following sample, -1 at a strand end
        private readonly Dictionary<long, List<int>> grid;
        private readonly float cell;

        private const float AlongTolerance = 0.5f;   // how far past a strand end still counts

        public Bounds Bounds { get; }
        public int SampleCount => pts.Length;

        private RoadClearance(Vector3[] pts, Vector3[] ups, int[] next, float cell,
                              Dictionary<long, List<int>> grid, Bounds bounds)
        {
            this.pts = pts; this.ups = ups; this.next = next;
            this.cell = cell; this.grid = grid;
            Bounds = bounds;
        }

        /// cellSize must be at least the widest query radius, so a 3x3 cell lookup
        /// cannot miss a segment.
        public static RoadClearance Build(IReadOnlyList<List<RoadSample>> strands, float cellSize)
        {
            var pts = new List<Vector3>();
            var ups = new List<Vector3>();
            var next = new List<int>();

            for (int si = 0; si < strands.Count; si++)
            {
                var strand = strands[si];
                if (strand == null || strand.Count < 2) continue;

                int start = pts.Count;
                for (int i = 0; i < strand.Count; i++)
                {
                    pts.Add(strand[i].position);
                    ups.Add(strand[i].up);
                    next.Add(i < strand.Count - 1 ? start + i + 1 : -1);
                }
            }

            float cell = Mathf.Max(1f, cellSize);
            var grid = new Dictionary<long, List<int>>(pts.Count / 4 + 1);
            var bounds = new Bounds();

            for (int i = 0; i < pts.Count; i++)
            {
                if (i == 0) bounds = new Bounds(pts[0], Vector3.one);
                else bounds.Encapsulate(pts[i]);

                long key = Key(pts[i], cell);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>(8);
                list.Add(i);
            }

            return new RoadClearance(pts.ToArray(), ups.ToArray(), next.ToArray(), cell, grid, bounds);
        }

        /// halfWidth is the driving surface only — pass the road half-width minus an
        /// inset so the shoulder, where props are meant to live, is left alone.
        public bool IsOnRoad(Vector3 p, float halfWidth, float above, float below)
        {
            int cx = Cell(p.x, cell), cz = Cell(p.z, cell);

            for (int ox = -1; ox <= 1; ox++)
            for (int oz = -1; oz <= 1; oz++)
            {
                if (!grid.TryGetValue(Pack(cx + ox, cz + oz), out var list)) continue;

                for (int k = 0; k < list.Count; k++)
                {
                    int i = list[k];
                    int j = next[i];

                    Vector3 a = pts[i];
                    Vector3 b = j >= 0 ? pts[j] : a;
                    Vector3 ab = b - a;

                    float lenSqr = ab.sqrMagnitude;
                    Vector3 proj = a;
                    Vector3 fwd = ab;

                    if (lenSqr > 1e-6f)
                    {
                        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSqr);
                        proj = a + ab * t;
                        fwd = ab / Mathf.Sqrt(lenSqr);
                    }
                    else if (j < 0) continue;

                    Vector3 up = ups[i];
                    Vector3 across = Vector3.Cross(up, fwd);
                    if (across.sqrMagnitude < 1e-6f) continue;
                    across.Normalize();

                    Vector3 d = p - proj;

                    // Past the end of a strand the offset leaks into the forward axis;
                    // without this every object beyond the last ring reads as on-road.
                    if (Mathf.Abs(Vector3.Dot(d, fwd)) > AlongTolerance) continue;
                    if (Mathf.Abs(Vector3.Dot(d, across)) > halfWidth) continue;

                    float height = Vector3.Dot(d, up);
                    if (height > above || height < -below) continue;   // overpass, or far below

                    return true;
                }
            }

            return false;
        }

        /// Centreline points spaced at least `spacing` apart, for driving an overlap
        /// query along the road. Sweeping the ribbon beats one query on the bounding
        /// box, which for a winding road is mostly not road at all.
        public IEnumerable<Vector3> Sweep(float spacing)
        {
            float minSqr = Mathf.Max(0.5f, spacing) * Mathf.Max(0.5f, spacing);
            bool first = true;
            Vector3 last = Vector3.zero;

            for (int i = 0; i < pts.Length; i++)
            {
                if (!first && (pts[i] - last).sqrMagnitude < minSqr) continue;
                first = false;
                last = pts[i];
                yield return pts[i];
            }
        }

        private static int Cell(float v, float size) => Mathf.FloorToInt(v / size);
        private static long Pack(int x, int z) => ((long)x << 32) ^ (uint)z;
        private static long Key(Vector3 p, float size) => Pack(Cell(p.x, size), Cell(p.z, size));
    }
}