using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RallyGame.Core;
using RallyGame.Input;

namespace RallyGame.UI
{
    /// Pause + save/quit. Save button reflects the "no saving mid-race" rule.
    ///
    /// This is now the ONLY thing in the game that reads Escape. Every other panel
    /// registers with UiModalStack and gets closed from here, topmost first, so
    /// Escape means "back one screen" rather than "every open panel reacts at once".
    public class PauseMenuView : MonoBehaviour, IUiModal
    {
        [SerializeField] private InputReader input;
        [SerializeField] private SaveManager saves;
        [SerializeField] private BoolVariable inputLocked;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button saveButton;
        [SerializeField] private TMP_Text statusLabel;

        private bool open;

        public bool IsModalOpen => open;

        private void Update()
        {
            if (input.MenuPressed)
            {
                // Something is on screen (garage, controls, dealer, or this menu):
                // Escape backs out of it. Otherwise Escape opens the pause menu.
                if (!UiModalStack.CloseTopmost()) Open();
            }

            if (open && saveButton) saveButton.interactable = saves.CanSave;
        }

        public void Open()
        {
            if (open) return;
            open = true;

            if (panel) panel.SetActive(true);
            UiModalStack.Push(this);
            UiModalStack.ApplyInputState(inputLocked);

            if (statusLabel) statusLabel.text = saves.CanSave ? string.Empty : "Cannot save during a race.";
            GameLog.Action(LogCat.UI, "Pause menu OPENED", saves.CanSave ? "saving allowed" : "saving blocked (in race)", this);
        }

        public void Close()
        {
            if (!open) return;
            open = false;

            if (panel) panel.SetActive(false);
            UiModalStack.Pop(this);
            UiModalStack.ApplyInputState(inputLocked);

            GameLog.Action(LogCat.UI, "Pause menu CLOSED", null, this);
        }

        /// Kept for the Resume button and any existing inspector wiring.
        public void Toggle()
        {
            if (open) Close(); else Open();
        }

        public void CloseModal() => Close();

        public void OnSavePressed()
        {
            bool ok = saves.Save();
            if (statusLabel) statusLabel.text = ok ? "Saved." : "Cannot save during a race.";
        }

        public void OnQuitPressed() => Application.Quit();
    }
}