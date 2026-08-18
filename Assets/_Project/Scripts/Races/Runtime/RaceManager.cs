using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Economy;
using RallyGame.Races.Data;
using RallyGame.Vehicles.Controllers;

namespace RallyGame.Races.Runtime
{
    /// Sequences an event's stages inside the one persistent world scene: toggles race
    /// mode, teleports the car to each start, runs service windows, pays out.
    public class RaceManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RaceState raceState;
        [SerializeField] private StageRunner stageRunner;
        [SerializeField] private CarSpawner carSpawner;
        [SerializeField] private EconomyService economy;
        [SerializeField] private DefinitionDatabase database;
        [SerializeField] private GameClock clock;
        [SerializeField] private BoolVariable clockPaused;

        [Header("Open-world systems disabled during a race")]
        [SerializeField] private GameObject[] openWorldOnly;

        [Header("Channels")]
        [SerializeField] private GameEvent onRaceStarted;
        [SerializeField] private GameEvent onStageStarted;
        [SerializeField] private GameEvent onStageFinished;
        [SerializeField] private GameEvent onRaceFinished;

        [Header("Tuning")]
        [Tooltip("Real seconds of service time between stages (GDD: ~10 min countdown).")]
        [SerializeField] private float serviceWindowSeconds = 600f;
        [Tooltip("In-game hours consumed by completing a race day.")]
        [SerializeField] private float hoursPerRaceDay = 4f;
        [SerializeField] private float countdownSeconds = 3f;

        private RaceEvent activeEvent;
        private readonly List<StageResult> dayResults = new List<StageResult>();
        private float countdown;

        public IReadOnlyList<StageResult> DayResults => dayResults;
        public RaceEvent ActiveEvent => activeEvent;

        private void OnEnable()
        {
            stageRunner.OnStageFinished += HandleStageFinished;
            stageRunner.OnCheckpointPassed += HandleCheckpoint;
        }

        private void OnDisable()
        {
            stageRunner.OnStageFinished -= HandleStageFinished;
            stageRunner.OnCheckpointPassed -= HandleCheckpoint;
        }

        // ---- entry ---------------------------------------------------------

        /// Called by the entry-tent board when the player signs on.
        public bool StartEvent(RaceEvent evt)
        {
            if (raceState.inRace || evt == null || evt.completed) return false;
            if (!evt.IsOpenNow(clock)) return false;
            if (economy.Payouts.entryFee > 0 && !economy.TrySpend(economy.Payouts.entryFee)) return false;

            activeEvent = evt;
            dayResults.Clear();

            raceState.inRace = true;
            raceState.activeEventId = evt.eventId;
            raceState.stageIndex = 0;
            raceState.stageCount = evt.stageIds.Count;
            SetOpenWorldActive(false);
            if (clockPaused) clockPaused.Value = true;    // time stops for the duration (GDD)

            onRaceStarted?.Raise();
            BeginStage(0);
            return true;
        }

        private void BeginStage(int index)
        {
            var stage = database.GetStage(activeEvent.stageIds[index]);
            if (stage == null) { EndEvent(); return; }

            raceState.stageIndex = index;
            raceState.activeStageId = stage.id;
            raceState.phase = RacePhase.Countdown;
            raceState.stageTime = 0f;
            countdown = countdownSeconds;

            var route = stage.RunOrder();
            carSpawner.Current?.Vehicle.Teleport(route[0].position, Quaternion.Euler(route[0].eulerAngles));
            carSpawner.Current?.Vehicle.SetControlEnabled(false);

            stageRunner.Begin(stage);
            raceState.totalCheckpoints = stageRunner.TotalCheckpoints;
            raceState.nextCheckpoint = 1;
            raceState.Notify();
            onStageStarted?.Raise();
        }

        private void Update()
        {
            if (!raceState.inRace) return;

            switch (raceState.phase)
            {
                case RacePhase.Countdown:
                    countdown -= Time.deltaTime;
                    if (countdown <= 0f)
                    {
                        raceState.phase = RacePhase.Running;
                        carSpawner.Current?.Vehicle.SetControlEnabled(true);
                        carSpawner.Current?.Vehicle.SetEngineRunning(true);
                        raceState.Notify();
                    }
                    break;

                case RacePhase.Running:
                    raceState.stageTime = stageRunner.Timer;
                    break;

                case RacePhase.ServiceWindow:
                    raceState.serviceSecondsRemaining -= Time.deltaTime;
                    if (raceState.serviceSecondsRemaining <= 0f) BeginStage(raceState.stageIndex + 1);
                    break;
            }
        }

        private void HandleCheckpoint(int passed, int total)
        {
            raceState.nextCheckpoint = passed + 1;
            raceState.Notify();
        }

        // ---- scoring -------------------------------------------------------

        private void HandleStageFinished(StageResult result)
        {
            var stage = database.GetStage(result.stageId);
            var rivals = RivalTimes.Generate(activeEvent, stage, activeEvent.fieldSize);
            result.placement = RivalTimes.Placement(rivals, result.TotalSeconds);
            result.fieldSize = activeEvent.fieldSize;

            dayResults.Add(result);
            raceState.phase = RacePhase.Finished;
            raceState.Notify();
            onStageFinished?.Raise();

            bool moreStages = raceState.stageIndex + 1 < activeEvent.stageIds.Count;
            if (moreStages) EnterServiceWindow();
            else EndEvent();
        }

        /// Between stages: control returns, a countdown runs, service park is reachable.
        private void EnterServiceWindow()
        {
            raceState.phase = RacePhase.ServiceWindow;
            raceState.serviceSecondsRemaining = serviceWindowSeconds;
            carSpawner.Current?.Vehicle.SetControlEnabled(true);
            raceState.Notify();
        }

        /// Skip the rest of the service window once the player is ready.
        public void ReadyForNextStage()
        {
            if (raceState.phase == RacePhase.ServiceWindow) raceState.serviceSecondsRemaining = 0f;
        }

        private void EndEvent()
        {
            int placementSum = 0;
            foreach (var r in dayResults) placementSum += r.placement;

            int placement = dayResults.Count == 0 ? activeEvent.fieldSize
                : Mathf.Max(1, Mathf.RoundToInt((float)placementSum / dayResults.Count));

            int payout = economy.Payouts.Payout(activeEvent.purse, placement, activeEvent.fieldSize);
            economy.Credit(payout);
            activeEvent.completed = true;

            raceState.inRace = false;
            raceState.phase = RacePhase.None;
            SetOpenWorldActive(true);

            if (clockPaused) clockPaused.Value = false;
            clock.Advance(hoursPerRaceDay);      // dropped off a few hours later (GDD)

            raceState.Notify();
            onRaceFinished?.Raise();
        }

        /// Retire from the stage: scored as a DNF, no payout for that stage.
        public void Retire()
        {
            if (!raceState.inRace) return;
            stageRunner.Finish(true);
        }

        private void SetOpenWorldActive(bool active)
        {
            foreach (var go in openWorldOnly) if (go) go.SetActive(active);
        }

        public EventResult BuildEventResult()
        {
            float total = 0f;
            foreach (var r in dayResults) total += r.TotalSeconds;
            return new EventResult
            {
                eventId = activeEvent?.eventId,
                totalSeconds = total,
                fieldSize = activeEvent?.fieldSize ?? 0
            };
        }
    }
}
