using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RallyGame.Core;
using RallyGame.Vehicles.Controllers;

namespace RallyGame.UI
{
    /// The HUD half of the reset feature. Reads only SO state and the service, never
    /// searches for the car — same rule as HudView.
    ///
    /// The button is not the only way in: R does the same thing, and CarUnstick can
    /// raise the event itself. This just makes it visible and greys it out during the
    /// cooldown so a frustrated player does not mash it.
    public class CarResetButton : MonoBehaviour
    {
        [SerializeField] private CarResetService service;
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;
        [Tooltip("Optional. Hides the whole button while on foot.")]
        [SerializeField] private BoolVariable isDriving;
        [Tooltip("Optional root to show/hide. Falls back to this object.")]
        [SerializeField] private GameObject root;

        [SerializeField] private string idleText = "Reset car  [R]";
        [SerializeField] private string busyText = "Reset...";

        private GameObject Root => root ? root : gameObject;

        private void Awake()
        {
            if (button) button.onClick.AddListener(OnPressed);
            else GameLog.Warn(LogCat.UI, $"'{name}' has no Button assigned — the reset control does nothing.", this);
        }

        private void OnDestroy() { if (button) button.onClick.RemoveListener(OnPressed); }

        private void Update()
        {
            bool show = isDriving == null || isDriving.Value;
            if (Root.activeSelf != show) Root.SetActive(show);
            if (!show || service == null) return;

            bool ready = service.CanReset;
            if (button && button.interactable != ready) button.interactable = ready;
            if (label) label.text = ready ? idleText : busyText;
        }

        private void OnPressed()
        {
            GameLog.Action(LogCat.UI, "Reset button pressed", null, this);
            service?.RequestReset();
        }
    }
}