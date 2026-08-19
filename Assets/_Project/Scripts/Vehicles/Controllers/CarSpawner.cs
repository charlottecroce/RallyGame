using UnityEngine;
using RallyGame.Core;
using RallyGame.Garage;
using RallyGame.Vehicles.Data;

namespace RallyGame.Vehicles.Controllers
{
    /// Only one car exists in the world at a time (GDD). This is the single place
    /// that instantiates/destroys it, so nothing else needs a scene reference to "the car".
    ///
    /// Respawn is a discrete, low-frequency event (car swap, save load, new game),
    /// so every stage of it is logged in full.
    public class CarSpawner : MonoBehaviour
    {
        [SerializeField] private GarageState garage;
        [SerializeField] private Transform garageSpawnPoint;
        [SerializeField] private GameEvent onActiveCarChanged;
        [SerializeField] private GameEvent onCarSpawned;

        private GameObject current;
        public CarAssembly Current { get; private set; }

        private void OnEnable() { if (onActiveCarChanged) onActiveCarChanged.Register(Respawn); }
        private void OnDisable() { if (onActiveCarChanged) onActiveCarChanged.Unregister(Respawn); }

        private void Start()
        {
            if (Current == null)
            {
                GameLog.Verbose(LogCat.Vehicle, "No car in world at Start — running initial spawn.", this);
                Respawn();
            }
        }

        /// Rebuild the world car from the active OwnedCar, preserving position where sensible.
        public void Respawn()
        {
            var owned = garage.ActiveCar;
            Vector3 pos = current ? current.transform.position : garageSpawnPoint.position;
            Quaternion rot = current ? current.transform.rotation : garageSpawnPoint.rotation;

            if (current)
            {
                GameLog.Action(LogCat.Vehicle, "Despawning current car", $"'{current.name}'", this);
                Destroy(current);
            }
            Current = null;

            if (owned == null)
            {
                GameLog.Warn(LogCat.Vehicle, "Respawn requested but the garage has no active car — world is now carless.", this);
                return;
            }

            var def = owned.Definition(garage.Database);
            if (def == null || def.prefab == null)
            {
                GameLog.Error(LogCat.Vehicle, $"Car '{owned.definitionId}' has no prefab.", this);
                return;
            }

            // Swapping cars in the garage always places the new car on the garage pad.
            Vector3 spawnPos = garageSpawnPoint ? garageSpawnPoint.position : pos;
            Quaternion spawnRot = garageSpawnPoint ? garageSpawnPoint.rotation : rot;

            GameLog.Action(LogCat.Vehicle, "Spawning car",
                           $"{def.displayName} (def '{def.id}', instance '{owned.instanceId}') " +
                           $"at {spawnPos:0.0}{(garageSpawnPoint ? " [garage pad]" : " [previous position]")}", this);

            current = Instantiate(def.prefab, spawnPos, spawnRot);
            Current = current.GetComponent<CarAssembly>();

            if (Current == null)
            {
                GameLog.Error(LogCat.Vehicle, "Car prefab is missing CarAssembly.", current);
                return;
            }

            Current.Bind(owned);
            GameLog.Action(LogCat.Vehicle, "Car assembled and bound",
                           $"'{current.name}' -> OwnedCar '{owned.instanceId}'", current);

            onCarSpawned?.Raise();
        }

        public void PlaceAt(Transform point)
        {
            if (Current == null)
            {
                GameLog.Refused(LogCat.Vehicle, "place car", "no car spawned", this);
                return;
            }

            GameLog.Action(LogCat.Vehicle, "Car teleported",
                           $"to '{point.name}' {point.position:0.0}", Current);
            Current.Vehicle.Teleport(point.position, point.rotation);
        }
    }
}
