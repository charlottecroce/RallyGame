using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Vehicles.Controllers
{
    /// Sits on the car prefab (usually on the driver's door collider). Holds no
    /// scene reference - VehicleEntry binds itself on spawn.
    public class CarDoorInteractable : MonoBehaviour, IInteractable
    {
        private Player.VehicleEntry entry;

        public bool CanInteract => entry != null && !entry.IsDriving;
        public string Prompt => "Enter car [E]";

        public void Bind(Player.VehicleEntry owner) => entry = owner;
        public void Interact(GameObject instigator) => entry?.Enter();
    }
}
