using System.Collections.Generic;
using UnityEngine;
using RallyGame.Parts.Data;
using RallyGame.Vehicles.Data;
using RallyGame.Races.Data;

namespace RallyGame.Core
{
    /// String-ID -> definition asset lookup. Save files store IDs only (SO references
    /// do not survive JSON), so every load path resolves through here.
    /// Asset rather than scene singleton: consistent with the rest of the SO architecture.
    [CreateAssetMenu(menuName = "Rally/Definition Database", fileName = "DefinitionDatabase")]
    public class DefinitionDatabase : ScriptableObject
    {
        [SerializeField] private List<PartDefinition> allParts = new List<PartDefinition>();
        [SerializeField] private List<CarDefinition> allCars = new List<CarDefinition>();
        [SerializeField] private List<StageDefinition> allStages = new List<StageDefinition>();
        [SerializeField] private List<LocationDefinition> allLocations = new List<LocationDefinition>();

        public IReadOnlyList<PartDefinition> AllParts => allParts;
        public IReadOnlyList<CarDefinition> AllCars => allCars;
        public IReadOnlyList<StageDefinition> AllStages => allStages;
        public IReadOnlyList<LocationDefinition> AllLocations => allLocations;

        private Dictionary<string, PartDefinition> partsById;
        private Dictionary<string, CarDefinition> carsById;
        private Dictionary<string, StageDefinition> stagesById;
        private Dictionary<string, LocationDefinition> locationsById;

        private void OnEnable() { partsById = null; } // force rebuild after domain reload

        private void Build()
        {
            partsById = new Dictionary<string, PartDefinition>();
            carsById = new Dictionary<string, CarDefinition>();
            stagesById = new Dictionary<string, StageDefinition>();
            locationsById = new Dictionary<string, LocationDefinition>();

            foreach (var p in allParts) if (p && !string.IsNullOrEmpty(p.id)) partsById[p.id] = p;
            foreach (var c in allCars) if (c && !string.IsNullOrEmpty(c.id)) carsById[c.id] = c;
            foreach (var s in allStages) if (s && !string.IsNullOrEmpty(s.id)) stagesById[s.id] = s;
            foreach (var l in allLocations) if (l && !string.IsNullOrEmpty(l.id)) locationsById[l.id] = l;
        }

        private void EnsureBuilt() { if (partsById == null) Build(); }

        public PartDefinition GetPart(string id)
        {
            EnsureBuilt();
            if (id != null && partsById.TryGetValue(id, out var p)) return p;
            Debug.LogError($"[Definitions] Unknown part id '{id}'");
            return null;
        }

        public CarDefinition GetCar(string id)
        {
            EnsureBuilt();
            if (id != null && carsById.TryGetValue(id, out var c)) return c;
            Debug.LogError($"[Definitions] Unknown car id '{id}'");
            return null;
        }

        public StageDefinition GetStage(string id)
        {
            EnsureBuilt();
            return id != null && stagesById.TryGetValue(id, out var s) ? s : null;
        }

        public LocationDefinition GetLocation(string id)
        {
            EnsureBuilt();
            return id != null && locationsById.TryGetValue(id, out var l) ? l : null;
        }

#if UNITY_EDITOR
        /// Editor helper: pull every definition asset in the project into the lists.
        [ContextMenu("Rescan Project For Definitions")]
        private void Rescan()
        {
            allParts = LoadAll<PartDefinition>();
            allCars = LoadAll<CarDefinition>();
            allStages = LoadAll<StageDefinition>();
            allLocations = LoadAll<LocationDefinition>();
            Build();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private static List<T> LoadAll<T>() where T : ScriptableObject
        {
            var list = new List<T>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset) list.Add(asset);
            }
            return list;
        }
#endif
    }
}
