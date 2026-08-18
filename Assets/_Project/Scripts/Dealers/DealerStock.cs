using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.Dealers
{
    /// One item on a dealer shelf. Runtime/save state referencing a definition ID -
    /// never baked into the definition asset itself.
    [System.Serializable]
    public class StockItem
    {
        public string definitionId;
        public int price;
        [Range(0f, 1f)] public float condition = 1f;   // parts only; cars are sold at full
        public bool sold;
    }

    [System.Serializable]
    public class DealerStock
    {
        public int generatedForWeek = -1;
        public List<StockItem> items = new List<StockItem>();

        public void MarkSold(StockItem item) { item.sold = true; }
        public bool NeedsRestock(int week) => generatedForWeek != week;
    }
}
