using System.Collections.Generic;
using UnityEngine;
using RallyGame.Races.Data;

namespace RallyGame.Races.Runtime
{
    /// Spawns and scores a single stage: gates, timer, missed-checkpoint penalties.
    /// Owns no economy or scheduling logic - RaceManager sequences it.
    public class StageRunner : MonoBehaviour
    {
        [SerializeField] private LayerMask carLayer = ~0;

        private readonly List<Checkpoint> spawned = new List<Checkpoint>();
        private StageDefinition stage;
        private List<StageDefinition.Node> route;
        private int nextIndex;
        private int missed;
        private float timer;
        private bool running;

        public System.Action<StageResult> OnStageFinished;
        public System.Action<int, int> OnCheckpointPassed;   // (passedIndex, total)

        public float Timer => timer;
        public int NextCheckpoint => nextIndex;
        public int TotalCheckpoints => route?.Count ?? 0;
        public bool Running => running;

        public void Begin(StageDefinition definition)
        {
            Cleanup();
            stage = definition;
            route = stage.RunOrder();
            nextIndex = 1;      // index 0 is the start gate the car is already sitting on
            missed = 0;
            timer = 0f;
            running = true;

            for (int i = 0; i < route.Count; i++)
            {
                var n = route[i];
                spawned.Add(Checkpoint.Spawn(transform, n.position, n.eulerAngles, n.size, i, OnGate, carLayer));
            }
        }

        private void Update() { if (running) timer += Time.deltaTime; }

        private void OnGate(int index)
        {
            if (!running || index < nextIndex) return;

            // Skipping gates is allowed but penalised, so a bad line does not soft-lock a stage.
            missed += index - nextIndex;
            nextIndex = index + 1;
            OnCheckpointPassed?.Invoke(index, route.Count);

            if (nextIndex >= route.Count) Finish(false);
        }

        public void Finish(bool dnf)
        {
            if (!running) return;
            running = false;

            var result = new StageResult
            {
                stageId = stage.id,
                rawTimeSeconds = timer,
                missedCheckpoints = missed,
                penaltySeconds = missed * stage.missedCheckpointPenalty,
                didNotFinish = dnf
            };

            Cleanup();
            OnStageFinished?.Invoke(result);
        }

        public void Cleanup()
        {
            foreach (var cp in spawned) if (cp) Destroy(cp.gameObject);
            spawned.Clear();
            running = false;
        }

        public Vector3 StartPosition => route != null && route.Count > 0 ? route[0].position : transform.position;
        public Quaternion StartRotation => route != null && route.Count > 0 ? Quaternion.Euler(route[0].eulerAngles) : Quaternion.identity;
    }
}
