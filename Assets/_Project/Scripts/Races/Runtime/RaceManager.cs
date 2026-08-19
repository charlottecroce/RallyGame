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
    ///
    /// Every phase transition is logged. The per-frame countdown tick and stage timer
    /// are not - only the moment they cross a boundary.
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
            // Each guard reports its own reason: "nothing happened when I signed on"
            // is otherwise impossible to diagnose from the outside.
            if (raceState.inRace)
            {
                GameLog.Refused(LogCat.Race, "start event", $"already racing '{raceState.activeEventId}'", this);
                return false;
            }
            if (evt == null)
            {
                GameLog.Refused(LogCat.Race, "start event", "event was null", this);
                return false;
            }
            if (evt.completed)
            {
                GameLog.Refused(LogCat.Race, $"start '{evt.eventId}'", "already completed this week", this);
                return false;
            }
            if (!evt.IsOpenNow(clock))
            {
                GameLog.Refused(LogCat.Race, $"start '{evt.eventId}'",
                                $"window closed — event runs {evt.day} {evt.startHour:0}h-{evt.endHour:0}h, " +
                                $"now is {clock.Weekday} {clock.TimeOfDay:0.0}h", this);
                return false;
            }
            if (economy.Payouts.entryFee > 0 && !economy.TrySpend(economy.Payouts.entryFee))
            {
                GameLog.Refused(LogCat.Race, $"start '{evt.eventId}'",
                                $"entry fee {economy.Payouts.entryFee:N0} unaffordable", this);
                return false;
            }

            activeEvent = evt;
            dayResults.Clear();

            raceState.inRace = true;
            raceState.activeEventId = evt.eventId;
            raceState.stageIndex = 0;
            raceState.stageCount = evt.stageIds.Count;

            GameLog.Action(LogCat.Race, "RACE EVENT STARTED",
                           $"'{evt.eventId}' ({evt.kind}) at {evt.locationId} — " +
                           $"{evt.stageIds.Count} stage(s), field {evt.fieldSize}, purse {evt.purse:N0}, " +
                           $"entry fee {economy.Payouts.entryFee:N0}", this);

            SetOpenWorldActive(false);
            if (clockPaused) clockPaused.Value = true;    // time stops for the duration (GDD)

            onRaceStarted?.Raise();
            BeginStage(0);
            return true;
        }

        private void BeginStage(int index)
        {
            var stage = database.GetStage(activeEvent.stageIds[index]);
            if (stage == null)
            {
                GameLog.Error(LogCat.Race,
                    $"Stage id '{activeEvent.stageIds[index]}' is not in the DefinitionDatabase — ending event early.", this);
                EndEvent();
                return;
            }

            raceState.stageIndex = index;
            raceState.activeStageId = stage.id;
            raceState.phase = RacePhase.Countdown;
            raceState.stageTime = 0f;
            countdown = countdownSeconds;

            var route = stage.RunOrder();
            carSpawner.Current?.Vehicle.Teleport(route[0].position, Quaternion.Euler(route[0].eulerAngles));
            carSpawner.Current?.Vehicle.SetControlEnabled(false);

            if (carSpawner.Current == null)
                GameLog.Warn(LogCat.Race, "Stage starting but there is no car in the world to place on the start line.", this);

            stageRunner.Begin(stage);
            raceState.totalCheckpoints = stageRunner.TotalCheckpoints;
            raceState.nextCheckpoint = 1;
            raceState.Notify();

            GameLog.Action(LogCat.Race, $"Stage {index + 1} of {activeEvent.stageIds.Count} loaded",
                           $"'{stage.id}', {raceState.totalCheckpoints} gate(s), " +
                           $"car on start line, {countdownSeconds:0.#}s countdown", this);

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

                        // Fires exactly once per stage, so it is safe to log here.
                        GameLog.Action(LogCat.Race, "GO — stage running", $"'{raceState.activeStageId}'", this);
                    }
                    break;

                case RacePhase.Running:
                    raceState.stageTime = stageRunner.Timer;   // per-frame, never logged
                    break;

                case RacePhase.ServiceWindow:
                    raceState.serviceSecondsRemaining -= Time.deltaTime;
                    if (raceState.serviceSecondsRemaining <= 0f)
                    {
                        GameLog.Action(LogCat.Race, "Service window over", "starting next stage", this);
                        BeginStage(raceState.stageIndex + 1);
                    }
                    break;
            }
        }

        private void HandleCheckpoint(int passed, int total)
        {
            // StageRunner already logs each gate; this only mirrors it into RaceState.
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

            GameLog.Action(LogCat.Race, "Stage scored",
                           $"'{result.stageId}' {result.TotalSeconds:0.00}s -> " +
                           $"P{result.placement} of {result.fieldSize}{(result.didNotFinish ? " (DNF)" : "")}", this);

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

            GameLog.Action(LogCat.Race, "Service window OPEN",
                           $"{serviceWindowSeconds:0}s until stage {raceState.stageIndex + 2}, " +
                           "driver control returned, service park reachable", this);
        }

        /// Skip the rest of the service window once the player is ready.
        public void ReadyForNextStage()
        {
            if (raceState.phase == RacePhase.ServiceWindow)
            {
                GameLog.Action(LogCat.Race, "Service window skipped by player",
                               $"{raceState.serviceSecondsRemaining:0.#}s forfeited", this);
                raceState.serviceSecondsRemaining = 0f;
            }
            else
            {
                GameLog.Refused(LogCat.Race, "skip service window", $"current phase is {raceState.phase}", this);
            }
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

            GameLog.Action(LogCat.Race, "EVENT COMPLETE",
                           $"'{activeEvent.eventId}' — average placement {placement} of {activeEvent.fieldSize} " +
                           $"across {dayResults.Count} stage(s), payout {payout:N0} from purse {activeEvent.purse:N0}", this);

            raceState.inRace = false;
            raceState.phase = RacePhase.None;
            SetOpenWorldActive(true);

            if (clockPaused) clockPaused.Value = false;
            clock.Advance(hoursPerRaceDay);      // dropped off a few hours later (GDD)

            GameLog.Action(LogCat.Race, "Returned to open world",
                           $"clock advanced {hoursPerRaceDay:0.#}h", this);

            raceState.Notify();
            onRaceFinished?.Raise();
        }

        /// Retire from the stage: scored as a DNF, no payout for that stage.
        public void Retire()
        {
            if (!raceState.inRace)
            {
                GameLog.Refused(LogCat.Race, "retire", "not currently in a race", this);
                return;
            }

            GameLog.Action(LogCat.Race, "RETIRED from stage",
                           $"'{raceState.activeStageId}' at {raceState.stageTime:0.0}s — scored as DNF", this);
            stageRunner.Finish(true);
        }

        private void SetOpenWorldActive(bool active)
        {
            GameLog.Verbose(LogCat.Race,
                $"Open-world objects {(active ? "re-enabled" : "disabled")}: {openWorldOnly.Length} object(s)", this);

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
