using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;

namespace RallyGame.World.Physics
{
    /// Authoring tool. Drop it on the parent of your scenery, right-click the
    /// component header, and it stamps the CollisionProfile onto every collider
    /// underneath. Zero runtime cost - it does nothing outside the editor.
    ///
    /// "Report Overlaps" answers the other half of the stuck-car question: two props
    /// whose colliders intersect form a concave pocket, and a car that noses into one
    /// has no single direction PhysX can push it out along. Those are the spots that
    /// eat cars.
    public class PropColliderSetup : MonoBehaviour
    {
        [SerializeField] private CollisionProfile profile;
        [Tooltip("Root to process. Falls back to this object.")]
        [SerializeField] private Transform root;
        [Tooltip("Also move the props onto the profile's layer.")]
        [SerializeField] private bool assignLayer = true;
        [Tooltip("Mark props static. Big win for physics and batching, but only correct " +
                 "if nothing here ever moves.")]
        [SerializeField] private bool markStatic = true;

        [Header("Overlap report")]
        [Tooltip("Colliders closer than this count as overlapping. Slight negative padding " +
                 "avoids flagging props that merely touch.")]
        [SerializeField] private float overlapPadding = -0.05f;
        [Tooltip("Stop listing after this many, so the console stays readable.")]
        [SerializeField] private int maxOverlapsReported = 40;

        private Transform Root => root ? root : transform;

#if UNITY_EDITOR
        [ContextMenu("Apply Profile To Children")]
        private void Apply()
        {
            if (!profile) { GameLog.Error(LogCat.World, "PropColliderSetup has no CollisionProfile.", this); return; }

            var colliders = Root.GetComponentsInChildren<Collider>(true);
            int layer = profile.PropLayer;

            if (assignLayer && layer < 0)
                GameLog.Warn(LogCat.World,
                    $"Layer '{profile.propLayerName}' does not exist — create it in Project Settings > Tags and Layers, " +
                    "or clear the name on the profile. Skipping layer assignment.", this);

            int stamped = 0;
            foreach (var col in colliders)
            {
                if (col.isTrigger) continue;

                UnityEditor.Undo.RecordObject(col, "Apply Collision Profile");
                col.sharedMaterial = profile.propMaterial;
                stamped++;

                if (assignLayer && layer >= 0 && col.gameObject.layer != layer)
                {
                    UnityEditor.Undo.RecordObject(col.gameObject, "Apply Collision Profile");
                    col.gameObject.layer = layer;
                }

                if (markStatic && !col.gameObject.isStatic)
                {
                    UnityEditor.Undo.RecordObject(col.gameObject, "Apply Collision Profile");
                    col.gameObject.isStatic = true;
                }
            }

            GameLog.Action(LogCat.World, "Prop colliders stamped",
                           $"{stamped} collider(s) under '{Root.name}' -> " +
                           $"'{(profile.propMaterial ? profile.propMaterial.name : "<none>")}'" +
                           $"{(assignLayer && layer >= 0 ? $", layer '{profile.propLayerName}'" : "")}", this);
        }

        [ContextMenu("Report Overlaps")]
        private void ReportOverlaps()
        {
            var colliders = Root.GetComponentsInChildren<Collider>(true);
            var seen = new HashSet<long>();
            var sb = new System.Text.StringBuilder();
            int found = 0;

            for (int i = 0; i < colliders.Length && found < maxOverlapsReported; i++)
            {
                var a = colliders[i];
                if (!a || a.isTrigger) continue;

                var b = a.bounds;
                var hits = UnityEngine.Physics.OverlapBox(
                    b.center, b.extents + Vector3.one * overlapPadding,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

                foreach (var other in hits)
                {
                    if (other == a || !other || other.isTrigger) continue;
                    if (other.transform.IsChildOf(a.transform) || a.transform.IsChildOf(other.transform)) continue;

                    long key = Pair(a.GetHashCode(), other.GetHashCode());
                    if (!seen.Add(key)) continue;

                    if (!UnityEngine.Physics.ComputePenetration(
                            a, a.transform.position, a.transform.rotation,
                            other, other.transform.position, other.transform.rotation,
                            out _, out float depth)) continue;
                    if (depth <= 0.01f) continue;

                    found++;
                    sb.AppendLine($"  {depth:0.00} m  '{a.name}' <-> '{other.name}'  at {a.bounds.center:0.0}");
                    if (found >= maxOverlapsReported) break;
                }
            }

            if (found == 0)
                GameLog.Action(LogCat.World, "Overlap scan clean",
                               $"{colliders.Length} collider(s) under '{Root.name}', no interpenetration", this);
            else
                GameLog.Warn(LogCat.World,
                    $"{found} overlapping prop collider pair(s) under '{Root.name}'. These form pockets a car " +
                    $"cannot be pushed out of — separate or merge them:\n{sb}", this);
        }

        private static long Pair(int x, int y) => x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
#endif
    }
}