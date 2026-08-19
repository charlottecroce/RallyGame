using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RallyGame.Dealers;
using RallyGame.Garage;
using RallyGame.Races.Runtime;

namespace RallyGame.Core
{
    /// The one place allowed to reach across systems. Gathering save state is a
    /// cross-cutting concern, so it is centralised instead of smeared over components.
    ///
    /// Loading is logged step by step. A save that "works" but silently drops one
    /// system is the worst class of bug here, so each restore stage reports what it
    /// actually put back.
    public class SaveManager : MonoBehaviour
    {
        [Header("Systems")]
        [SerializeField] private GarageState garage;
        [SerializeField] private FloatVariable money;
        [SerializeField] private GameClock clock;
        [SerializeField] private WeatherSystem weather;
        [SerializeField] private WeatherVariable weatherVariable;
        [SerializeField] private WeekScheduler scheduler;
        [SerializeField] private WeeklyStockGenerator dealers;
        [SerializeField] private RaceState raceState;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Vehicles.Controllers.CarSpawner carSpawner;

        [Header("Channels")]
        [SerializeField] private GameEvent onSaved;
        [SerializeField] private GameEvent onLoaded;
        [SerializeField] private GameEvent onNewGame;

        [Header("Config")]
        [SerializeField] private string fileName = "rally_save.json";
        [SerializeField] private Vehicles.Data.CarDefinition starterCar;
        [SerializeField] private Vector3 defaultPlayerSpawn;

        private string Path => System.IO.Path.Combine(Application.persistentDataPath, fileName);

        /// GDD: save anywhere except during a race.
        public bool CanSave => raceState == null || raceState.CanSave;
        public bool HasSave => File.Exists(Path);

        private void Awake()
        {
            GameLog.Verbose(LogCat.Save, $"Save file path: {Path} (exists: {HasSave})", this);
        }

        // ---- write ---------------------------------------------------------

        public bool Save()
        {
            if (!CanSave)
            {
                GameLog.Refused(LogCat.Save, "save", "a race is in progress", this);
                return false;
            }

            GameLog.Action(LogCat.Save, "SAVING",
                           $"day {clock.DayIndex} {clock.TimeOfDay:0.0}h, {money.Value:N0} cash, " +
                           $"{garage.ownedCars.Count} car(s), {garage.allParts.Count} part(s)", this);

            var data = new PlayerSaveData
            {
                money = money.Value,
                ownedCars = garage.ownedCars,
                allParts = garage.allParts,
                activeCarInstanceId = garage.activeCarInstanceId,
                currentDay = clock.DayIndex,
                currentTimeOfDay = clock.TimeOfDay,
                currentWeather = weatherVariable.Value,
                schedule = scheduler.Current,
                partDealerStock = dealers.PartStock,
                carDealerStock = dealers.CarStock
            };

            if (playerRoot) { data.playerPosition = playerRoot.position; data.playerRotation = playerRoot.rotation; }
            else GameLog.Warn(LogCat.Save, "No playerRoot assigned — player position will not be saved.", this);

            if (carSpawner && carSpawner.Current)
            {
                data.carPosition = carSpawner.Current.transform.position;
                data.carRotation = carSpawner.Current.transform.rotation;
            }
            else
            {
                GameLog.Verbose(LogCat.Save, "No car in the world — car position not saved.", this);
            }

            foreach (var e in scheduler.Current.events) if (e.completed) data.completedRaceIds.Add(e.eventId);

            File.WriteAllText(Path, JsonUtility.ToJson(data, true));

            GameLog.Action(LogCat.Save, "Save written",
                           $"{new FileInfo(Path).Length:N0} bytes, {data.completedRaceIds.Count} completed race(s)", this);

            onSaved?.Raise();
            return true;
        }

        // ---- read ----------------------------------------------------------

        public bool Load()
        {
            if (!HasSave)
            {
                GameLog.Refused(LogCat.Save, "load", $"no save file at {Path}", this);
                return false;
            }

            GameLog.Action(LogCat.Save, "LOADING", Path, this);

            var data = JsonUtility.FromJson<PlayerSaveData>(File.ReadAllText(Path));
            if (data == null)
            {
                GameLog.Error(LogCat.Save, $"Save file at {Path} could not be parsed — it may be corrupt.", this);
                return false;
            }

            // Silent writes: restore everything before any listener reacts.
            money.SetSilent(data.money);
            GameLog.Verbose(LogCat.Save, $"  money restored: {data.money:N0}", this);

            garage.ownedCars = data.ownedCars ?? new List<Vehicles.Data.OwnedCar>();
            garage.allParts = data.allParts ?? new List<Parts.Runtime.OwnedPart>();
            garage.activeCarInstanceId = data.activeCarInstanceId;
            GameLog.Verbose(LogCat.Save,
                $"  garage restored: {garage.ownedCars.Count} car(s), {garage.allParts.Count} part(s), " +
                $"active '{garage.activeCarInstanceId ?? "<none>"}'", this);

            clock.SetTime(data.currentDay, data.currentTimeOfDay);
            weatherVariable.SetSilent(data.currentWeather);
            weather.Apply(data.currentWeather);
            GameLog.Verbose(LogCat.Save,
                $"  world restored: day {data.currentDay} {data.currentTimeOfDay:0.0}h, weather {data.currentWeather}", this);

            scheduler.Restore(data.schedule);

            int matched = 0;
            foreach (var id in data.completedRaceIds)
            {
                var e = scheduler.Current.Find(id);
                if (e != null) { e.completed = true; matched++; }
                else GameLog.Warn(LogCat.Save,
                    $"Saved completed race '{id}' is not in the regenerated schedule — it may re-appear as available.", this);
            }
            GameLog.Verbose(LogCat.Save,
                $"  schedule restored: {scheduler.Current.events.Count} event(s), {matched} marked complete", this);

            dealers.RestoreStock(data.partDealerStock, data.carDealerStock);
            GameLog.Verbose(LogCat.Save,
                $"  dealers restored: {data.partDealerStock?.items.Count ?? 0} part listing(s), " +
                $"{data.carDealerStock?.items.Count ?? 0} car listing(s)", this);

            if (playerRoot) playerRoot.SetPositionAndRotation(data.playerPosition, data.playerRotation);
            GameLog.Verbose(LogCat.Save, $"  player placed at {data.playerPosition:0.0}", this);

            garage.OnGarageChanged?.Raise();
            carSpawner?.Respawn();

            if (carSpawner && carSpawner.Current)
            {
                carSpawner.Current.Vehicle.Teleport(data.carPosition, data.carRotation);
                GameLog.Verbose(LogCat.Save, $"  car placed at {data.carPosition:0.0}", this);
            }

            GameLog.Action(LogCat.Save, "LOAD COMPLETE",
                           $"day {data.currentDay} {data.currentTimeOfDay:0.0}h, {data.money:N0} cash", this);

            onLoaded?.Raise();
            return true;
        }

        public void NewGame()
        {
            GameLog.Action(LogCat.Save, "NEW GAME",
                           $"starter car '{(starterCar ? starterCar.displayName : "<none assigned>")}'", this);

            garage.Clear();
            money.SetSilent(0f);
            clock.SetTime(0, 8f);

            if (starterCar) garage.AddCar(starterCar);
            else GameLog.Warn(LogCat.Save, "No starter car assigned — the player begins with no vehicle.", this);

            scheduler.Generate();
            dealers.Restock();

            // Unset spawn = leave the player wherever the scene placed them.
            if (playerRoot && defaultPlayerSpawn != Vector3.zero)
            {
                playerRoot.position = defaultPlayerSpawn;
                GameLog.Verbose(LogCat.Save, $"  player moved to default spawn {defaultPlayerSpawn:0.0}", this);
            }

            carSpawner?.Respawn();

            GameLog.Action(LogCat.Save, "New game ready", "day 0 (Monday) 08:00, 0 cash", this);
            onNewGame?.Raise();
        }

        public void DeleteSave()
        {
            if (HasSave)
            {
                GameLog.Action(LogCat.Save, "Save file DELETED", Path, this);
                File.Delete(Path);
            }
            else
            {
                GameLog.Refused(LogCat.Save, "delete save", "no save file exists", this);
            }
        }
    }
}
