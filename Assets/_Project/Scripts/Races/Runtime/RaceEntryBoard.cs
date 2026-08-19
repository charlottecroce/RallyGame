using UnityEngine;
using RallyGame.Core;
using RallyGame.Races.Data;

namespace RallyGame.Races.Runtime
{
    /// The interactable board in the entry tent. Signs the player on if an event
    /// for this location is open right now.
    ///
    /// "Nothing happens when I press E on the board" is nearly always a schedule
    /// window problem, so the refusal path logs the current day/time against the
    /// location instead of failing silently.
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

            if (e == null)
            {
                GameLog.Refused(LogCat.Race, $"sign on at {location.displayName}",
                                $"no open event here on {clock.Weekday} at {clock.TimeOfDay:0.0}h", this);
                return;
            }

            GameLog.Action(LogCat.Race, "SIGNING ON",
                           $"event '{e.eventId}' ({e.kind}) at {location.displayName}, " +
                           $"{e.stageIds.Count} stage(s), field of {e.fieldSize}, purse {e.purse:N0}", this);

            bool started = raceManager.StartEvent(e);

            if (!started)
                GameLog.Refused(LogCat.Race, $"start event '{e.eventId}'",
                                "RaceManager rejected it — already in a race, event completed, window closed, or entry fee unaffordable", this);
        }
    }
}
