using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.Races.Data
{
    /// A single stage: an ordered spline of checkpoints baked into the asset, plus
    /// its reverse twin. Nothing is looked up in the scene at runtime.
    [CreateAssetMenu(menuName = "Rally/Definitions/Stage", fileName = "Stage_")]
    public class StageDefinition : ScriptableObject
    {
        [System.Serializable]
        public class Node
        {
            public Vector3 position;
            public Vector3 eulerAngles;
            [Tooltip("Gate width/height in metres.")]
            public Vector2 size = new Vector2(14f, 6f);
        }

        [Header("Identity")]
        public string id = "Stage_New";
        public string displayName = "New Stage";
        public string locationId;
        [Tooltip("Reverse running of the same route (GDD: 4 stages x 2 directions).")]
        public bool isReverse;

        [Header("Route")]
        public Node startGate;
        public List<Node> checkpoints = new List<Node>();
        public Node finishGate;

        [Header("Balance")]
        [Tooltip("Reference time in seconds for a competent run in a stock car.")]
        public float parTimeSeconds = 180f;
        public float lengthKm = 4f;
        [Tooltip("Seconds added per missed checkpoint.")]
        public float missedCheckpointPenalty = 10f;

        /// Route in running order. Reverse stages walk the same nodes backwards,
        /// so one authored route yields two stages.
        public List<Node> RunOrder()
        {
            var list = new List<Node>();
            list.Add(isReverse ? finishGate : startGate);
            if (isReverse) { for (int i = checkpoints.Count - 1; i >= 0; i--) list.Add(checkpoints[i]); }
            else list.AddRange(checkpoints);
            list.Add(isReverse ? startGate : finishGate);
            return list;
        }

        private void OnValidate() { if (string.IsNullOrEmpty(id)) id = name; }
    }
}
