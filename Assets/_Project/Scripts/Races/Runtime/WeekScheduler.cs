using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Races.Data;
using RallyGame.Utilities;

namespace RallyGame.Races.Runtime
{
    /// Builds the race book every Monday 00:00. Deterministic per week so a reload
    /// never reshuffles the calendar the player already read.
    ///
    /// Generation happens once a week, so the full calendar is printed. OpenEventAt
    /// is called every frame the player looks at an entry board, so it stays silent.
    public class WeekScheduler : MonoBehaviour
    {
        [SerializeField] private ScheduleTemplate template;
        [SerializeField] private IntVariable dayIndex;
        [SerializeField] private GameEvent onWeekRolled;
        [SerializeField] private GameEvent onScheduleChanged;

        [Header("Debug")]
        [Tooltip("Print every generated event as its own line, not just the count.")]
        [SerializeField] private bool logFullCalendar = true;

        public WeeklySchedule Current { get; private set; } = new WeeklySchedule();
        public int CurrentWeek => Mathf.FloorToInt(dayIndex.Value / 7f);

        private void OnEnable() { if (onWeekRolled) onWeekRolled.Register(Generate); }
        private void OnDisable() { if (onWeekRolled) onWeekRolled.Unregister(Generate); }

        private void Start()
        {
            if (Current.weekIndex != CurrentWeek)
            {
                GameLog.Verbose(LogCat.Race,
                    $"Schedule is for week {Current.weekIndex} but the clock says week {CurrentWeek} — regenerating.", this);
                Generate();
            }
        }

        public void Generate()
        {
            int week = CurrentWeek;
            var rng = new DeterministicRandom(week, "schedule");
            var schedule = new WeeklySchedule { weekIndex = week };

            // Casual single-stage races.
            foreach (var slot in template.casualSlots)
            {
                var loc = Pick(template.casualLocations, ref rng);
                if (loc == null || loc.stages.Count == 0)
                {
                    GameLog.Warn(LogCat.Race,
                        $"Casual slot on {slot.day} skipped — location pool is empty or the picked location has no stages.", this);
                    continue;
                }

                var stage = loc.stages[rng.Range(0, loc.stages.Count)];
                schedule.events.Add(new RaceEvent
                {
                    eventId = $"w{week}_casual_{schedule.events.Count}",
                    locationId = loc.id,
                    kind = RaceKind.CasualStage,
                    day = slot.day,
                    startHour = slot.startHour,
                    endHour = slot.endHour,
                    stageIds = new List<string> { stage.id },
                    purse = loc.casualPurse,
                    fieldSize = loc.fieldSize
                });
            }

            // Rally weekend: one location, several days, stages not repeated within the weekend.
            var rallyLoc = Pick(template.rallyLocations, ref rng);
            if (rallyLoc != null && rallyLoc.stages.Count > 0)
            {
                string groupId = $"w{week}_rally";
                var unused = new List<StageDefinition>(rallyLoc.stages);

                foreach (var day in template.rallyDays)
                {
                    var stageIds = new List<string>();
                    for (int i = 0; i < day.stageCount; i++)
                    {
                        if (unused.Count == 0)
                        {
                            GameLog.Verbose(LogCat.Race,
                                $"Rally at {rallyLoc.displayName} ran out of unique stages — reusing the pool.", this);
                            unused = new List<StageDefinition>(rallyLoc.stages);
                        }
                        int idx = rng.Range(0, unused.Count);
                        stageIds.Add(unused[idx].id);
                        unused.RemoveAt(idx);
                    }

                    schedule.events.Add(new RaceEvent
                    {
                        eventId = $"{groupId}_{day.day}",
                        locationId = rallyLoc.id,
                        kind = RaceKind.RallyDay,
                        day = day.day,
                        startHour = day.startHour,
                        endHour = day.endHour,
                        stageIds = stageIds,
                        purse = Mathf.RoundToInt(rallyLoc.rallyPurse / Mathf.Max(1, template.rallyDays.Count)),
                        fieldSize = rallyLoc.fieldSize,
                        rallyGroupId = groupId
                    });
                }
            }
            else
            {
                GameLog.Warn(LogCat.Race, "No rally weekend generated — rally location pool is empty.", this);
            }

            Current = schedule;

            GameLog.Action(LogCat.Race, "RACEBOOK GENERATED",
                           $"week {week + 1} — {schedule.events.Count} event(s)", this);

            if (logFullCalendar)
                foreach (var e in schedule.events)
                    GameLog.Verbose(LogCat.Race,
                        $"  {e.day} {e.startHour:00}:00-{e.endHour:00}:00  {e.kind} at {e.locationId}  " +
                        $"{e.stageIds.Count} stage(s), purse {e.purse:N0}, field {e.fieldSize}  [{e.eventId}]", this);

            onScheduleChanged?.Raise();
        }

        /// Any event whose window contains the current time.
        /// Called every frame from RaceEntryBoard.Prompt — intentionally silent.
        public RaceEvent OpenEventAt(GameClock clock, string locationId)
        {
            foreach (var e in Current.events)
                if (e.locationId == locationId && e.IsOpenNow(clock)) return e;
            return null;
        }

        /// Weighted-free pick from an authored pool; returns null if the pool is empty.
        private static LocationDefinition Pick(List<LocationDefinition> pool, ref DeterministicRandom rng)
            => pool == null || pool.Count == 0 ? null : pool[rng.Range(0, pool.Count)];

        public void Restore(WeeklySchedule schedule)
        {
            if (schedule != null && schedule.events != null)
            {
                Current = schedule;
                GameLog.Action(LogCat.Race, "Racebook restored from save",
                               $"week {schedule.weekIndex + 1}, {schedule.events.Count} event(s)", this);
            }
            else
            {
                GameLog.Warn(LogCat.Race, "Restore called with an empty schedule — keeping the generated one.", this);
            }

            onScheduleChanged?.Raise();
        }
    }
}
