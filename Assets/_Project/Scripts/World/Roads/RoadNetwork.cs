using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;

namespace RallyGame.World.Roads
{
    /// A point on a road, in world space.
    public struct RoadPoint
    {
        public Vector3 position;
        public Vector3 forward;
        public float width;
        public float distance;      // metres from the query point
        public RoadSpline road;

        public bool IsValid => road != null;
    }

    /// Every RoadSpline in the scene, registered on enable. An asset rather than a
    /// scene singleton, for the same reason GarageState is: anything can reference it
    /// without a scene lookup and without a load-order problem.
    ///
    /// The query is a linear scan with a bounds reject. That is deliberate — a few
    /// hundred roads cost nothing at the rate this is actually called (respawns, not
    /// frames). If it ever runs per frame, put a grid in here and nothing else changes.
    [CreateAssetMenu(menuName = "Rally/State/Road Network", fileName = "RoadNetwork")]
    public class RoadNetwork : ScriptableObject
    {
        [System.NonSerialized] private readonly List<RoadSpline> roads = new List<RoadSpline>();

        public int RoadCount => roads.Count;

        private void OnEnable() { roads.Clear(); }

        public void Register(RoadSpline road)
        {
            if (road == null || roads.Contains(road)) return;
            roads.Add(road);
            GameLog.Verbose(LogCat.World, $"Road network <- '{road.name}' (now {roads.Count})", this);
        }

        public void Unregister(RoadSpline road)
        {
            if (roads.Remove(road))
                GameLog.Verbose(LogCat.World, $"Road network -> '{road.name}' (now {roads.Count})", this);
        }

        /// Closest point on any baked road centreline. maxDistance culls the search and
        /// is also the honest answer to "am I anywhere near a road at all".
        public bool TryFindNearest(Vector3 worldPosition, out RoadPoint result,
                                   float maxDistance = float.PositiveInfinity)
        {
            result = default;
            float best = maxDistance * maxDistance;
            bool found = false;

            foreach (var road in roads)
            {
                if (road == null || !road.HasBake) continue;

                // Cheap reject before touching the samples.
                if (maxDistance < float.PositiveInfinity &&
                    road.BakedBounds.SqrDistance(worldPosition) > best) continue;

                if (!NearestOnRoad(road, worldPosition, out var candidate)) continue;

                float sqr = candidate.distance * candidate.distance;
                if (sqr >= best) continue;

                best = sqr;
                result = candidate;
                found = true;
            }

            return found;
        }

        /// Projects onto each centreline segment rather than snapping to the nearest
        /// sample — otherwise a car between two 2 m-apart samples lands up to a metre
        /// off the road on respawn.
        private static bool NearestOnRoad(RoadSpline road, Vector3 p, out RoadPoint result)
        {
            result = default;
            var line = road.Centreline;
            if (line.Count < 2) return false;

            float bestSqr = float.MaxValue;
            Vector3 bestPos = Vector3.zero;
            Vector3 bestFwd = Vector3.forward;

            for (int i = 1; i < line.Count; i++)
            {
                Vector3 a = line[i - 1].position;
                Vector3 b = line[i].position;
                Vector3 ab = b - a;

                float lenSqr = ab.sqrMagnitude;
                if (lenSqr < 1e-6f) continue;

                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSqr);
                Vector3 proj = a + ab * t;

                float sqr = (p - proj).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                bestPos = proj;
                bestFwd = Vector3.Slerp(line[i - 1].forward, line[i].forward, t).normalized;
            }

            if (bestSqr == float.MaxValue) return false;

            result = new RoadPoint
            {
                position = bestPos,
                forward = bestFwd,
                width = road.Width,
                distance = Mathf.Sqrt(bestSqr),
                road = road
            };
            return true;
        }
    }
}