using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Parts.Data;
using RallyGame.Parts.Runtime;
using RallyGame.Vehicles.Data;

namespace RallyGame.Garage
{
    /// Fitment rules. Per GDD: any car parked in the garage can use any part in
    /// inventory, but repairs are NOT available here - only mechanic/service.
    public class GarageService : MonoBehaviour
    {
        [SerializeField] private GarageState garage;
        [SerializeField] private BoolVariable playerInGarage;
        [SerializeField] private GameEvent onFitmentChanged;

        public bool CanEditFitment => playerInGarage == null || playerInGarage.Value;

        /// Fits a loose part, auto-removing whatever occupies the slot.
        public bool Install(OwnedCar car, OwnedPart part)
        {
            if (!CanEditFitment || car == null || part == null) return false;

            var def = part.Definition(garage.Database);
            if (def == null) return false;

            var existing = car.PartInSlot(def.slot, garage, garage.Database);
            if (existing != null) car.installedPartInstanceIds.Remove(existing.instanceId);

            if (!car.installedPartInstanceIds.Contains(part.instanceId))
                car.installedPartInstanceIds.Add(part.instanceId);

            Notify();
            return true;
        }

        /// Unfits a part back to garage inventory.
        public bool Uninstall(OwnedCar car, PartSlot slot)
        {
            if (!CanEditFitment || car == null) return false;
            var part = car.PartInSlot(slot, garage, garage.Database);
            if (part == null) return false;
            car.installedPartInstanceIds.Remove(part.instanceId);
            Notify();
            return true;
        }

        /// Every required slot filled and above zero condition.
        public bool IsRoadworthy(OwnedCar car, out List<PartSlot> missing)
        {
            missing = new List<PartSlot>();
            if (car == null) return false;

            foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
            {
                var part = car.PartInSlot(slot, garage, garage.Database);
                if (part == null) { if (SlotRequired(slot)) missing.Add(slot); continue; }
                var def = part.Definition(garage.Database);
                if (def != null && def.requiredToStart && part.condition <= 0f) missing.Add(slot);
            }
            return missing.Count == 0;
        }

        /// Slots the car physically cannot run without. Cosmetic/aux slots are optional.
        private bool SlotRequired(PartSlot slot) => slot switch
        {
            PartSlot.RallyLights => false,
            PartSlot.Turbo => false,
            PartSlot.Headlights => false,
            _ => true
        };

        /// Buying a car parks the current one and makes the new one active (GDD).
        public void SwapActiveCar(OwnedCar newCar)
        {
            if (newCar == null) return;
            garage.SetActiveCar(newCar.instanceId);
        }

        private void Notify() { garage.OnGarageChanged?.Raise(); onFitmentChanged?.Raise(); }
    }
}
