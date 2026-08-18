using UnityEngine;
using RallyGame.Core;
using RallyGame.Input;
using RallyGame.Vehicles.Controllers;

namespace RallyGame.Player
{
    /// Owns the on-foot <-> driving state switch. The car prefab carries a
    /// CarDoorInteractable that is wired to this on spawn, so the prefab needs
    /// no scene references.
    public class VehicleEntry : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private CarSpawner spawner;
        [SerializeField] private FirstPersonController onFoot;
        [SerializeField] private GameObject onFootRoot;
        [SerializeField] private Camera onFootCamera;
        [SerializeField] private Transform exitPointFallback;
        [SerializeField] private BoolVariable isDriving;
        [SerializeField] private BoolVariable inputLocked;
        [SerializeField] private GameEvent onCarSpawned;
        [SerializeField] private GameEvent onEnteredCar;
        [SerializeField] private GameEvent onExitedCar;

        private IVehicleController vehicle;
        private Camera interiorCamera;

        public bool IsDriving => isDriving && isDriving.Value;

        private void OnEnable() { if (onCarSpawned) onCarSpawned.Register(WireSpawnedCar); }
        private void OnDisable() { if (onCarSpawned) onCarSpawned.Unregister(WireSpawnedCar); }

        /// Injection point: give the freshly spawned car a way to call back into us.
        private void WireSpawnedCar()
        {
            var door = spawner.Current ? spawner.Current.GetComponentInChildren<CarDoorInteractable>(true) : null;
            if (door) door.Bind(this);
        }

        private void Update()
        {
            if (!IsDriving || vehicle == null) return;

            vehicle.SetInput(input.Vehicle);

            // Raycast interaction is unavailable while seated, so exit is a direct key.
            if (input.InteractPressed && !(inputLocked && inputLocked.Value)) Exit();
        }

        public void Enter()
        {
            if (IsDriving || spawner.Current == null) return;
            vehicle = spawner.Current.Vehicle;

            interiorCamera = spawner.Current.GetComponentInChildren<Camera>(true);
            if (interiorCamera) interiorCamera.gameObject.SetActive(true);
            if (onFootCamera) onFootCamera.gameObject.SetActive(false);
            if (onFoot) onFoot.enabled = false;
            if (onFootRoot) onFootRoot.SetActive(false);

            vehicle.SetControlEnabled(true);
            vehicle.SetEngineRunning(true);
            if (isDriving) isDriving.Value = true;
            Cursor.lockState = CursorLockMode.Locked;
            onEnteredCar?.Raise();
        }

        public void Exit()
        {
            if (vehicle == null) return;

            Vector3 exitPos = vehicle.Root.position - vehicle.Root.right * 1.6f + Vector3.up * 0.6f;
            if (Physics.CheckSphere(exitPos, 0.4f) && exitPointFallback) exitPos = exitPointFallback.position;

            vehicle.SetControlEnabled(false);
            vehicle.SetEngineRunning(false);
            if (interiorCamera) interiorCamera.gameObject.SetActive(false);

            if (onFootRoot) onFootRoot.SetActive(true);
            if (onFootCamera) onFootCamera.gameObject.SetActive(true);
            if (onFoot)
            {
                onFoot.enabled = true;
                onFoot.TeleportTo(exitPos, Quaternion.LookRotation(-vehicle.Root.right, Vector3.up));
            }

            vehicle = null;
            if (isDriving) isDriving.Value = false;
            onExitedCar?.Raise();
        }
    }
}
