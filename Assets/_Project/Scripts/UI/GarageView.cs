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
            Rebuild();
        }

        private void OnDisable() { if (garage.OnGarageChanged) garage.OnGarageChanged.Unregister(Rebuild); }

        public void Rebuild()
        {
            foreach (var go in spawned) if (go) Destroy(go);
            spawned.Clear();

            var car = garage.ActiveCar;
            if (car == null) { if (carLabel) carLabel.text = "No car"; return; }

            var def = car.Definition(garage.Database);
            if (carLabel) carLabel.text = $"{def.displayName}   {car.odometerKm:0} km   tires {car.tires.compound} {Format.Percent(1f - car.tires.wear)}";

            foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
            {
                var fitted = car.PartInSlot(slot, garage, garage.Database);
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
            }
        }

        /// Fit = install the best loose part in that slot; Remove = unfit to inventory.
        private void Wire(Button button, PartSlot slot, bool fit)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                var car = garage.ActiveCar;
                if (car == null) return;

                if (!fit) { service.Uninstall(car, slot); return; }

                OwnedPart best = null;
                foreach (var p in garage.LoosePartsInSlot(slot))
                    if (best == null || p.condition > best.condition) best = p;

                if (best != null) service.Install(car, best);
            });
        }
    }
}
