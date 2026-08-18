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

        public void Interact(GameObject instigator) => onOpenGarageUi?.Raise();
    }
}
