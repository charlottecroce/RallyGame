using UnityEngine;
using RallyGame.Core;
using RallyGame.Garage;
using RallyGame.Vehicles.Data;

namespace RallyGame.Vehicles.Controllers
{
    /// Only one car exists in the world at a time (GDD). This is the single place
    /// that instantiates/destroys it, so nothing else needs a scene reference to "the car".
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

        private void Start() { if (Current == null) Respawn(); }

        /// Rebuild the world car from the active OwnedCar, preserving position where sensible.
        public void Respawn()
        {
            var owned = garage.ActiveCar;
            Vector3 pos = current ? current.transform.position : garageSpawnPoint.position;
            Quaternion rot = current ? current.transform.rotation : garageSpawnPoint.rotation;

            if (current) Destroy(current);
            Current = null;

            if (owned == null) return;

            var def = owned.Definition(garage.Database);
            if (def == null || def.prefab == null) { Debug.LogError($"[CarSpawner] Car '{owned.definitionId}' has no prefab."); return; }

            // Swapping cars in the garage always places the new car on the garage pad.
            current = Instantiate(def.prefab, garageSpawnPoint ? garageSpawnPoint.position : pos,
                                              garageSpawnPoint ? garageSpawnPoint.rotation : rot);
            Current = current.GetComponent<CarAssembly>();
            if (Current == null) { Debug.LogError("[CarSpawner] Car prefab is missing CarAssembly."); return; }

            Current.Bind(owned);
            onCarSpawned?.Raise();
        }

        public void PlaceAt(Transform point)
        {
            if (Current == null) return;
            Current.Vehicle.Teleport(point.position, point.rotation);
        }
    }
}
