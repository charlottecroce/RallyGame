using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Garage
{
    /// Interactable that opens the garage UI. Explicitly cannot repair (GDD).
    public class GarageWorkbench : MonoBehaviour, IInteractable
    {
        [SerializeField] private GarageState garage;
        [SerializeField] private BoolVariable playerInGarage;
        [SerializeField] private GameEvent onOpenGarageUi;

        public bool CanInteract => garage.ActiveCar != null && (playerInGarage == null || playerInGarage.Value);

        public string Prompt => "Workbench - fit parts [E]";

        public void Interact(GameObject instigator)
        {
            // Re-check with reasons so a dead-feeling workbench explains itself.
            if (garage.ActiveCar == null)
            {
                GameLog.Refused(LogCat.Garage, "open workbench", "no active car in the garage", this);
                return;
            }
            if (playerInGarage != null && !playerInGarage.Value)
            {
                GameLog.Refused(LogCat.Garage, "open workbench", "player is not inside the garage zone", this);
                return;
            }

            var car = garage.ActiveCar;
            GameLog.Action(LogCat.Garage, "Workbench opened",
                           $"active car '{car.instanceId}' ({car.definitionId}), " +
                           $"{car.installedPartInstanceIds.Count} fitted part(s)", this);

            onOpenGarageUi?.Raise();
        }
    }
}
