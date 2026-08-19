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
    ///
    /// Install/Uninstall return bool, and a silent false is the most confusing
    /// outcome in the whole garage. Every false path now names its reason.
    public class GarageService : MonoBehaviour
    {
        [SerializeField] private GarageState garage;
        [SerializeField] private BoolVariable playerInGarage;
        [SerializeField] private GameEvent onFitmentChanged;

        public bool CanEditFitment => playerInGarage == null || playerInGarage.Value;

        /// Fits a loose part, auto-removing whatever occupies the slot.
        public bool Install(OwnedCar car, OwnedPart part)
        {
            if (!CanEditFitment)
            {
                GameLog.Refused(LogCat.Parts, "install part", "player is not inside the garage zone", this);
                return false;
            }
            if (car == null)
            {
                GameLog.Refused(LogCat.Parts, "install part", "no car supplied", this);
                return false;
            }
            if (part == null)
            {
                GameLog.Refused(LogCat.Parts, "install part", "no part supplied", this);
                return false;
            }

            var def = part.Definition(garage.Database);
            if (def == null)
            {
                GameLog.Refused(LogCat.Parts, $"install instance '{part.instanceId}'",
                                $"definition '{part.definitionId}' not found in the database", this);
                return false;
            }

            var existing = car.PartInSlot(def.slot, garage, garage.Database);
            if (existing != null)
            {
                car.installedPartInstanceIds.Remove(existing.instanceId);
                var exDef = existing.Definition(garage.Database);
                GameLog.Action(LogCat.Parts, "Displaced part returned to inventory",
                               $"'{exDef?.displayName}' at {existing.condition:P0} out of slot {def.slot}", this);
            }

            if (!car.installedPartInstanceIds.Contains(part.instanceId))
                car.installedPartInstanceIds.Add(part.instanceId);

            GameLog.Action(LogCat.Parts, "PART FITTED",
                           $"'{def.displayName}' ({def.slot}, {part.condition:P0} condition) " +
                           $"onto car '{car.instanceId}'", this);

            Notify();
            return true;
        }

        /// Unfits a part back to garage inventory.
        public bool Uninstall(OwnedCar car, PartSlot slot)
        {
            if (!CanEditFitment)
            {
                GameLog.Refused(LogCat.Parts, $"remove {slot} part", "player is not inside the garage zone", this);
                return false;
            }
            if (car == null)
            {
                GameLog.Refused(LogCat.Parts, $"remove {slot} part", "no car supplied", this);
                return false;
            }

            var part = car.PartInSlot(slot, garage, garage.Database);
            if (part == null)
            {
                GameLog.Refused(LogCat.Parts, $"remove {slot} part", "that slot is already empty", this);
                return false;
            }

            car.installedPartInstanceIds.Remove(part.instanceId);

            var def = part.Definition(garage.Database);
            GameLog.Action(LogCat.Parts, "PART REMOVED",
                           $"'{def?.displayName}' ({slot}) off car '{car.instanceId}' -> loose inventory", this);

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

            if (missing.Count > 0)
                GameLog.Verbose(LogCat.Parts,
                    $"Car '{car.instanceId}' is NOT roadworthy — missing/dead: {string.Join(", ", missing)}", this);

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
            if (newCar == null)
            {
                GameLog.Refused(LogCat.Garage, "swap active car", "no car supplied", this);
                return;
            }

            GameLog.Action(LogCat.Garage, "Swapping active car",
                           $"parking '{garage.activeCarInstanceId ?? "<none>"}', " +
                           $"taking out '{newCar.instanceId}' ({newCar.definitionId})", this);

            garage.SetActiveCar(newCar.instanceId);
        }

        private void Notify() { garage.OnGarageChanged?.Raise(); onFitmentChanged?.Raise(); }
    }
}
