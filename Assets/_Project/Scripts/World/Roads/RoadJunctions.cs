using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.World.Roads
{
    /// A place where two strands of the same network meet or cross.
    [System.Serializable]
    public struct RoadJunction
    {
        public Vector3 position;
        public float radius;      // how far out of the junction props/keep-out reach
        public int strands;       // distinct splines involved; 2 for a T or a crossroads
    }

    public struct RoadJunctionSettings
    {
        public float joinDistance;     // two centrelines this close (in XZ) are touching
        public float maxHeightDelta;   // bigger vertical gap = a bridge, not a junction
        public float mergeRadius;      // candidates within this collapse into one junction
        public float radius;           // authored radius of the resulting junction
        public int minSampleGap;       // self-crossing guard along a single strand
    }

    /// Junction finding over the baked centrelines of one network.
    ///
    /// Everything works off samples rather than spline maths: a T junction is just an
    /// endpoint sitting on top of another strand, and a crossroads is two strands
    /// passing through the same metre of ground, so proximity is the whole test. The
    /// height tolerance is what keeps a bridge from being called a junction.
    public static class RoadJunctions
    {
        public static List<RoadJunction> Find(IReadOnlyList<List<RoadSample>> strands, in RoadJunctionSettings s)
        {
            var result = new List<RoadJunction>();
            if (strands == null || strands.Count == 0) return result;

            float join = Mathf.Max(0.5f, s.joinDistance);
            float joinSqr = join * join;
            int gap = Mathf.Max(2, s.minSampleGap);

            // Flatten every strand into one array so the grid can be built once.
            var pts = new List<Vector3>();
            var owner = new List<int>();
            var order = new List<int>();

            for (int si = 0; si < strands.Count; si++)
            {
                var strand = strands[si];
                if (strand == null) continue;
                for (int i = 0; i < strand.Count; i++)
                {
                    pts.Add(strand[i].position);
                    owner.Add(si);
                    order.Add(i);
                }
            }
            if (pts.Count < 2) return result;

            // Uniform grid at the join distance: 3x3 cells cover every possible pair,
            // which turns an O(n^2) sweep into something a 20 km network survives.
            var grid = new Dictionary<long, List<int>>(pts.Count / 4 + 1);
            for (int i = 0; i < pts.Count; i++)
            {
                long key = Key(pts[i], join);
                if (!grid.TryGetValue(key, out var cell)) grid[key] = cell = new List<int>(8);
                cell.Add(i);
            }

            var hitPos = new List<Vector3>();
            var hitA = new List<int>();
            var hitB = new List<int>();

            for (int i = 0; i < pts.Count; i++)
            {
                int cx = Cell(pts[i].x, join), cz = Cell(pts[i].z, join);

                for (int ox = -1; ox <= 1; ox++)
                for (int oz = -1; oz <= 1; oz++)
                {
                    if (!grid.TryGetValue(Pack(cx + ox, cz + oz), out var cell)) continue;

                    for (int c = 0; c < cell.Count; c++)
                    {
                        int j = cell[c];
                        if (j <= i) continue;

                        // On one strand only a real self-crossing counts, not the next
                        // sample two metres along.
                        if (owner[i] == owner[j] && Mathf.Abs(order[i] - order[j]) < gap) continue;

                        Vector3 a = pts[i], b = pts[j];
                        float dx = a.x - b.x, dz = a.z - b.z;
                        if (dx * dx + dz * dz > joinSqr) continue;
                        if (Mathf.Abs(a.y - b.y) > s.maxHeightDelta) continue;   // overpass

                        hitPos.Add((a + b) * 0.5f);
                        hitA.Add(owner[i]);
                        hitB.Add(owner[j]);
                    }
                }
            }

            if (hitPos.Count == 0) return result;

            // One junction produces dozens of touching pairs; collapse them into a
            // single averaged point per cluster.
            float merge = Mathf.Max(join, s.mergeRadius);
            float mergeSqr = merge * merge;

            var sum = new List<Vector3>();
            var count = new List<int>();
            var members = new List<HashSet<int>>();

            for (int h = 0; h < hitPos.Count; h++)
            {
                int found = -1;
                for (int c = 0; c < sum.Count; c++)
                {
                    Vector3 centre = sum[c] / count[c];
                    float dx = centre.x - hitPos[h].x, dz = centre.z - hitPos[h].z;
                    if (dx * dx + dz * dz <= mergeSqr) { found = c; break; }
                }

                if (found < 0)
                {
                    sum.Add(hitPos[h]);
                    count.Add(1);
                    members.Add(new HashSet<int>());
                    found = sum.Count - 1;
                }
                else
                {
                    sum[found] += hitPos[h];
                    count[found]++;
                }

                members[found].Add(hitA[h]);
                members[found].Add(hitB[h]);
            }

            for (int c = 0; c < sum.Count; c++)
                result.Add(new RoadJunction
                {
                    position = sum[c] / count[c],
                    radius = Mathf.Max(1f, s.radius),
                    strands = members[c].Count
                });

            return result;
        }

        /// XZ test against every junction's radius plus an extra margin. Horizontal
        /// only, so a road under a junction on a hill is not counted as inside it.
        public static bool IsNear(IReadOnlyList<RoadJunction> junctions, Vector3 point, float extraMargin)
        {
            if (junctions == null) return false;

            for (int i = 0; i < junctions.Count; i++)
            {
                float r = junctions[i].radius + extraMargin;
                float dx = junctions[i].position.x - point.x;
                float dz = junctions[i].position.z - point.z;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }

        private static int Cell(float v, float size) => Mathf.FloorToInt(v / size);
        private static long Pack(int x, int z) => ((long)x << 32) ^ (uint)z;
        private static long Key(Vector3 p, float size) => Pack(Cell(p.x, size), Cell(p.z, size));
    }
}