using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Races.Data
{
    public enum RaceKind { CasualStage, RallyDay }

    /// One entry in the weekly race book. Save state (generated per week),
    /// not an authored asset.
    [System.Serializable]
    public class RaceEvent
    {
        public string eventId;
        public string locationId;
        public RaceKind kind;
        public Weekday day;
        public float startHour;
        public float endHour;
        public List<string> stageIds = new List<string>();
        public int purse;
        public int fieldSize = 24;
        public bool completed;

        [Tooltip("Rally weekend events sharing this tag are scored as one rally.")]
        public string rallyGroupId;

        public bool IsOpenNow(GameClock clock)
            => !completed && clock.Weekday == day && clock.TimeOfDay >= startHour && clock.TimeOfDay < endHour;

        public string WindowLabel() => $"{day} {startHour:00}:00-{endHour:00}:00";
    }

    [System.Serializable]
    public class WeeklySchedule
    {
        public int weekIndex = -1;
        public List<RaceEvent> events = new List<RaceEvent>();

        public RaceEvent Find(string id)
        {
            foreach (var e in events) if (e.eventId == id) return e;
            return null;
        }
    }
}
