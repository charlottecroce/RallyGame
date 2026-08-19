using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Utilities;

namespace RallyGame.Dealers
{
    /// Restocks both dealers on Monday 00:00. Deterministic from the week number so
    /// reloading a save cannot reroll the shelves.
    ///
    /// Restocking happens once a week, so the full shelf is printed at Verbose. If a
    /// price or condition looks wrong in-game, the roll that produced it is in the log.
    public class WeeklyStockGenerator : MonoBehaviour
    {
        [SerializeField] private DefinitionDatabase database;
        [SerializeField] private IntVariable dayIndex;
        [SerializeField] private GameEvent onWeekRolled;
        [SerializeField] private GameEvent onStockChanged;

        [Header("Part dealer")]
        [SerializeField] private int partSlots = 8;
        [Tooltip("Quality at or above this counts as the guaranteed high-quality item.")]
        [SerializeField, Range(0f, 1f)] private float highQualityThreshold = 0.75f;

        [Header("Car dealer")]
        [SerializeField] private int carSlots = 4;
        [Tooltip("Chance an existing car listing is kept week to week (slight variation).")]
        [SerializeField, Range(0f, 1f)] private float carCarryOver = 0.65f;

        [Header("Debug")]
        [Tooltip("Print every generated listing, not just the totals.")]
        [SerializeField] private bool logEveryListing = true;

        public DealerStock PartStock { get; private set; } = new DealerStock();
        public DealerStock CarStock { get; private set; } = new DealerStock();

        public int CurrentWeek => Mathf.FloorToInt(dayIndex.Value / 7f);

        private void OnEnable() { if (onWeekRolled) onWeekRolled.Register(Restock); }
        private void OnDisable() { if (onWeekRolled) onWeekRolled.Unregister(Restock); }

        private void Start()
        {
            if (PartStock.NeedsRestock(CurrentWeek))
            {
                GameLog.Verbose(LogCat.Dealer, $"Stock is stale for week {CurrentWeek} — restocking at startup.", this);
                Restock();
            }
        }

        public void Restock()
        {
            int week = CurrentWeek;

            GameLog.Action(LogCat.Dealer, "RESTOCKING DEALERS", $"week {week + 1}", this);

            GeneratePartStock(week);
            GenerateCarStock(week);

            GameLog.Action(LogCat.Dealer, "Restock complete",
                           $"{PartStock.items.Count} part listing(s), {CarStock.items.Count} car listing(s)", this);

            onStockChanged?.Raise();
        }

        private void GeneratePartStock(int week)
        {
            var rng = new DeterministicRandom(week, "parts");
            var pool = database.AllParts;
            if (pool.Count == 0)
            {
                GameLog.Warn(LogCat.Dealer, "Part dealer has nothing to sell — the database contains no parts.", this);
                return;
            }

            PartStock = new DealerStock { generatedForWeek = week };

            for (int i = 0; i < partSlots; i++)
            {
                var def = pool[rng.Range(0, pool.Count)];
                float condition = rng.Range(0.7f, 1f);
                PartStock.items.Add(new StockItem
                {
                    definitionId = def.id,
                    condition = condition,
                    price = Mathf.RoundToInt(def.PriceForCondition(condition) * rng.Range(0.9f, 1.15f))
                });
            }

            EnsureHighQualityItem(rng, week);

            if (logEveryListing)
                foreach (var item in PartStock.items)
                {
                    var d = database.GetPart(item.definitionId);
                    GameLog.Verbose(LogCat.Dealer,
                        $"  PART  {d?.displayName ?? item.definitionId} ({d?.slot}) " +
                        $"q{d?.quality:0.00} cond {item.condition:P0} — {item.price:N0}", this);
                }
        }

        /// GDD: the part dealer always carries at least one high-quality item.
        private void EnsureHighQualityItem(DeterministicRandom rng, int week)
        {
            foreach (var item in PartStock.items)
            {
                var d = database.GetPart(item.definitionId);
                if (d != null && d.quality >= highQualityThreshold) return;
            }

            var candidates = new List<Parts.Data.PartDefinition>();
            foreach (var p in database.AllParts) if (p.quality >= highQualityThreshold) candidates.Add(p);

            if (candidates.Count == 0)
            {
                GameLog.Warn(LogCat.Dealer,
                    $"No part in the database has quality >= {highQualityThreshold:0.00}, so the guaranteed " +
                    "high-quality slot could not be filled.", this);
                return;
            }

            var pick = candidates[rng.Range(0, candidates.Count)];
            PartStock.items[0] = new StockItem
            {
                definitionId = pick.id,
                condition = 1f,
                price = Mathf.RoundToInt(pick.basePrice * rng.Range(1f, 1.2f))
            };

            GameLog.Verbose(LogCat.Dealer,
                $"  guaranteed high-quality slot filled with '{pick.displayName}' (q{pick.quality:0.00})", this);
        }

        private void GenerateCarStock(int week)
        {
            var rng = new DeterministicRandom(week, "cars");
            var pool = database.AllCars;
            if (pool.Count == 0)
            {
                GameLog.Warn(LogCat.Dealer, "Car dealer has nothing to sell — the database contains no cars.", this);
                return;
            }

            var previous = CarStock;
            var next = new DealerStock { generatedForWeek = week };

            // Carry most listings over so the forecourt only shifts slightly each week.
            int carried = 0;
            foreach (var old in previous.items)
            {
                if (next.items.Count >= carSlots) break;
                if (!old.sold && rng.Value01() < carCarryOver)
                {
                    next.items.Add(new StockItem { definitionId = old.definitionId, price = old.price, condition = 1f });
                    carried++;
                }
            }

            int fresh = 0;
            while (next.items.Count < carSlots)
            {
                var def = pool[rng.Range(0, pool.Count)];
                next.items.Add(new StockItem
                {
                    definitionId = def.id,
                    condition = 1f,
                    price = Mathf.RoundToInt(def.basePrice * rng.Range(0.92f, 1.12f))
                });
                fresh++;
            }

            CarStock = next;

            GameLog.Verbose(LogCat.Dealer,
                $"  forecourt: {carried} listing(s) carried over, {fresh} new arrival(s)", this);

            if (logEveryListing)
                foreach (var item in CarStock.items)
                {
                    var d = database.GetCar(item.definitionId);
                    GameLog.Verbose(LogCat.Dealer, $"  CAR   {d?.displayName ?? item.definitionId} — {item.price:N0}", this);
                }
        }

        // Save hooks - stock is save state, not definition data.
        public void RestoreStock(DealerStock parts, DealerStock cars)
        {
            if (parts != null) PartStock = parts;
            if (cars != null) CarStock = cars;

            GameLog.Action(LogCat.Dealer, "Dealer stock restored from save",
                           $"{PartStock.items.Count} part listing(s), {CarStock.items.Count} car listing(s)", this);

            onStockChanged?.Raise();
        }
    }
}
