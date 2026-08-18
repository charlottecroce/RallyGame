using System.Collections.Generic;
using TMPro;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Races.Data;
using RallyGame.Races.Runtime;

namespace RallyGame.UI
{
    /// Always-available weekly calendar (GDD). Rebuilds only when the schedule changes.
    public class RaceBookView : MonoBehaviour
    {
        [SerializeField] private WeekScheduler scheduler;
        [SerializeField] private DefinitionDatabase database;
        [SerializeField] private GameEvent onScheduleChanged;
        [SerializeField] private Transform entryRoot;
        [SerializeField] private TMP_Text entryPrefab;
        [SerializeField] private TMP_Text header;

        private readonly List<TMP_Text> spawned = new List<TMP_Text>();

        private void OnEnable()
        {
            if (onScheduleChanged) onScheduleChanged.Register(Rebuild);
            Rebuild();
        }

        private void OnDisable() { if (onScheduleChanged) onScheduleChanged.Unregister(Rebuild); }

        public void Rebuild()
        {
            foreach (var t in spawned) if (t) Destroy(t.gameObject);
            spawned.Clear();

            if (header) header.text = $"WEEK {scheduler.CurrentWeek + 1}";

            foreach (var e in scheduler.Current.events)
            {
                var loc = database.GetLocation(e.locationId);
                var row = Instantiate(entryPrefab, entryRoot);
                string name = loc ? loc.displayName : e.locationId;
                string tag = e.kind == RaceKind.RallyDay ? "RALLY" : "stage race";
                string done = e.completed ? "  [done]" : string.Empty;
                row.text = $"{e.stageIds.Count} stage {tag} - {name}\n   {e.WindowLabel()}{done}";
                spawned.Add(row);
            }
        }
    }
}
