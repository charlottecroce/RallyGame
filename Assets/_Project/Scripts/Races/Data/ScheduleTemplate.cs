using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Races.Data
{
    /// Authored shape of a week. The generator fills locations/stages into these slots,
    /// so pacing is tuned in one asset instead of in code.
    [CreateAssetMenu(menuName = "Rally/Definitions/Schedule Template", fileName = "ScheduleTemplate")]
    public class ScheduleTemplate : ScriptableObject
    {
        [System.Serializable]
        public class CasualSlot
        {
            public Weekday day = Weekday.Wednesday;
            public float startHour = 13f;
            public float endHour = 16f;
        }

        [System.Serializable]
        public class RallyDaySlot
        {
            public Weekday day = Weekday.Friday;
            public float startHour = 18f;
            public float endHour = 22f;
            [Range(1, 5)] public int stageCount = 2;
        }

        [Header("Casual single-stage races")]
        public List<CasualSlot> casualSlots = new List<CasualSlot>();

        [Header("Rally weekend (one location for all days)")]
        public List<RallyDaySlot> rallyDays = new List<RallyDaySlot>();

        [Tooltip("Locations eligible to host the rally weekend.")]
        public List<LocationDefinition> rallyLocations = new List<LocationDefinition>();
        [Tooltip("Locations eligible to host casual races.")]
        public List<LocationDefinition> casualLocations = new List<LocationDefinition>();
    }
}
