using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RallyGame.Core;
using RallyGame.Garage;
using RallyGame.Parts.Data;
using RallyGame.Parts.Runtime;
using RallyGame.Utilities;

namespace RallyGame.UI
{
    /// Slot list for the active car + loose inventory. Fitment only - repairs are
    /// deliberately absent here (GDD).
    ///
    /// The Fit button silently picks the best loose part in the slot, which is a
    /// surprising amount of hidden logic. It now reports what it chose and from
    /// how many candidates.
    ///
    /// The same panel serves two entry points. The workbench opens it editable;
    /// the hood box on the car opens it read-only, where the buttons are hidden and
    /// extra diagnostic rows (tires, roadworthiness) appear.
    public class GarageView : MonoBehaviour
    {
        [SerializeField] private GarageState garage;
        [SerializeField] private GarageService service;
        [SerializeField] private Transform slotRoot;
        [Tooltip("Row prefab: needs one TMP_Text and two Buttons (fit, remove).")]
        [SerializeField] private GameObject slotRowPrefab;
        [SerializeField] private TMP_Text carLabel;

        [Header("Mode")]
        [Tooltip("Set true by CarHoodInspectable, false by GarageWorkbench. " +
                 "When true the panel is a diagnostic readout: no fitting, no removing.")]
        [SerializeField] private BoolVariable inspectOnly;

        private readonly List<GameObject> spawned = new List<GameObject>();

        private bool ReadOnly => inspectOnly != null && inspectOnly.Value;

        private void OnEnable()
        {
            if (garage.OnGarageChanged) garage.OnGarageChanged.Register(Rebuild);
            GameLog.Action(LogCat.UI, ReadOnly ? "Garage panel OPENED (inspect only)" : "Garage panel OPENED",
                           $"active car '{garage.activeCarInstanceId ?? "<none>"}', " +
                           $"{garage.LooseParts().Count} loose part(s)", this);
            Rebuild();
        }

        private void OnDisable()
        {
            if (garage.OnGarageChanged) garage.OnGarageChanged.Unregister(Rebuild);
            GameLog.Action(LogCat.UI, "Garage panel CLOSED", null, this);
        }

        public void Rebuild()
        {
            foreach (var go in spawned) if (go) Destroy(go);
            spawned.Clear();

            var car = garage.ActiveCar;
            if (car == null)
            {
                if (carLabel) carLabel.text = "No car";
                GameLog.Verbose(LogCat.UI, "Garage panel rebuilt with no active car.", this);
                return;
            }

            bool readOnly = ReadOnly;
            var def = car.Definition(garage.Database);

            if (carLabel)
                carLabel.text = readOnly
                    ? $"UNDER THE HOOD - {def.displayName}   {car.odometerKm:0} km   (view only)"
                    : $"{def.displayName}   {car.odometerKm:0} km   tires {car.tires.compound} {Format.Percent(1f - car.tires.wear)}";

            // Tires are not a PartSlot, so they never appeared as a row. In inspect
            // mode they matter as much as anything bolted on, so give them one.
            if (readOnly)
                SpawnRow($"Tires: {car.tires.compound}  {Format.Percent(1f - car.tires.wear)} remaining  " +
                         $"({car.tires.kmDriven:0} km on set)", interactive: false);

            int empty = 0;
            foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
            {
                var fitted = car.PartInSlot(slot, garage, garage.Database);
                if (fitted == null) empty++;

                var row = SpawnRow(RowText(slot, fitted, readOnly), interactive: !readOnly);
                if (readOnly) continue;

                var buttons = row.GetComponentsInChildren<Button>(true);
                if (buttons.Length > 0) Wire(buttons[0], slot, true);
                if (buttons.Length > 1) Wire(buttons[1], slot, false);

                if (buttons.Length < 2)
                    GameLog.Warn(LogCat.UI,
                        $"Slot row prefab has only {buttons.Length} Button(s); two are needed (fit, remove).", this);
            }

            // A car that will not start is the single thing you open the hood to find out.
            if (readOnly && service != null)
            {
                bool ok = service.IsRoadworthy(car, out var missing);
                SpawnRow(ok ? "Status: roadworthy"
                            : $"Status: NOT ROADWORTHY - {string.Join(", ", missing)}",
                         interactive: false);
            }

            GameLog.Verbose(LogCat.UI,
                $"Garage panel rebuilt{(readOnly ? " (inspect only)" : "")}: {spawned.Count} row(s), " +
                $"{empty} empty slot(s), {garage.LooseParts().Count} loose part(s) available", this);
        }

        /// Inspect mode gets the extra columns a mechanic would want; edit mode keeps
        /// the original one-line format so the workbench layout is unchanged.
        private string RowText(PartSlot slot, OwnedPart fitted, bool readOnly)
        {
            if (fitted == null) return $"{slot}: -- empty --";

            var db = garage.Database;
            var def = fitted.Definition(db);

            if (!readOnly)
                return $"{slot}: {def.displayName}  {Format.Percent(fitted.condition)} ({fitted.Tier(db)})";

            return $"{slot}: {def.displayName}  {Format.Percent(fitted.condition)} ({fitted.Tier(db)})  " +
                   $"{fitted.kmSinceNew:0} km  effect {Format.Percent(fitted.Effectiveness(db))}";
        }

        /// One row factory for both modes. Read-only rows hide their buttons rather
        /// than disabling them, so a dead-looking Fit button is never on screen.
        private GameObject SpawnRow(string text, bool interactive)
        {
            var row = Instantiate(slotRowPrefab, slotRoot);
            spawned.Add(row);

            var label = row.GetComponentInChildren<TMP_Text>();
            if (label) label.text = text;

            if (!interactive)
                foreach (var b in row.GetComponentsInChildren<Button>(true))
                    b.gameObject.SetActive(false);

            return row;
        }

        /// Fit = install the best loose part in that slot; Remove = unfit to inventory.
        private void Wire(Button button, PartSlot slot, bool fit)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                // Belt and braces: the buttons are hidden in inspect mode, but a stale
                // listener firing here would edit fitment from the middle of a stage.
                if (ReadOnly)
                {
                    GameLog.Refused(LogCat.UI, fit ? $"fit {slot}" : $"remove {slot}",
                                    "panel is open in inspect-only mode", this);
                    return;
                }

                var car = garage.ActiveCar;
                if (car == null)
                {
                    GameLog.Refused(LogCat.UI, fit ? $"fit {slot}" : $"remove {slot}", "no active car", this);
                    return;
                }

                if (!fit)
                {
                    GameLog.Action(LogCat.UI, "Remove clicked", $"slot {slot} on car '{car.instanceId}'", this);
                    service.Uninstall(car, slot);
                    return;
                }

                // "Best" is decided here, invisibly, so say what was chosen.
                var candidates = garage.LoosePartsInSlot(slot);
                OwnedPart best = null;
                foreach (var p in candidates)
                    if (best == null || p.condition > best.condition) best = p;

                if (best == null)
                {
                    GameLog.Refused(LogCat.UI, $"fit {slot}", "no loose parts in inventory for that slot", this);
                    return;
                }

                var bestDef = best.Definition(garage.Database);
                GameLog.Action(LogCat.UI, "Fit clicked",
                               $"slot {slot}: chose '{bestDef?.displayName}' at {best.condition:P0} " +
                               $"(best of {candidates.Count} candidate(s))", this);

                service.Install(car, best);
            });
        }
    }
}