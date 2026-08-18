using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Dealers
{
    public enum DealerKind { Parts, Cars, Mechanic }

    /// The interactable object you walk up to. Opens the matching UI; all logic
    /// lives in the UI + EconomyService, so this stays a thin trigger.
    public class DealerCounter : MonoBehaviour, IInteractable
    {
        [SerializeField] private DealerKind kind;
        [SerializeField] private string prompt = "Talk to dealer [E]";
        [SerializeField] private GameEvent onOpenRequested;
        [SerializeField] private BoolVariable isOpenVariable;

        [Header("Opening hours (24h)")]
        [SerializeField] private GameClock clock;
        [SerializeField] private float openHour = 8f;
        [SerializeField] private float closeHour = 20f;

        public DealerKind Kind => kind;
        public bool CanInteract => IsOpenNow;
        public string Prompt => IsOpenNow ? prompt : $"Closed (opens {openHour:00}:00)";

        private bool IsOpenNow => clock == null || (clock.TimeOfDay >= openHour && clock.TimeOfDay < closeHour);

        public void Interact(GameObject instigator)
        {
            if (!IsOpenNow) return;
            if (isOpenVariable) isOpenVariable.Value = true;
            onOpenRequested?.Raise();
        }
    }
}
