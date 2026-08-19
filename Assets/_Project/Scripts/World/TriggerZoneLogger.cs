using UnityEngine;
using RallyGame.Core;

namespace RallyGame.World
{
    /// Drop this on any trigger collider that does not already have a script of its
    /// own — service park boundaries, stage start areas, shop doorways, out-of-bounds
    /// volumes — and it will report entry and exit without any other wiring.
    ///
    /// Enter/exit are edge events, so this is safe on any number of volumes. Dwell
    /// time is reported on exit rather than sampled, so nothing here runs per frame.
    [RequireComponent(typeof(Collider))]
    public class TriggerZoneLogger : MonoBehaviour
    {
        [Tooltip("Friendly name for the console. Falls back to the GameObject name.")]
        [SerializeField] private string zoneName;

        [Tooltip("Only report colliders with this tag. Leave blank to report everything.")]
        [SerializeField] private string filterTag = "Player";

        [SerializeField] private LogCat category = LogCat.World;

        [Header("Optional side effects")]
        [Tooltip("Raised on enter, if assigned.")]
        [SerializeField] private GameEvent onEntered;
        [Tooltip("Raised on exit, if assigned.")]
        [SerializeField] private GameEvent onExited;
        [Tooltip("Set true while an accepted collider is inside, if assigned.")]
        [SerializeField] private BoolVariable occupiedFlag;

        private float enteredAt;
        private int occupants;

        private string Label => string.IsNullOrEmpty(zoneName) ? name : zoneName;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
            zoneName = name;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!Accepts(other)) return;

            occupants++;
            if (occupants == 1)
            {
                enteredAt = Time.time;
                if (occupiedFlag) occupiedFlag.Value = true;
            }

            GameLog.Action(category, $"ENTERED zone '{Label}'",
                           $"by '{other.name}'{(occupants > 1 ? $", {occupants} occupant(s)" : "")}", this);
            onEntered?.Raise();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!Accepts(other)) return;

            occupants = Mathf.Max(0, occupants - 1);

            GameLog.Action(category, $"LEFT zone '{Label}'",
                           $"by '{other.name}' after {Time.time - enteredAt:0.0}s", this);

            if (occupants == 0 && occupiedFlag) occupiedFlag.Value = false;
            onExited?.Raise();
        }

        private bool Accepts(Collider other)
            => string.IsNullOrEmpty(filterTag) || other.CompareTag(filterTag);
    }
}
