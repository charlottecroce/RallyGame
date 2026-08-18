using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RallyGame.Core;
using RallyGame.Input;

namespace RallyGame.UI
{
    /// Pause + save/quit. Save button reflects the "no saving mid-race" rule.
    public class PauseMenuView : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private SaveManager saves;
        [SerializeField] private BoolVariable inputLocked;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button saveButton;
        [SerializeField] private TMP_Text statusLabel;

        private bool open;

        private void Update()
        {
            if (input.MenuPressed) Toggle();
            if (open && saveButton) saveButton.interactable = saves.CanSave;
        }

        public void Toggle()
        {
            open = !open;
            if (panel) panel.SetActive(open);
            if (inputLocked) inputLocked.Value = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (open && statusLabel) statusLabel.text = saves.CanSave ? string.Empty : "Cannot save during a race.";
        }

        public void OnSavePressed()
        {
            bool ok = saves.Save();
            if (statusLabel) statusLabel.text = ok ? "Saved." : "Cannot save during a race.";
        }

        public void OnQuitPressed() => Application.Quit();
    }
}
