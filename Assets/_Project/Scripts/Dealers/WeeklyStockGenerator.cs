using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Utilities;

namespace RallyGame.Dealers
{
    /// Restocks both dealers on Monday 00:00. Deterministic from the week number so
    /// reloading a save cannot reroll the shelves.
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

        public DealerStock PartStock { get; private set; } = new DealerStock();
        public DealerStock CarStock { get; private set; } = new DealerStock();

        public int CurrentWeek => Mathf.FloorToInt(dayIndex.Value / 7f);

        private void OnEnable() { if (onWeekRolled) onWeekRolled.Register(Restock); }
        private void OnDisable() { if (onWeekRolled) onWeekRolled.Unregister(Restock); }

        private void Start() { if (PartStock.NeedsRestock(CurrentWeek)) Restock(); }

        public void Restock()
        {
            int week = CurrentWeek;
            GeneratePartStock(week);
            GenerateCarStock(week);
            onStockChanged?.Raise();
        }

        private void GeneratePartStock(int week)
        {
            var rng = new DeterministicRandom(week, "parts");
            var pool = database.AllParts;
            if (pool.Count == 0) return;

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
            if (candidates.Count == 0) return;

            var pick = candidates[rng.Range(0, candidates.Count)];
            PartStock.items[0] = new StockItem
            {
                definitionId = pick.id,
                condition = 1f,
                price = Mathf.RoundToInt(pick.basePrice * rng.Range(1f, 1.2f))
            };
        }

        private void GenerateCarStock(int week)
        {
            var rng = new DeterministicRandom(week, "cars");
            var pool = database.AllCars;
            if (pool.Count == 0) return;

            var previous = CarStock;
            var next = new DealerStock { generatedForWeek = week };

            // Carry most listings over so the forecourt only shifts slightly each week.
            foreach (var old in previous.items)
            {
                if (next.items.Count >= carSlots) break;
                if (!old.sold && rng.Value01() < carCarryOver)
                    next.items.Add(new StockItem { definitionId = old.definitionId, price = old.price, condition = 1f });
            }

            while (next.items.Count < carSlots)
            {
                var def = pool[rng.Range(0, pool.Count)];
                next.items.Add(new StockItem
                {
                    definitionId = def.id,
                    condition = 1f,
                    price = Mathf.RoundToInt(def.basePrice * rng.Range(0.92f, 1.12f))
                });
            }

            CarStock = next;
        }

        // Save hooks - stock is save state, not definition data.
        public void RestoreStock(DealerStock parts, DealerStock cars)
        {
            if (parts != null) PartStock = parts;
            if (cars != null) CarStock = cars;
            onStockChanged?.Raise();
        }
    }
}
