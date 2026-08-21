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
    ///
    /// Build() now VALIDATES while it indexes rather than trusting the lists. Two things
    /// used to slip through silently:
    ///   1. A placeholder ID ("Part_New"). Indexed happily, then written into a save by
    ///      the stock generator — and the day the asset gets a real ID, that save points
    ///      at a string that exists nowhere. Such assets are now excluded and reported.
    ///   2. A duplicate ID. `byId[id] = asset` meant the last one silently won and the
    ///      other became unreachable, with no error until something tried to load it.
    ///      Now the first one wins and the collision is reported.
    ///
    /// AllParts/AllCars/etc. return the VALIDATED lists, so a broken definition cannot be
    /// picked by a generator and baked into a save in the first place.
    [CreateAssetMenu(menuName = "Rally/Definition Database", fileName = "DefinitionDatabase")]
    public class DefinitionDatabase : ScriptableObject
    {
        [SerializeField] private List<PartDefinition> allParts = new List<PartDefinition>();
        [SerializeField] private List<CarDefinition> allCars = new List<CarDefinition>();
        [SerializeField] private List<StageDefinition> allStages = new List<StageDefinition>();
        [SerializeField] private List<LocationDefinition> allLocations = new List<LocationDefinition>();

        // Post-validation views. Everything that PICKS a definition reads these.
        private readonly List<PartDefinition> validParts = new List<PartDefinition>();
        private readonly List<CarDefinition> validCars = new List<CarDefinition>();
        private readonly List<StageDefinition> validStages = new List<StageDefinition>();
        private readonly List<LocationDefinition> validLocations = new List<LocationDefinition>();

        public IReadOnlyList<PartDefinition> AllParts { get { EnsureBuilt(); return validParts; } }
        public IReadOnlyList<CarDefinition> AllCars { get { EnsureBuilt(); return validCars; } }
        public IReadOnlyList<StageDefinition> AllStages { get { EnsureBuilt(); return validStages; } }
        public IReadOnlyList<LocationDefinition> AllLocations { get { EnsureBuilt(); return validLocations; } }

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

            Index(allParts, validParts, partsById, a => a.id, PartDefinition.IdPrefix, "Part");
            Index(allCars, validCars, carsById, a => a.id, CarDefinition.IdPrefix, "Car");
            Index(allStages, validStages, stagesById, a => a.id, StageDefinition.IdPrefix, "Stage");
            Index(allLocations, validLocations, locationsById, a => a.id, LocationDefinition.IdPrefix, "Location");

            GameLog.Verbose(LogCat.Core,
                $"Definitions indexed: {partsById.Count} part(s), {carsById.Count} car(s), " +
                $"{stagesById.Count} stage(s), {locationsById.Count} location(s)", this);
        }

        /// One pass, one set of rules, four types. An asset that fails validation is left
        /// out of BOTH the dictionary and the valid list — it is not content until it has
        /// a usable ID, and pretending otherwise is what put "Part_New" in a save file.
        private void Index<T>(List<T> source, List<T> valid, Dictionary<string, T> byId,
                              System.Func<T, string> idOf, string prefix, string label)
            where T : ScriptableObject
        {
            valid.Clear();

            foreach (var asset in source)
            {
                if (asset == null)
                {
                    GameLog.Warn(LogCat.Core,
                        $"The {label} list has an empty slot — remove it or refill it.", this);
                    continue;
                }

                string id = idOf(asset);

                if (DefinitionId.IsPlaceholder(id, prefix))
                {
                    GameLog.Error(LogCat.Core,
                        $"{label} asset '{asset.name}' still has the placeholder id '{id}'. It has been " +
                        "left out of the database — give it a real ID (or run Stamp Placeholder IDs " +
                        "From Asset Names), or nothing will ever resolve to it.", asset);
                    continue;
                }

                if (byId.TryGetValue(id, out var existing))
                {
                    GameLog.Error(LogCat.Core,
                        $"Duplicate {label} id '{id}' on '{asset.name}' and '{existing.name}'. Keeping " +
                        $"'{existing.name}' — the other is unreachable until you change its ID.", asset);
                    continue;
                }

                byId[id] = asset;
                valid.Add(asset);
            }
        }

        private void EnsureBuilt() { if (partsById == null) Build(); }

        // ---- lookup --------------------------------------------------------
        // TryGet* are the quiet versions. Use them anywhere a miss is EXPECTED and
        // recoverable — restoring a save written against older definitions, for
        // instance, where one dead listing should not log an error on every UI rebuild.

        public bool TryGetPart(string id, out PartDefinition part)
        {
            EnsureBuilt();
            part = null;
            return !string.IsNullOrEmpty(id) && partsById.TryGetValue(id, out part);
        }

        public bool TryGetCar(string id, out CarDefinition car)
        {
            EnsureBuilt();
            car = null;
            return !string.IsNullOrEmpty(id) && carsById.TryGetValue(id, out car);
        }

        public bool TryGetStage(string id, out StageDefinition stage)
        {
            EnsureBuilt();
            stage = null;
            return !string.IsNullOrEmpty(id) && stagesById.TryGetValue(id, out stage);
        }

        public bool TryGetLocation(string id, out LocationDefinition location)
        {
            EnsureBuilt();
            location = null;
            return !string.IsNullOrEmpty(id) && locationsById.TryGetValue(id, out location);
        }

        public PartDefinition GetPart(string id)
        {
            if (TryGetPart(id, out var p)) return p;
            GameLog.Error(LogCat.Core, $"Unknown part id '{id}' — is it in the database's Parts list?", this);
            return null;
        }

        public CarDefinition GetCar(string id)
        {
            if (TryGetCar(id, out var c)) return c;
            GameLog.Error(LogCat.Core, $"Unknown car id '{id}' — is it in the database's Cars list?", this);
            return null;
        }

        public StageDefinition GetStage(string id)
        {
            if (TryGetStage(id, out var s)) return s;
            GameLog.Warn(LogCat.Core, $"Unknown stage id '{id}' — is it in the database's Stages list?", this);
            return null;
        }

        public LocationDefinition GetLocation(string id)
        {
            if (TryGetLocation(id, out var l)) return l;
            GameLog.Warn(LogCat.Core, $"Unknown location id '{id}' — is it in the database's Locations list?", this);
            return null;
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

            GameLog.Info(LogCat.Core,
                $"Rescan complete: {allParts.Count} part(s), {allCars.Count} car(s), " +
                $"{allStages.Count} stage(s), {allLocations.Count} location(s)", this);
        }

        /// The pass to run before trusting a save. Build() already shouts about
        /// placeholders and duplicates; this adds the two it cannot see from the lists
        /// alone — assets on disk that were never added, and car default builds whose
        /// parts do not resolve. That second one is the "car did not get its default
        /// part installed" bug, caught at author time instead of at load time.
        [ContextMenu("Audit Definition IDs")]
        private void Audit()
        {
            Build();

            int problems = 0;
            problems += ReportMissing(LoadAll<PartDefinition>(), allParts, "Part");
            problems += ReportMissing(LoadAll<CarDefinition>(), allCars, "Car");
            problems += ReportMissing(LoadAll<StageDefinition>(), allStages, "Stage");
            problems += ReportMissing(LoadAll<LocationDefinition>(), allLocations, "Location");

            foreach (var car in allCars)
            {
                if (car == null) continue;

                for (int i = 0; i < car.defaultParts.Count; i++)
                {
                    var part = car.defaultParts[i];

                    if (part == null)
                    {
                        GameLog.Error(LogCat.Core,
                            $"Car '{car.name}' has an empty entry at index {i} of Default Parts.", car);
                        problems++;
                    }
                    else if (!TryGetPart(part.id, out _))
                    {
                        GameLog.Error(LogCat.Core,
                            $"Car '{car.name}' fits '{part.name}' by default, but its id '{part.id}' does " +
                            "not resolve through the database — that slot will be empty on a new game.", car);
                        problems++;
                    }
                }
            }

            GameLog.Info(LogCat.Core,
                $"Audit finished: {problems} problem(s) beyond anything already logged above.", this);
        }

        private int ReportMissing<T>(List<T> onDisk, List<T> listed, string label) where T : ScriptableObject
        {
            int missing = 0;

            foreach (var asset in onDisk)
            {
                if (listed.Contains(asset)) continue;

                GameLog.Warn(LogCat.Core,
                    $"{label} asset '{asset.name}' exists in the project but is not in the database — " +
                    "run Rescan Project For Definitions.", asset);
                missing++;
            }

            return missing;
        }

        /// Repair pass for assets authored before the inline defaults were removed.
        /// Touches ONLY placeholder IDs, so nothing a save already points at can move
        /// under it. Assets still named "Part_" are skipped rather than given an equally
        /// useless ID — rename them and run this again.
        [ContextMenu("Stamp Placeholder IDs From Asset Names")]
        private void StampPlaceholderIds()
        {
            int changed = 0;
            changed += Stamp(LoadAll<PartDefinition>(), PartDefinition.IdPrefix, a => a.id, (a, v) => a.id = v);
            changed += Stamp(LoadAll<CarDefinition>(), CarDefinition.IdPrefix, a => a.id, (a, v) => a.id = v);
            changed += Stamp(LoadAll<StageDefinition>(), StageDefinition.IdPrefix, a => a.id, (a, v) => a.id = v);
            changed += Stamp(LoadAll<LocationDefinition>(), LocationDefinition.IdPrefix, a => a.id, (a, v) => a.id = v);

            if (changed > 0) UnityEditor.AssetDatabase.SaveAssets();
            Build();

            GameLog.Info(LogCat.Core, $"Stamped {changed} placeholder id(s) from asset names.", this);
        }

        private int Stamp<T>(List<T> assets, string prefix,
                             System.Func<T, string> get, System.Action<T, string> set)
            where T : ScriptableObject
        {
            int changed = 0;

            foreach (var asset in assets)
            {
                string current = get(asset);
                string resolved = DefinitionId.Resolve(current, asset.name, prefix);
                if (resolved == current) continue;

                set(asset, resolved);
                UnityEditor.EditorUtility.SetDirty(asset);
                GameLog.Info(LogCat.Core, $"  '{asset.name}': id '{current}' -> '{resolved}'", asset);
                changed++;
            }

            return changed;
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