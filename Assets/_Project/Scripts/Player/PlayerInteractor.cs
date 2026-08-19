using UnityEngine;
using RallyGame.Core;
using RallyGame.Input;

namespace RallyGame.Player
{
    /// Raycast interaction. Publishes the current prompt through an SO channel so
    /// the HUD never has to find the player.
    ///
    /// Logging contract: the raycast itself runs every frame and is NEVER logged.
    /// What gets logged is the transitions — a new target entering focus, focus
    /// being lost, the prompt string changing, an interaction firing, and an
    /// interaction being blocked (with the reason). That is a handful of lines
    /// per minute of play instead of one per frame.
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private Camera view;
        [SerializeField] private StringVariable promptText;
        [SerializeField] private BoolVariable inputLocked;
        [SerializeField] private float range = 3f;
        [SerializeField] private LayerMask mask = ~0;

        [Header("Debug")]
        [Tooltip("Log when the raycast target changes, even if it is not interactable.")]
        [SerializeField] private bool logFocusChanges = true;
        [Tooltip("Log the prompt string every time it changes.")]
        [SerializeField] private bool logPromptChanges = true;

        private IInteractable current;

        // Transition tracking - the whole point is to only speak when something changes.
        private Object lastFocusObject;
        private string lastPrompt = string.Empty;
        private bool lastLocked;
        private bool lastCanInteract;

        private void Update()
        {
            bool locked = inputLocked && inputLocked.Value;
            if (locked != lastLocked)
            {
                lastLocked = locked;
                GameLog.Action(LogCat.Interact, locked ? "Interaction LOCKED" : "Interaction UNLOCKED",
                               locked ? "UI or cutscene has input" : "world interaction restored", this);
            }

            if (locked)
            {
                if (current != null || lastFocusObject) ReportFocusLost("input locked");
                current = null;
                SetPrompt(null);
                return;
            }

            current = null;
            var ray = new Ray(view.transform.position, view.transform.forward);

            Object hitObject = null;
            if (Physics.Raycast(ray, out var hit, range, mask, QueryTriggerInteraction.Collide))
            {
                hitObject = hit.collider.gameObject;
                current = hit.collider.GetComponentInParent<IInteractable>();
            }

            ReportFocus(hitObject, hit.distance);

            bool canInteract = current != null && current.CanInteract;

            // A target that is present but refusing is the single most useful thing
            // to see in the console, so log the moment its availability flips.
            if (current != null && canInteract != lastCanInteract)
            {
                lastCanInteract = canInteract;
                GameLog.Action(LogCat.Interact,
                               canInteract ? "Target became AVAILABLE" : "Target became UNAVAILABLE",
                               $"{Describe(current)}", ObjectOf(current));
            }
            else if (current == null)
            {
                lastCanInteract = false;
            }

            SetPrompt(canInteract ? current.Prompt : null);

            if (input.InteractPressed)
            {
                if (current == null)
                {
                    GameLog.Refused(LogCat.Interact, "interact", "nothing in range", this);
                }
                else if (!current.CanInteract)
                {
                    GameLog.Refused(LogCat.Interact, $"interact with {Describe(current)}",
                                    "target reports CanInteract = false", ObjectOf(current));
                }
                else
                {
                    GameLog.Action(LogCat.Interact, $"INTERACT -> {Describe(current)}",
                                   $"prompt was \"{current.Prompt}\", distance {hit.distance:0.00}m", ObjectOf(current));
                    current.Interact(gameObject);
                }
            }
        }

        private void ReportFocus(Object hitObject, float distance)
        {
            if (hitObject == lastFocusObject) return;

            if (hitObject == null)
            {
                ReportFocusLost("looked away");
                return;
            }

            lastFocusObject = hitObject;
            lastCanInteract = false;

            if (current != null)
                GameLog.Action(LogCat.Interact, $"Focus -> {Describe(current)}",
                               $"on '{hitObject.name}' at {distance:0.00}m, CanInteract={current.CanInteract}", hitObject);
            else if (logFocusChanges)
                GameLog.Verbose(LogCat.Interact, $"Focus -> '{hitObject.name}' (not interactable) at {distance:0.00}m", hitObject);
        }

        private void ReportFocusLost(string why)
        {
            if (lastFocusObject == null) return;
            GameLog.Verbose(LogCat.Interact, $"Focus lost from '{lastFocusObject.name}' ({why})");
            lastFocusObject = null;
            lastCanInteract = false;
        }

        private void SetPrompt(string text)
        {
            string value = text ?? string.Empty;

            if (logPromptChanges && value != lastPrompt)
            {
                if (value.Length == 0)      GameLog.Action(LogCat.Interact, "Prompt cleared", $"was \"{lastPrompt}\"", this);
                else if (lastPrompt.Length == 0) GameLog.Action(LogCat.Interact, "Prompt shown", $"\"{value}\"", this);
                else                        GameLog.Change(LogCat.Interact, "Prompt", lastPrompt, value, this);
                lastPrompt = value;
            }

            if (promptText) promptText.Value = value;
        }

        // ---- debug helpers -------------------------------------------------

        private static string Describe(IInteractable i)
        {
            if (i == null) return "<none>";
            var comp = i as Component;
            return comp ? $"{comp.GetType().Name} '{comp.gameObject.name}'" : i.GetType().Name;
        }

        private static Object ObjectOf(IInteractable i) => i as Component;
    }
}
