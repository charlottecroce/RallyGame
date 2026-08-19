using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Races.Runtime
{
    /// Spawned by StageRunner from StageDefinition nodes. No authored scene objects,
    /// so stages can be re-baked without touching the level.
    ///
    /// The rejection branch is logged: "gates stopped registering" after a prefab or
    /// layer change is otherwise completely invisible.
    [RequireComponent(typeof(BoxCollider))]
    public class Checkpoint : MonoBehaviour
    {
        public int Index { get; private set; }
        private System.Action<int> onPassed;

        public static Checkpoint Spawn(Transform parent, Vector3 pos, Vector3 euler, Vector2 size,
                                       int index, System.Action<int> callback, LayerMask carLayer)
        {
            var go = new GameObject($"Checkpoint_{index}");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, 2f);
            box.center = new Vector3(0f, size.y * 0.5f, 0f);

            var cp = go.AddComponent<Checkpoint>();
            cp.Index = index;
            cp.onPassed = callback;
            return cp;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Car root carries the Rigidbody; child colliders resolve up to it.
            if (other.attachedRigidbody == null)
            {
                GameLog.Verbose(LogCat.Stage,
                    $"Gate {Index}: '{other.name}' passed through but has no attached Rigidbody — ignored. " +
                    "Only the car (whose root has the Rigidbody) can trigger gates.", this);
                return;
            }

            onPassed?.Invoke(Index);
        }
    }
}
