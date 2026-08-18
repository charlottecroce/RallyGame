using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Garage;
using RallyGame.Parts.Data;
using RallyGame.Parts.Runtime;
using RallyGame.Vehicles.Data;

namespace RallyGame.Economy
{
    /// Every money movement goes through here so balancing lives in one asset
    /// and UI can preview costs without duplicating the maths.
    public class EconomyService : MonoBehaviour
    {
        [SerializeField] private FloatVariable money;
        [SerializeField] private GarageState garage;
        [SerializeField] private TireCompoundTable tireTable;
        [SerializeField] private PayoutTable payouts;
        [SerializeField] private GameEvent onMoneyChanged;

        public float Money => money.Value;
        public PayoutTable Payouts => payouts;

        public bool CanAfford(float amount) => money.Value >= amount;

        public bool TrySpend(float amount)
        {
            if (!CanAfford(amount)) return false;
            money.Value -= amount;
            onMoneyChanged?.Raise();
            return true;
        }

        public void Credit(float amount)
        {
            money.Value += Mathf.Max(0f, amount);
            onMoneyChanged?.Raise();
        }

        // ---- repairs -------------------------------------------------------

        /// Total to bring every fitted part on a car back to full condition.
        public int QuoteFullRepair(OwnedCar car)
        {
            int total = 0;
            foreach (var id in car.installedPartInstanceIds)
            {
                var part = garage.GetOwnedPart(id);
                var def = part?.Definition(garage.Database);
                if (def != null) total += def.RepairCost(part.condition);
            }
            return total;
        }

        public int QuotePartRepair(OwnedPart part)
        {
            var def = part?.Definition(garage.Database);
            return def == null ? 0 : def.RepairCost(part.condition);
        }

        public bool RepairAll(OwnedCar car)
        {
            int cost = QuoteFullRepair(car);
            if (!TrySpend(cost)) return false;
            foreach (var id in car.installedPartInstanceIds) garage.GetOwnedPart(id)?.Repair();
            garage.OnGarageChanged?.Raise();
            return true;
        }

        public bool RepairPart(OwnedPart part)
        {
            int cost = QuotePartRepair(part);
            if (!TrySpend(cost)) return false;
            part.Repair();
            garage.OnGarageChanged?.Raise();
            return true;
        }

        // ---- tires ---------------------------------------------------------

        public int TireChangeCost => tireTable ? tireTable.changeCost : 0;

        public bool ChangeTires(OwnedCar car, TireCompound compound)
        {
            if (car == null || !TrySpend(TireChangeCost)) return false;
            car.tires.Fit(compound);
            garage.OnGarageChanged?.Raise();
            return true;
        }

        // ---- trading -------------------------------------------------------

        public bool BuyPart(PartDefinition def, float condition, int price)
        {
            if (!TrySpend(price)) return false;
            garage.AddPart(def, condition);
            return true;
        }

        public bool SellPart(OwnedPart part)
        {
            var def = part?.Definition(garage.Database);
            if (def == null) return false;
            Credit(Mathf.RoundToInt(def.PriceForCondition(part.condition) * 0.5f));  // dealer margin
            garage.RemovePart(part);
            return true;
        }

        public bool BuyCar(CarDefinition def, int price, out OwnedCar bought)
        {
            bought = null;
            if (!TrySpend(price)) return false;
            bought = garage.AddCar(def);
            return true;
        }

        public bool SellCar(OwnedCar car, float valuation)
        {
            if (car == null || garage.ownedCars.Count <= 1) return false;   // never leave the player carless
            Credit(valuation);
            garage.RemoveCar(car);
            return true;
        }

        /// Trade-in value: base price scaled by average fitted-part condition.
        public int ValueOf(OwnedCar car)
        {
            var def = car.Definition(garage.Database);
            if (def == null) return 0;
            float sum = 0f; int n = 0;
            foreach (var id in car.installedPartInstanceIds)
            {
                var p = garage.GetOwnedPart(id);
                if (p != null) { sum += p.condition; n++; }
            }
            float avg = n == 0 ? 0.5f : sum / n;
            return Mathf.RoundToInt(def.basePrice * Mathf.Lerp(0.35f, 0.8f, avg));
        }
    }
}
