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
    ///
    /// Because it is the single choke point for currency, every debit and credit
    /// is logged with the balance before and after — an economy bug becomes a
    /// readable ledger in the console rather than a mystery.
    public class EconomyService : MonoBehaviour
    {
        [SerializeField] private FloatVariable money;
        [SerializeField] private GarageState garage;
        [SerializeField] private TireCompoundTable tireTable;
        [SerializeField] private PayoutTable payouts;
        [SerializeField] private GameEvent onMoneyChanged;

        [Header("Debug")]
        [Tooltip("Log price quotes as well as actual transactions. Off by default — UI asks for quotes often.")]
        [SerializeField] private bool logQuotes = false;

        public float Money => money.Value;
        public PayoutTable Payouts => payouts;

        public bool CanAfford(float amount) => money.Value >= amount;

        public bool TrySpend(float amount)
        {
            if (!CanAfford(amount))
            {
                GameLog.Refused(LogCat.Economy, $"spend {amount:N0}",
                                $"balance is {money.Value:N0}, short by {amount - money.Value:N0}", this);
                return false;
            }

            float before = money.Value;
            money.Value -= amount;
            GameLog.Action(LogCat.Economy, $"DEBIT {amount:N0}", $"{before:N0} -> {money.Value:N0}", this);

            onMoneyChanged?.Raise();
            return true;
        }

        public void Credit(float amount)
        {
            float applied = Mathf.Max(0f, amount);
            float before = money.Value;
            money.Value += applied;

            GameLog.Action(LogCat.Economy, $"CREDIT {applied:N0}", $"{before:N0} -> {money.Value:N0}", this);
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

            if (logQuotes)
                GameLog.Verbose(LogCat.Economy, $"Quote: full repair of '{car.instanceId}' = {total:N0}", this);

            return total;
        }

        public int QuotePartRepair(OwnedPart part)
        {
            var def = part?.Definition(garage.Database);
            int cost = def == null ? 0 : def.RepairCost(part.condition);

            if (logQuotes && def != null)
                GameLog.Verbose(LogCat.Economy,
                    $"Quote: repair '{def.displayName}' at {part.condition:P0} condition = {cost:N0}", this);

            return cost;
        }

        public bool RepairAll(OwnedCar car)
        {
            int cost = QuoteFullRepair(car);

            if (!TrySpend(cost))
            {
                GameLog.Refused(LogCat.Economy, $"repair all on '{car.instanceId}'", $"cannot afford {cost:N0}", this);
                return false;
            }

            int repaired = 0;
            foreach (var id in car.installedPartInstanceIds)
            {
                var p = garage.GetOwnedPart(id);
                if (p != null) { p.Repair(); repaired++; }
            }

            GameLog.Action(LogCat.Parts, "Full repair complete",
                           $"car '{car.instanceId}', {repaired} part(s) restored, cost {cost:N0}", this);

            garage.OnGarageChanged?.Raise();
            return true;
        }

        public bool RepairPart(OwnedPart part)
        {
            int cost = QuotePartRepair(part);
            var def = part?.Definition(garage.Database);

            if (!TrySpend(cost))
            {
                GameLog.Refused(LogCat.Economy, $"repair '{def?.displayName ?? "<unknown>"}'",
                                $"cannot afford {cost:N0}", this);
                return false;
            }

            float before = part.condition;
            part.Repair();

            GameLog.Action(LogCat.Parts, "Part repaired",
                           $"'{def?.displayName}' {before:P0} -> {part.condition:P0}, cost {cost:N0}", this);

            garage.OnGarageChanged?.Raise();
            return true;
        }

        // ---- tires ---------------------------------------------------------

        public int TireChangeCost => tireTable ? tireTable.changeCost : 0;

        public bool ChangeTires(OwnedCar car, TireCompound compound)
        {
            if (car == null)
            {
                GameLog.Refused(LogCat.Parts, "change tires", "no car supplied", this);
                return false;
            }
            if (!TrySpend(TireChangeCost))
            {
                GameLog.Refused(LogCat.Parts, $"fit {compound} tires", $"cannot afford {TireChangeCost:N0}", this);
                return false;
            }

            car.tires.Fit(compound);
            GameLog.Action(LogCat.Parts, "Tires changed",
                           $"car '{car.instanceId}' now on {compound}, cost {TireChangeCost:N0}", this);

            garage.OnGarageChanged?.Raise();
            return true;
        }

        // ---- trading -------------------------------------------------------

        public bool BuyPart(PartDefinition def, float condition, int price)
        {
            if (!TrySpend(price))
            {
                GameLog.Refused(LogCat.Dealer, $"buy part '{def?.displayName}'", $"cannot afford {price:N0}", this);
                return false;
            }

            garage.AddPart(def, condition);
            GameLog.Action(LogCat.Dealer, "PART BOUGHT",
                           $"'{def.displayName}' ({def.id}) at {condition:P0} condition for {price:N0}", this);
            return true;
        }

        public bool SellPart(OwnedPart part)
        {
            var def = part?.Definition(garage.Database);
            if (def == null)
            {
                GameLog.Refused(LogCat.Dealer, "sell part", "part has no resolvable definition", this);
                return false;
            }

            int proceeds = Mathf.RoundToInt(def.PriceForCondition(part.condition) * 0.5f);  // dealer margin
            Credit(proceeds);
            garage.RemovePart(part);

            GameLog.Action(LogCat.Dealer, "PART SOLD",
                           $"'{def.displayName}' at {part.condition:P0} condition for {proceeds:N0} (50% dealer margin)", this);
            return true;
        }

        public bool BuyCar(CarDefinition def, int price, out OwnedCar bought)
        {
            bought = null;

            if (!TrySpend(price))
            {
                GameLog.Refused(LogCat.Dealer, $"buy car '{def?.displayName}'", $"cannot afford {price:N0}", this);
                return false;
            }

            bought = garage.AddCar(def);
            GameLog.Action(LogCat.Dealer, "CAR BOUGHT",
                           $"'{def.displayName}' ({def.id}) for {price:N0}, new instance '{bought?.instanceId}'", this);
            return true;
        }

        public bool SellCar(OwnedCar car, float valuation)
        {
            if (car == null)
            {
                GameLog.Refused(LogCat.Dealer, "sell car", "no car supplied", this);
                return false;
            }
            if (garage.ownedCars.Count <= 1)
            {
                // never leave the player carless
                GameLog.Refused(LogCat.Dealer, $"sell car '{car.instanceId}'", "it is the player's only car", this);
                return false;
            }

            Credit(valuation);
            garage.RemoveCar(car);

            GameLog.Action(LogCat.Dealer, "CAR SOLD",
                           $"'{car.instanceId}' ({car.definitionId}) for {valuation:N0}, " +
                           $"{garage.ownedCars.Count} car(s) remaining", this);
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
            int value = Mathf.RoundToInt(def.basePrice * Mathf.Lerp(0.35f, 0.8f, avg));

            if (logQuotes)
                GameLog.Verbose(LogCat.Economy,
                    $"Quote: trade-in '{car.instanceId}' base {def.basePrice:N0} x avg condition {avg:P0} = {value:N0}", this);

            return value;
        }
    }
}
