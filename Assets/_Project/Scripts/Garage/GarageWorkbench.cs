using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Garage
{
    /// Interactable that opens the garage UI. Explicitly cannot repair (GDD).
    public class GarageWorkbench : MonoBehaviour, IInteractable
    {
        [SerializeField] private GarageState garage;
        [SerializeField] private BoolVariable playerInGarage;
        [Tooltip("Shared with CarHoodInspectable and GarageView. The hood leaves this true; " +
                 "the workbench clears it so the panel always opens editable.")]
        [SerializeField] private BoolVariable inspectOnly;
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

            // The hood box leaves this true. Clear it so the workbench always opens
            // editable, whatever opened the panel last.
            if (inspectOnly) inspectOnly.Value = false;

            onOpenGarageUi?.Raise();
        }
    }
}