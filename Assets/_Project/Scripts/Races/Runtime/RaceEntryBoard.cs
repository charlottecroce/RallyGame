using UnityEngine;
using RallyGame.Core;
using RallyGame.Races.Data;

namespace RallyGame.Races.Runtime
{
    /// The interactable board in the entry tent. Signs the player on if an event
    /// for this location is open right now.
    public class RaceEntryBoard : MonoBehaviour, IInteractable
    {
        [SerializeField] private LocationDefinition location;
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private WeekScheduler scheduler;
        [SerializeField] private GameClock clock;

        private RaceEvent Open => scheduler.OpenEventAt(clock, location.id);

        public bool CanInteract => Open != null;
        public string Prompt
        {
            get
            {
                var e = Open;
                return e == null ? $"{location.displayName} - no event running"
                                 : $"Start {e.kind} ({e.stageIds.Count} stage(s)) [E]";
            }
        }

        public void Interact(GameObject instigator)
        {
            var e = Open;
            if (e != null) raceManager.StartEvent(e);
        }
    }
}
