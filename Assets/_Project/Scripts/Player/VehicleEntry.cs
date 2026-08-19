using UnityEngine;
using RallyGame.Core;
using RallyGame.Input;
using RallyGame.Vehicles.Controllers;

namespace RallyGame.Player
{
    /// Owns the on-foot <-> driving state switch. The car prefab carries a
    /// CarDoorInteractable that is wired to this on spawn, so the prefab needs
    /// no scene references.
    ///
    /// Every step of the transition is logged, because when entering a car goes
    /// wrong it is almost always one specific step (camera not swapped, controller
    /// not disabled, exit point blocked) rather than the whole thing failing.
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

        [Header("Debug")]
        [Tooltip("Log each sub-step of the transition (cameras, controllers, roots) rather than a single summary line.")]
        [SerializeField] private bool logTransitionSteps = true;

        private IVehicleController vehicle;
        private Camera interiorCamera;

        public bool IsDriving => isDriving && isDriving.Value;

        private void OnEnable() { if (onCarSpawned) onCarSpawned.Register(WireSpawnedCar); }
        private void OnDisable() { if (onCarSpawned) onCarSpawned.Unregister(WireSpawnedCar); }

        /// Injection point: give the freshly spawned car a way to call back into us.
        private void WireSpawnedCar()
        {
            var door = spawner.Current ? spawner.Current.GetComponentInChildren<CarDoorInteractable>(true) : null;

            if (door)
            {
                door.Bind(this);
                GameLog.Action(LogCat.Player, "Car door wired to player",
                               $"door '{door.gameObject.name}' on '{spawner.Current.gameObject.name}'", door);
            }
            else
            {
                GameLog.Warn(LogCat.Player,
                    $"Car spawned but no CarDoorInteractable found in its hierarchy — the player will not be able to get in. " +
                    $"Car = {(spawner.Current ? spawner.Current.gameObject.name : "<null>")}", this);
            }
        }

        private void Update()
        {
            if (!IsDriving || vehicle == null) return;

            vehicle.SetInput(input.Vehicle);   // per-frame, deliberately never logged

            // Raycast interaction is unavailable while seated, so exit is a direct key.
            if (input.InteractPressed)
            {
                if (inputLocked && inputLocked.Value)
                    GameLog.Refused(LogCat.Player, "exit car", "input is locked (UI open?)", this);
                else
                    Exit();
            }
        }

        public void Enter()
        {
            if (IsDriving)
            {
                GameLog.Refused(LogCat.Player, "enter car", "already driving", this);
                return;
            }
            if (spawner.Current == null)
            {
                GameLog.Refused(LogCat.Player, "enter car", "no car currently spawned in the world", this);
                return;
            }

            vehicle = spawner.Current.Vehicle;
            if (vehicle == null)
            {
                GameLog.Error(LogCat.Player, "Enter aborted: spawned CarAssembly has no IVehicleController.", this);
                return;
            }

            GameLog.Action(LogCat.Player, "ENTERING CAR",
                           $"'{spawner.Current.gameObject.name}' at {Fmt(vehicle.Root.position)}", spawner.Current);

            interiorCamera = spawner.Current.GetComponentInChildren<Camera>(true);
            if (interiorCamera)
            {
                interiorCamera.gameObject.SetActive(true);
                if (logTransitionSteps) GameLog.Verbose(LogCat.Player, $"  interior camera '{interiorCamera.name}' enabled", interiorCamera);
            }
            else
            {
                GameLog.Warn(LogCat.Player, "Car has no interior Camera — the view will not change on entry.", spawner.Current);
            }

            if (onFootCamera)
            {
                onFootCamera.gameObject.SetActive(false);
                if (logTransitionSteps) GameLog.Verbose(LogCat.Player, "  on-foot camera disabled", onFootCamera);
            }
            if (onFoot)
            {
                onFoot.enabled = false;
                if (logTransitionSteps) GameLog.Verbose(LogCat.Player, "  FirstPersonController disabled", onFoot);
            }
            if (onFootRoot)
            {
                onFootRoot.SetActive(false);
                if (logTransitionSteps) GameLog.Verbose(LogCat.Player, "  on-foot root hidden", onFootRoot);
            }

            vehicle.SetControlEnabled(true);
            vehicle.SetEngineRunning(true);
            GameLog.Action(LogCat.Vehicle, "Engine started", $"driver control enabled, gear {vehicle.Gear}", spawner.Current);

            if (isDriving) isDriving.Value = true;
            Cursor.lockState = CursorLockMode.Locked;

            GameLog.Action(LogCat.Player, "Now DRIVING", $"car '{spawner.Current.gameObject.name}'", spawner.Current);
            onEnteredCar?.Raise();
        }

        public void Exit()
        {
            if (vehicle == null)
            {
                GameLog.Refused(LogCat.Player, "exit car", "not currently in a vehicle", this);
                return;
            }

            GameLog.Action(LogCat.Player, "EXITING CAR",
                           $"speed {vehicle.SpeedKph:0} kph, gear {vehicle.Gear}", this);

            Vector3 exitPos = vehicle.Root.position - vehicle.Root.right * 1.6f + Vector3.up * 0.6f;

            if (Physics.CheckSphere(exitPos, 0.4f))
            {
                if (exitPointFallback)
                {
                    GameLog.Action(LogCat.Player, "Door-side exit blocked",
                                   $"{Fmt(exitPos)} occupied, using fallback '{exitPointFallback.name}' at {Fmt(exitPointFallback.position)}", this);
                    exitPos = exitPointFallback.position;
                }
                else
                {
                    GameLog.Warn(LogCat.Player,
                        $"Door-side exit at {Fmt(exitPos)} is blocked and no fallback transform is assigned — " +
                        "the player may be placed inside geometry.", this);
                }
            }

            vehicle.SetControlEnabled(false);
            vehicle.SetEngineRunning(false);
            GameLog.Action(LogCat.Vehicle, "Engine stopped", "driver control released", this);

            if (interiorCamera)
            {
                interiorCamera.gameObject.SetActive(false);
                if (logTransitionSteps) GameLog.Verbose(LogCat.Player, "  interior camera disabled", interiorCamera);
            }

            if (onFootRoot)
            {
                onFootRoot.SetActive(true);
                if (logTransitionSteps) GameLog.Verbose(LogCat.Player, "  on-foot root restored", onFootRoot);
            }
            if (onFootCamera)
            {
                onFootCamera.gameObject.SetActive(true);
                if (logTransitionSteps) GameLog.Verbose(LogCat.Player, "  on-foot camera enabled", onFootCamera);
            }
            if (onFoot)
            {
                onFoot.enabled = true;
                onFoot.TeleportTo(exitPos, Quaternion.LookRotation(-vehicle.Root.right, Vector3.up));
                GameLog.Action(LogCat.Player, "Player placed on foot", $"at {Fmt(exitPos)}", onFoot);
            }

            vehicle = null;
            if (isDriving) isDriving.Value = false;

            GameLog.Action(LogCat.Player, "Now ON FOOT");
            onExitedCar?.Raise();
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
    }
}
