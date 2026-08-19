using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Vehicles.Controllers
{
    /// Sits on the car prefab (usually on the driver's door collider). Holds no
    /// scene reference - VehicleEntry binds itself on spawn.
    ///
    /// The unbound case is the classic silent failure here: the prompt simply never
    /// appears and there is nothing in the console to say why. Now there is.
    public class CarDoorInteractable : MonoBehaviour, IInteractable
    {
        [Header("Debug")]
        [Tooltip("Warn once if the player looks at this door while it has no VehicleEntry bound.")]
        [SerializeField] private bool warnWhenUnbound = true;

        private Player.VehicleEntry entry;
        private bool warned;

        public bool CanInteract
        {
            get
            {
                if (entry == null)
                {
                    if (warnWhenUnbound && !warned)
                    {
                        warned = true;
                        GameLog.Warn(LogCat.Vehicle,
                            $"Car door '{name}' has no VehicleEntry bound — Bind() was never called. " +
                            "Check that Evt_CarSpawned is raised after the car is instantiated.", this);
                    }
                    return false;
                }
                return !entry.IsDriving;
            }
        }

        public string Prompt => "Enter car [E]";

        public void Bind(Player.VehicleEntry owner)
        {
            entry = owner;
            warned = false;
            GameLog.Verbose(LogCat.Vehicle, $"Car door '{name}' bound to '{(owner ? owner.name : "<null>")}'", this);
        }

        public void Interact(GameObject instigator)
        {
            GameLog.Action(LogCat.Vehicle, "Car door opened",
                           $"door '{name}', instigator '{(instigator ? instigator.name : "<null>")}'", this);
            entry?.Enter();
        }
    }
}
