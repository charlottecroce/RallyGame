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

        // ---- write ---------------------------------------------------------

        public bool Save()
        {
            if (!CanSave) { Debug.Log("[Save] Blocked: race in progress."); return false; }

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
            if (carSpawner && carSpawner.Current)
            {
                data.carPosition = carSpawner.Current.transform.position;
                data.carRotation = carSpawner.Current.transform.rotation;
            }

            foreach (var e in scheduler.Current.events) if (e.completed) data.completedRaceIds.Add(e.eventId);

            File.WriteAllText(Path, JsonUtility.ToJson(data, true));
            onSaved?.Raise();
            return true;
        }

        // ---- read ----------------------------------------------------------

        public bool Load()
        {
            if (!HasSave) return false;

            var data = JsonUtility.FromJson<PlayerSaveData>(File.ReadAllText(Path));
            if (data == null) return false;

            // Silent writes: restore everything before any listener reacts.
            money.SetSilent(data.money);
            garage.ownedCars = data.ownedCars ?? new List<Vehicles.Data.OwnedCar>();
            garage.allParts = data.allParts ?? new List<Parts.Runtime.OwnedPart>();
            garage.activeCarInstanceId = data.activeCarInstanceId;

            clock.SetTime(data.currentDay, data.currentTimeOfDay);
            weatherVariable.SetSilent(data.currentWeather);
            weather.Apply(data.currentWeather);

            scheduler.Restore(data.schedule);
            foreach (var id in data.completedRaceIds)
            {
                var e = scheduler.Current.Find(id);
                if (e != null) e.completed = true;
            }
            dealers.RestoreStock(data.partDealerStock, data.carDealerStock);

            if (playerRoot) playerRoot.SetPositionAndRotation(data.playerPosition, data.playerRotation);

            garage.OnGarageChanged?.Raise();
            carSpawner?.Respawn();
            if (carSpawner && carSpawner.Current)
                carSpawner.Current.Vehicle.Teleport(data.carPosition, data.carRotation);

            onLoaded?.Raise();
            return true;
        }

        public void NewGame()
        {
            garage.Clear();
            money.SetSilent(0f);
            clock.SetTime(0, 8f);
            if (starterCar) garage.AddCar(starterCar);
            scheduler.Generate();
            dealers.Restock();
            // Unset spawn = leave the player wherever the scene placed them.
            if (playerRoot && defaultPlayerSpawn != Vector3.zero) playerRoot.position = defaultPlayerSpawn;
            carSpawner?.Respawn();
            onNewGame?.Raise();
        }

        public void DeleteSave() { if (HasSave) File.Delete(Path); }
    }
}
