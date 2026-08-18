using UnityEngine;
using RallyGame.Core;
using RallyGame.Economy;
using RallyGame.Garage;
using RallyGame.Vehicles.Data;

namespace RallyGame.Races.Runtime
{
    /// Service park / mechanic. The only place repairs and tire changes are allowed
    /// (garage can fit parts but not repair, per GDD).
    public class ServicePoint : MonoBehaviour, IInteractable
    {
        [SerializeField] private EconomyService economy;
        [SerializeField] private GarageState garage;
        [SerializeField] private RaceState raceState;
        [SerializeField] private BoolVariable serviceUiOpen;
        [SerializeField] private GameEvent onOpenServiceUi;
        [Tooltip("Service parks only work during a service window; a town mechanic works anytime.")]
        [SerializeField] private bool requiresServiceWindow;

        public bool CanInteract =>
            garage.ActiveCar != null &&
            (!requiresServiceWindow || raceState.phase == RacePhase.ServiceWindow);

        public string Prompt => CanInteract
            ? $"Service - repair {economy.QuoteFullRepair(garage.ActiveCar):N0} [E]"
            : "Service closed";

        public void Interact(GameObject instigator)
        {
            if (!CanInteract) return;
            if (serviceUiOpen) serviceUiOpen.Value = true;
            onOpenServiceUi?.Raise();
        }

        // Direct actions, also callable from the service UI buttons.
        public bool RepairAll() => economy.RepairAll(garage.ActiveCar);
        public bool FitTires(TireCompound c) => economy.ChangeTires(garage.ActiveCar, c);
    }
}
