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
    public class GarageView : MonoBehaviour
    {
        [SerializeField] private GarageState garage;
        [SerializeField] private GarageService service;
        [SerializeField] private Transform slotRoot;
        [Tooltip("Row prefab: needs one TMP_Text and two Buttons (fit, remove).")]
        [SerializeField] private GameObject slotRowPrefab;
        [SerializeField] private TMP_Text carLabel;

        private readonly List<GameObject> spawned = new List<GameObject>();

        private void OnEnable()
        {
            if (garage.OnGarageChanged) garage.OnGarageChanged.Register(Rebuild);
            GameLog.Action(LogCat.UI, "Garage panel OPENED",
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

            var def = car.Definition(garage.Database);
            if (carLabel) carLabel.text = $"{def.displayName}   {car.odometerKm:0} km   tires {car.tires.compound} {Format.Percent(1f - car.tires.wear)}";

            int empty = 0;
            foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
            {
                var fitted = car.PartInSlot(slot, garage, garage.Database);
                if (fitted == null) empty++;

                var row = Instantiate(slotRowPrefab, slotRoot);
                spawned.Add(row);

                var rowLabel = row.GetComponentInChildren<TMP_Text>();
                if (rowLabel)
                {
                    rowLabel.text = fitted == null
                        ? $"{slot}: -- empty --"
                        : $"{slot}: {fitted.Definition(garage.Database).displayName}  {Format.Percent(fitted.condition)} ({fitted.Tier(garage.Database)})";
                }

                var buttons = row.GetComponentsInChildren<Button>(true);
                if (buttons.Length > 0) Wire(buttons[0], slot, true);
                if (buttons.Length > 1) Wire(buttons[1], slot, false);

                if (buttons.Length < 2)
                    GameLog.Warn(LogCat.UI,
                        $"Slot row prefab has only {buttons.Length} Button(s); two are needed (fit, remove).", this);
            }

            GameLog.Verbose(LogCat.UI,
                $"Garage panel rebuilt: {spawned.Count} slot row(s), {empty} empty, " +
                $"{garage.LooseParts().Count} loose part(s) available", this);
        }

        /// Fit = install the best loose part in that slot; Remove = unfit to inventory.
        private void Wire(Button button, PartSlot slot, bool fit)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
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
