using UnityEngine;
using RallyGame.Core;
using RallyGame.Input;

namespace RallyGame.Player
{
    /// Raycast interaction. Publishes the current prompt through an SO channel so
    /// the HUD never has to find the player.
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private Camera view;
        [SerializeField] private StringVariable promptText;
        [SerializeField] private BoolVariable inputLocked;
        [SerializeField] private float range = 3f;
        [SerializeField] private LayerMask mask = ~0;

        private IInteractable current;

        private void Update()
        {
            if (inputLocked && inputLocked.Value) { SetPrompt(null); return; }

            current = null;
            var ray = new Ray(view.transform.position, view.transform.forward);

            if (Physics.Raycast(ray, out var hit, range, mask, QueryTriggerInteraction.Collide))
                current = hit.collider.GetComponentInParent<IInteractable>();

            SetPrompt(current != null && current.CanInteract ? current.Prompt : null);

            if (input.InteractPressed && current != null && current.CanInteract)
                current.Interact(gameObject);
        }

        private void SetPrompt(string text)
        {
            if (promptText) promptText.Value = text ?? string.Empty;
        }
    }
}
