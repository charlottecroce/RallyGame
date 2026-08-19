using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Races.Data;

namespace RallyGame.Races.Runtime
{
    /// Spawns and scores a single stage: gates, timer, missed-checkpoint penalties.
    /// Owns no economy or scheduling logic - RaceManager sequences it.
    ///
    /// The timer ticks in Update and is never logged. Gates are discrete — a stage
    /// has tens of them, not thousands — so each one gets a line with its split.
    public class StageRunner : MonoBehaviour
    {
        [SerializeField] private LayerMask carLayer = ~0;

        [Header("Debug")]
        [Tooltip("Log every gate as it is passed, with the running split.")]
        [SerializeField] private bool logEveryGate = true;

        private readonly List<Checkpoint> spawned = new List<Checkpoint>();
        private StageDefinition stage;
        private List<StageDefinition.Node> route;
        private int nextIndex;
        private int missed;
        private float timer;
        private bool running;
        private float lastGateTime;

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
            lastGateTime = 0f;
            running = true;

            for (int i = 0; i < route.Count; i++)
            {
                var n = route[i];
                spawned.Add(Checkpoint.Spawn(transform, n.position, n.eulerAngles, n.size, i, OnGate, carLayer));
            }

            GameLog.Action(LogCat.Stage, "STAGE BEGIN",
                           $"'{stage.id}', {route.Count} gate(s) spawned, " +
                           $"penalty {stage.missedCheckpointPenalty:0.#}s per missed gate", this);
        }

        private void Update() { if (running) timer += Time.deltaTime; }

        private void OnGate(int index)
        {
            if (!running)
            {
                GameLog.Verbose(LogCat.Stage, $"Gate {index} triggered after the stage ended — ignored.", this);
                return;
            }
            if (index < nextIndex)
            {
                GameLog.Verbose(LogCat.Stage, $"Gate {index} re-entered (already passed, next is {nextIndex}) — ignored.", this);
                return;
            }

            int skipped = index - nextIndex;
            if (skipped > 0)
            {
                // Skipping gates is allowed but penalised, so a bad line does not soft-lock a stage.
                GameLog.Action(LogCat.Stage, $"MISSED {skipped} gate(s)",
                               $"jumped from expected {nextIndex} to {index}, " +
                               $"+{skipped * stage.missedCheckpointPenalty:0.#}s penalty", this);
            }

            missed += skipped;
            nextIndex = index + 1;

            if (logEveryGate)
            {
                float split = timer - lastGateTime;
                GameLog.Action(LogCat.Stage, $"Gate {index + 1}/{route.Count}",
                               $"t={timer:0.00}s, split {split:0.00}s, missed so far {missed}", this);
            }
            lastGateTime = timer;

            OnCheckpointPassed?.Invoke(index, route.Count);

            if (nextIndex >= route.Count) Finish(false);
        }

        public void Finish(bool dnf)
        {
            if (!running)
            {
                GameLog.Verbose(LogCat.Stage, "Finish() called while not running — ignored.", this);
                return;
            }
            running = false;

            var result = new StageResult
            {
                stageId = stage.id,
                rawTimeSeconds = timer,
                missedCheckpoints = missed,
                penaltySeconds = missed * stage.missedCheckpointPenalty,
                didNotFinish = dnf
            };

            GameLog.Action(LogCat.Stage, dnf ? "STAGE DNF" : "STAGE FINISHED",
                           $"'{result.stageId}' raw {result.rawTimeSeconds:0.00}s " +
                           $"+ penalty {result.penaltySeconds:0.00}s ({result.missedCheckpoints} missed) " +
                           $"= total {result.TotalSeconds:0.00}s", this);

            Cleanup();
            OnStageFinished?.Invoke(result);
        }

        public void Cleanup()
        {
            if (spawned.Count > 0)
                GameLog.Verbose(LogCat.Stage, $"Cleaning up {spawned.Count} gate object(s).", this);

            foreach (var cp in spawned) if (cp) Destroy(cp.gameObject);
            spawned.Clear();
            running = false;
        }

        public Vector3 StartPosition => route != null && route.Count > 0 ? route[0].position : transform.position;
        public Quaternion StartRotation => route != null && route.Count > 0 ? Quaternion.Euler(route[0].eulerAngles) : Quaternion.identity;
    }
}
