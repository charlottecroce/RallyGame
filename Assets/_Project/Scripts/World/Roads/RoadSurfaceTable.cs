using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;

namespace RallyGame.World.Roads
{
    /// Every surface in the game in one asset, plus the fallback used when the car is
    /// not on a road at all. Mirrors TireCompoundTable so the two tuning files feel
    /// the same to edit.
    ///
    /// Adding a surface: create the RoadSurface asset, drag it into this list. Done.
    [CreateAssetMenu(menuName = "Rally/Definitions/Road Surface Table", fileName = "RoadSurfaces")]
    public class RoadSurfaceTable : ScriptableObject
    {
        [SerializeField] private List<RoadSurface> surfaces = new List<RoadSurface>();
        [Tooltip("Used wherever no road is tagged — terrain, grass, the wrong side of a hedge.")]
        [SerializeField] private RoadSurface offRoad;

        public IReadOnlyList<RoadSurface> All => surfaces;
        public RoadSurface OffRoad => offRoad;

        private Dictionary<string, RoadSurface> byId;

        private void OnEnable() { byId = null; }   // rebuild after domain reload

        public RoadSurface Get(string id)
        {
            if (byId == null) Build();
            if (id != null && byId.TryGetValue(id, out var s)) return s;

            GameLog.Warn(LogCat.World, $"Unknown road surface id '{id}' — is it in the table's list?", this);
            return offRoad;
        }

        /// Final multiplier the car applies to its friction curves.
        /// Null surface means "not on a road", which resolves to the off-road entry.
        public float GripMultiplier(RoadSurface surface, WeatherType weather)
        {
            var s = surface ? surface : offRoad;
            return s ? s.GripMultiplier(weather) : 1f;
        }

        private void Build()
        {
            byId = new Dictionary<string, RoadSurface>();
            foreach (var s in surfaces)
                if (s && !string.IsNullOrEmpty(s.id)) byId[s.id] = s;

            if (offRoad == null)
                GameLog.Warn(LogCat.World,
                    "Road surface table has no off-road fallback — driving off the road will read as neutral grip.", this);

            GameLog.Verbose(LogCat.World, $"Road surfaces indexed: {byId.Count} entry(s)", this);
        }
    }
}