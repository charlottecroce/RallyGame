using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RallyGame.Core;

namespace RallyGame.UI
{
    /// Full-screen controls reference, opened from the pause menu.
    ///
    /// The layout is generated from the serialized table below rather than laid out by
    /// hand: this screen is a list of key/action pairs, and a list is easier to keep
    /// correct in the inspector than in a hundred RectTransforms. Same reasoning as
    /// DealerView and GarageView instantiating their rows, minus the row prefab.
    ///
    /// The bindings below mirror InputReader. If you rebind there, update these — there
    /// is no automatic link, because InputReader reads raw device controls rather than
    /// named actions.
    public class ControlsView : MonoBehaviour, IUiModal
    {
        [System.Serializable]
        public class ControlEntry
        {
            public string keys;
            public string action;
            public ControlEntry() { }
            public ControlEntry(string keys, string action) { this.keys = keys; this.action = action; }
        }

        [System.Serializable]
        public class ControlGroup
        {
            public string title;
            public List<ControlEntry> entries = new List<ControlEntry>();
        }

        [Header("Wiring")]
        [Tooltip("Where the screen is built. Leave empty to use this object's transform " +
                 "(correct when this component sits on the Canvas).")]
        [SerializeField] private RectTransform parentOverride;
        [Tooltip("Var_InputLocked — same asset the pause menu uses.")]
        [SerializeField] private BoolVariable inputLocked;

        [Header("Content")]
        [SerializeField] private string title = "CONTROLS";
        [SerializeField] private string footer = "Esc — back";
        [SerializeField]
        private List<ControlGroup> groups = new List<ControlGroup>
        {
            new ControlGroup
            {
                title = "ON FOOT",
                entries = new List<ControlEntry>
                {
                    new ControlEntry("W A S D", "Move"),
                    new ControlEntry("Mouse", "Look"),
                    new ControlEntry("Space", "Jump"),
                    new ControlEntry("E", "Interact / enter car"),
                    new ControlEntry("Tab", "Race book"),
                    new ControlEntry("M", "Map"),
                    new ControlEntry("Esc", "Pause menu"),
                }
            },
            new ControlGroup
            {
                title = "DRIVING",
                entries = new List<ControlEntry>
                {
                    new ControlEntry("W", "Throttle"),
                    new ControlEntry("S", "Brake"),
                    new ControlEntry("A / D", "Steer"),
                    new ControlEntry("Space", "Handbrake"),
                    new ControlEntry("Left Shift", "Shift up"),
                    new ControlEntry("Left Ctrl", "Shift down"),
                    new ControlEntry("C", "Clutch"),
                    new ControlEntry("G", "Manual / automatic"),
                    new ControlEntry("L", "Lights"),
                    new ControlEntry("R", "Reset car"),
                    new ControlEntry("E", "Exit car"),
                }
            },
            new ControlGroup
            {
                title = "GAMEPAD",
                entries = new List<ControlEntry>
                {
                    new ControlEntry("Left stick", "Steer"),
                    new ControlEntry("RT / LT", "Throttle / brake"),
                    new ControlEntry("A", "Handbrake"),
                    new ControlEntry("B", "Jump"),
                    new ControlEntry("Y", "Interact"),
                    new ControlEntry("RB / LB", "Shift up / down"),
                    new ControlEntry("X", "Clutch"),
                    new ControlEntry("D-pad ←", "Manual / automatic"),
                    new ControlEntry("D-pad ↑", "Lights"),
                    new ControlEntry("D-pad ↓", "Reset car"),
                    new ControlEntry("Start", "Pause menu"),
                    new ControlEntry("Back", "Race book"),
                }
            },
        };

        [Header("Style")]
        [SerializeField] private Color backdrop = new Color(0.02f, 0.02f, 0.03f, 0.92f);
        [SerializeField] private Color frameColor = new Color(0.09f, 0.09f, 0.11f, 0.98f);
        [SerializeField] private Color keyColor = new Color(0.96f, 0.74f, 0.30f);
        [SerializeField] private Color textColor = new Color(0.90f, 0.90f, 0.92f);
        [SerializeField] private Color headingColor = new Color(0.55f, 0.78f, 1f);
        [SerializeField] private float columnWidth = 330f;

        private GameObject root;

        public bool IsModalOpen => root && root.activeSelf;

        private void Awake()
        {
            Build();
            if (root) root.SetActive(false);
        }

        // ---- open / close ---------------------------------------------------

        /// Wire the pause menu's Controls button to this.
        public void Open()
        {
            if (!root) Build();
            if (!root) return;

            root.SetActive(true);
            root.transform.SetAsLastSibling();   // in front of the pause menu behind it

            UiModalStack.Push(this);
            UiModalStack.ApplyInputState(inputLocked);

            GameLog.Action(LogCat.UI, "Controls screen OPENED", $"{groups.Count} group(s)", this);
        }

        public void Close()
        {
            if (!IsModalOpen) return;

            root.SetActive(false);
            UiModalStack.Pop(this);
            UiModalStack.ApplyInputState(inputLocked);

            GameLog.Action(LogCat.UI, "Controls screen CLOSED", null, this);
        }

        public void Toggle() { if (IsModalOpen) Close(); else Open(); }

        public void CloseModal() => Close();

        // ---- construction ---------------------------------------------------

        private void Build()
        {
            var parent = parentOverride ? parentOverride : transform as RectTransform;
            if (!parent)
            {
                GameLog.Error(LogCat.UI,
                    $"ControlsView on '{name}' is not under a Canvas and has no parent override — " +
                    "cannot build the screen.", this);
                return;
            }

            root = NewRect("ControlsPanel", parent);
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            AddImage(root, backdrop);           // also swallows clicks meant for the world

            var frame = NewRect("Frame", rootRt);
            var frameRt = (RectTransform)frame.transform;
            frameRt.anchorMin = frameRt.anchorMax = frameRt.pivot = new Vector2(0.5f, 0.5f);
            frameRt.anchoredPosition = Vector2.zero;
            AddImage(frame, frameColor);

            var frameLayout = frame.AddComponent<VerticalLayoutGroup>();
            frameLayout.padding = new RectOffset(40, 40, 32, 32);
            frameLayout.spacing = 20f;
            frameLayout.childAlignment = TextAnchor.UpperCenter;
            frameLayout.childControlWidth = frameLayout.childControlHeight = true;
            frameLayout.childForceExpandWidth = frameLayout.childForceExpandHeight = false;

            var fit = frame.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddText(frameRt, title, 40f, headingColor, TextAlignmentOptions.Center, FontStyles.Bold);

            var columns = NewRect("Columns", frameRt);
            var colLayout = columns.AddComponent<HorizontalLayoutGroup>();
            colLayout.spacing = 34f;
            colLayout.childAlignment = TextAnchor.UpperLeft;
            colLayout.childControlWidth = colLayout.childControlHeight = true;
            colLayout.childForceExpandWidth = colLayout.childForceExpandHeight = false;

            foreach (var group in groups) BuildColumn((RectTransform)columns.transform, group);

            if (!string.IsNullOrEmpty(footer))
                AddText(frameRt, footer, 17f, new Color(0.6f, 0.6f, 0.64f), TextAlignmentOptions.Center);
        }

        private void BuildColumn(RectTransform parent, ControlGroup group)
        {
            if (group == null) return;

            var column = NewRect($"Column_{group.title}", parent);
            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var element = column.AddComponent<LayoutElement>();
            element.preferredWidth = columnWidth;

            AddText((RectTransform)column.transform, group.title, 21f, headingColor,
                    TextAlignmentOptions.Left, FontStyles.Bold);

            if (group.entries == null) return;
            foreach (var entry in group.entries) AddRow((RectTransform)column.transform, entry);
        }

        private void AddRow(RectTransform parent, ControlEntry entry)
        {
            if (entry == null) return;

            var row = NewRect("Row", parent);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var keys = AddText((RectTransform)row.transform, entry.keys, 18f, keyColor,
                               TextAlignmentOptions.Right, FontStyles.Bold);
            var keysElement = keys.gameObject.AddComponent<LayoutElement>();
            keysElement.preferredWidth = 112f;
            keysElement.minHeight = 24f;

            var action = AddText((RectTransform)row.transform, entry.action, 18f, textColor,
                                 TextAlignmentOptions.Left);
            var actionElement = action.gameObject.AddComponent<LayoutElement>();
            actionElement.flexibleWidth = 1f;
            actionElement.minHeight = 24f;
        }

        // ---- tiny UI helpers -------------------------------------------------

        private static GameObject NewRect(string objectName, Transform parent)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void AddImage(GameObject go, Color color)
        {
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
        }

        private static TMP_Text AddText(RectTransform parent, string content, float size, Color color,
                                        TextAlignmentOptions alignment, FontStyles style = FontStyles.Normal)
        {
            var go = NewRect("Text", parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content ?? string.Empty;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
    }
}