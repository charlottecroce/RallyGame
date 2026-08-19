using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RallyGame.Core;
using RallyGame.Dealers;
using RallyGame.Economy;
using RallyGame.Garage;

namespace RallyGame.UI
{
    /// One view drives both dealers; kind decides which stock list and purchase path.
    ///
    /// Purchase clicks are logged before the transaction so a failed buy shows both
    /// the intent and the EconomyService refusal that followed it.
    public class DealerView : MonoBehaviour
    {
        [SerializeField] private DealerKind kind;
        [SerializeField] private WeeklyStockGenerator stockSource;
        [SerializeField] private EconomyService economy;
        [SerializeField] private GarageState garage;
        [SerializeField] private DefinitionDatabase database;
        [SerializeField] private GameEvent onStockChanged;

        [Header("Widgets")]
        [SerializeField] private Transform rowRoot;
        [SerializeField] private GameObject rowPrefab;   // needs TMP_Text + Button
        [SerializeField] private TMP_Text moneyLabel;

        private readonly List<GameObject> spawned = new List<GameObject>();

        private void OnEnable()
        {
            if (onStockChanged) onStockChanged.Register(Rebuild);
            GameLog.Action(LogCat.UI, $"{kind} dealer OPENED", $"cash {economy.Money:N0}", this);
            Rebuild();
        }

        private void OnDisable()
        {
            if (onStockChanged) onStockChanged.Unregister(Rebuild);
            GameLog.Action(LogCat.UI, $"{kind} dealer CLOSED", null, this);
        }

        public void Rebuild()
        {
            foreach (var go in spawned) if (go) Destroy(go);
            spawned.Clear();

            if (moneyLabel) moneyLabel.text = Utilities.Format.Money(economy.Money);

            var stock = kind == DealerKind.Cars ? stockSource.CarStock : stockSource.PartStock;

            int sold = 0;
            foreach (var item in stock.items)
            {
                if (item.sold) { sold++; continue; }
                var row = Instantiate(rowPrefab, rowRoot);
                spawned.Add(row);

                var label = row.GetComponentInChildren<TMP_Text>();
                var button = row.GetComponentInChildren<Button>();
                var captured = item;

                if (kind == DealerKind.Cars)
                {
                    var def = database.GetCar(item.definitionId);
                    if (def == null) continue;
                    if (label) label.text = $"{def.displayName}\n{Utilities.Format.Money(item.price)}";
                    if (button) button.onClick.AddListener(() => BuyCar(captured));
                }
                else
                {
                    var def = database.GetPart(item.definitionId);
                    if (def == null) continue;
                    if (label) label.text = $"{def.displayName} ({def.slot})\nq{def.quality:0.0}  {Utilities.Format.Percent(item.condition)}  {Utilities.Format.Money(item.price)}";
                    if (button) button.onClick.AddListener(() => BuyPart(captured));
                }
            }

            GameLog.Verbose(LogCat.UI,
                $"{kind} dealer rebuilt: {spawned.Count} row(s) shown, {sold} already sold, cash {economy.Money:N0}", this);
        }

        private void BuyPart(StockItem item)
        {
            var def = database.GetPart(item.definitionId);
            if (def == null) return;

            GameLog.Action(LogCat.UI, "Buy clicked",
                           $"part '{def.displayName}' at {item.condition:P0} for {item.price:N0} " +
                           $"(cash {economy.Money:N0})", this);

            if (economy.BuyPart(def, item.condition, item.price)) { item.sold = true; Rebuild(); }
        }

        /// Buying a car parks the old one and makes the new one active (GDD).
        private void BuyCar(StockItem item)
        {
            var def = database.GetCar(item.definitionId);
            if (def == null) return;

            GameLog.Action(LogCat.UI, "Buy clicked",
                           $"car '{def.displayName}' for {item.price:N0} (cash {economy.Money:N0})", this);

            if (!economy.BuyCar(def, item.price, out var bought)) return;

            garage.SetActiveCar(bought.instanceId);
            item.sold = true;
            Rebuild();
        }
    }
}
