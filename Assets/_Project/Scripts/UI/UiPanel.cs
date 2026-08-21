using UnityEngine;
using UnityEngine.Events;
using RallyGame.Core;

namespace RallyGame.UI
{
    /// Turns "Evt_OpenSomethingUi was raised" into "that panel is now on screen".
    ///
    /// THIS MUST LIVE ON AN ALWAYS-ACTIVE OBJECT (the Canvas root), never on the panel
    /// it drives. A component on a deactivated GameObject never gets OnEnable, so it
    /// can never register for the very signal that is supposed to wake it up. That is
    /// precisely the failure this class exists to stop:
    ///
    ///     Evt_OpenGarageUi raised — no listeners
    ///
    /// The panel itself stays a plain GameObject with no script on it.
    public class UiPanel : MonoBehaviour, IUiModal
    {
        [Header("Wiring")]
        [Tooltip("The GameObject to switch on. Leave it deactivated in the scene/prefab.")]
        [SerializeField] private GameObject panel;
        [Tooltip("Raising this shows the panel. e.g. Evt_OpenGarageUi.")]
        [SerializeField] private GameEvent openChannel;
        [Tooltip("Optional. Raising this hides the panel.")]
        [SerializeField] private GameEvent closeChannel;

        [Header("While open")]
        [Tooltip("Var_InputLocked. Stops the player walking/driving and stops the interact raycast.")]
        [SerializeField] private BoolVariable inputLocked;
        [Tooltip("Free the mouse so the panel's buttons are clickable.")]
        [SerializeField] private bool freeCursor = true;
        [Tooltip("Bring to front when opened, so it is not hidden behind another panel.")]
        [SerializeField] private bool raiseToFront = true;

        [Header("Hooks")]
        [Tooltip("Fires every time the panel is opened, including re-opens while already " +
                 "visible. Wire GarageView.Rebuild() here so mode changes take effect.")]
        [SerializeField] private UnityEvent onOpened;
        [SerializeField] private UnityEvent onClosed;

        public bool IsModalOpen => panel && panel.activeSelf;

        private void Awake()
        {
            // Known state at startup regardless of how the prefab was last saved.
            if (panel && panel.activeSelf) panel.SetActive(false);
        }

        private void OnEnable()
        {
            if (!panel)
                GameLog.Error(LogCat.UI,
                    $"UiPanel on '{name}' has no panel assigned — the channel will fire and nothing will appear.", this);

            if (!openChannel)
            {
                GameLog.Error(LogCat.UI,
                    $"UiPanel on '{name}' has no open channel assigned — nothing can ever open it.", this);
                return;
            }

            openChannel.Register(Open);
            if (closeChannel) closeChannel.Register(Close);

            // The interactables raise this from anywhere in the world, so say out loud
            // that someone is now listening. Compare against the "no listeners" line.
            GameLog.Verbose(LogCat.UI,
                $"'{(panel ? panel.name : "<none>")}' now listening to {openChannel.name}", this);
        }

        private void OnDisable()
        {
            if (openChannel) openChannel.Unregister(Open);
            if (closeChannel) closeChannel.Unregister(Close);
        }

        [ContextMenu("Open")]
        public void Open()
        {
            if (!panel)
            {
                GameLog.Refused(LogCat.UI, $"open panel from {(openChannel ? openChannel.name : "<no channel>")}",
                                "UiPanel has no panel assigned", this);
                return;
            }

            bool wasOpen = panel.activeSelf;

            panel.SetActive(true);
            if (raiseToFront) panel.transform.SetAsLastSibling();

            UiModalStack.Push(this);
            UiModalStack.ApplyInputState(inputLocked, freeCursor);

            GameLog.Action(LogCat.UI, wasOpen ? "Panel REFRESHED" : "Panel OPENED",
                           $"'{panel.name}' via {(openChannel ? openChannel.name : "code")}", this);

            // Always fire, even on a re-open: the hood and the workbench share one panel
            // and differ only by a flag, so the contents must be rebuilt each time.
            onOpened?.Invoke();
        }

        [ContextMenu("Close")]
        public void Close()
        {
            if (!panel || !panel.activeSelf) return;

            panel.SetActive(false);
            UiModalStack.Pop(this);
            UiModalStack.ApplyInputState(inputLocked, freeCursor);

            GameLog.Action(LogCat.UI, "Panel CLOSED", $"'{panel.name}'", this);
            onClosed?.Invoke();
        }

        public void Toggle()
        {
            if (IsModalOpen) Close(); else Open();
        }

        public void CloseModal() => Close();
    }
}