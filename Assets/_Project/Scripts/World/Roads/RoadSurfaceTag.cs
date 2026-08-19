using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.World.Roads
{
    /// Put on generated road mesh objects by RoadSpline. The car asks "what am I
    /// driving on" by looking at the collider its wheel hit and calling Resolve.
    ///
    /// A raycast answer beats a spline distance query here: it costs nothing extra
    /// (the WheelCollider already reports its ground hit), and it is correct on
    /// bridges, in tunnels and anywhere two roads cross.
    public class RoadSurfaceTag : MonoBehaviour
    {
        [SerializeField] private RoadSurface surface;

        public RoadSurface Surface => surface;

        public void SetSurface(RoadSurface s) => surface = s;

        // ---- lookup --------------------------------------------------------

        /// Collider -> surface, cached. Wheels hit the same few colliders thousands of
        /// times, so GetComponentInParent must not run per wheel per physics step.
        private static readonly Dictionary<Collider, RoadSurface> cache = new Dictionary<Collider, RoadSurface>(64);

        public static RoadSurface Resolve(Collider collider)
        {
            if (collider == null) return null;
            if (cache.TryGetValue(collider, out var cached)) return cached;

            var tag = collider.GetComponentInParent<RoadSurfaceTag>();
            var found = tag ? tag.Surface : null;
            cache[collider] = found;                       // null is a valid, cacheable answer
            return found;
        }

        /// Call after rebuilding roads at runtime — destroyed colliders leave dead keys.
        public static void ClearCache() => cache.Clear();

        private void OnDestroy() { cache.Clear(); }
    }
}