using UnityEngine;

namespace RallyGame.Races.Runtime
{
    /// Spawned by StageRunner from StageDefinition nodes. No authored scene objects,
    /// so stages can be re-baked without touching the level.
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
            if (other.attachedRigidbody == null) return;
            onPassed?.Invoke(Index);
        }
    }
}
