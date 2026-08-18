using UnityEngine;
using RallyGame.Races.Data;

namespace RallyGame.Races.Runtime
{
    /// Authoring helper: drag empty GameObjects around the world to lay out a route,
    /// then bake their transforms into a StageDefinition asset. Editor-only workflow,
    /// zero runtime cost - the baked asset is what ships.
    public class StageBaker : MonoBehaviour
    {
        [SerializeField] private StageDefinition target;
        [SerializeField] private Transform startGate;
        [SerializeField] private Transform finishGate;
        [Tooltip("Children of this transform, in order, become the checkpoints.")]
        [SerializeField] private Transform checkpointRoot;
        [SerializeField] private Vector2 defaultGateSize = new Vector2(14f, 6f);
        [SerializeField] private Color gizmoColor = Color.yellow;

#if UNITY_EDITOR
        [ContextMenu("Bake Into Stage Definition")]
        private void Bake()
        {
            if (!target) { Debug.LogError("[StageBaker] No target StageDefinition."); return; }

            target.startGate = Node(startGate);
            target.finishGate = Node(finishGate);
            target.checkpoints.Clear();

            if (checkpointRoot)
                foreach (Transform child in checkpointRoot)
                    target.checkpoints.Add(Node(child));

            target.lengthKm = EstimateLength() / 1000f;
            UnityEditor.EditorUtility.SetDirty(target);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"[StageBaker] Baked {target.checkpoints.Count} checkpoints into {target.name} ({target.lengthKm:0.00} km).");
        }

        [ContextMenu("Load From Stage Definition")]
        private void Load()
        {
            if (!target || !checkpointRoot) return;
            for (int i = checkpointRoot.childCount - 1; i >= 0; i--)
                DestroyImmediate(checkpointRoot.GetChild(i).gameObject);

            for (int i = 0; i < target.checkpoints.Count; i++)
            {
                var n = target.checkpoints[i];
                var go = new GameObject($"CP_{i:00}");
                go.transform.SetParent(checkpointRoot);
                go.transform.SetPositionAndRotation(n.position, Quaternion.Euler(n.eulerAngles));
            }
        }
#endif

        private StageDefinition.Node Node(Transform t) => t == null
            ? new StageDefinition.Node()
            : new StageDefinition.Node { position = t.position, eulerAngles = t.eulerAngles, size = defaultGateSize };

        private float EstimateLength()
        {
            float len = 0f;
            Vector3 prev = startGate ? startGate.position : transform.position;
            if (checkpointRoot)
                foreach (Transform c in checkpointRoot) { len += Vector3.Distance(prev, c.position); prev = c.position; }
            if (finishGate) len += Vector3.Distance(prev, finishGate.position);
            return len;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Vector3 prev = startGate ? startGate.position : transform.position;
            if (startGate) Gizmos.DrawWireCube(startGate.position + Vector3.up * 3f, new Vector3(defaultGateSize.x, defaultGateSize.y, 1f));

            if (checkpointRoot)
                foreach (Transform c in checkpointRoot)
                {
                    Gizmos.DrawLine(prev, c.position);
                    Gizmos.DrawWireCube(c.position + Vector3.up * 3f, new Vector3(defaultGateSize.x, defaultGateSize.y, 1f));
                    prev = c.position;
                }

            if (finishGate)
            {
                Gizmos.DrawLine(prev, finishGate.position);
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(finishGate.position + Vector3.up * 3f, new Vector3(defaultGateSize.x, defaultGateSize.y, 1f));
            }
        }
    }
}
