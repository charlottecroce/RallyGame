using System.Text;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Garage;
using RallyGame.Parts.Data;
using RallyGame.Parts.Runtime;
using RallyGame.Utilities;

namespace RallyGame.Vehicles.Controllers
{
    /// Sits on a BoxCollider over the hood. Opens the existing garage panel in
    /// read-only mode rather than duplicating it — GarageView reads the same
    /// inspectOnly flag this component sets, and hides its Fit/Remove buttons.
    ///
    /// Works anywhere in the world, unlike the workbench: the whole point is
    /// diagnosing a car at the side of a stage where you cannot fix it.
    ///
    /// Like CarDoorInteractable this lives on the prefab and holds only SO
    /// references, so the spawner needs to wire nothing.
    [RequireComponent(typeof(BoxCollider))]
    public class CarHoodInspectable : MonoBehaviour, IInteractable
    {
        [SerializeField] private GarageState garage;
        [Tooltip("Shared flag GarageView reads to decide whether to show Fit/Remove. " +
                 "GarageWorkbench must set the same flag to false when it opens.")]
        [SerializeField] private BoolVariable inspectOnly;
        [Tooltip("Optional. Stops the prompt appearing through the windscreen while seated.")]
        [SerializeField] private BoolVariable isDriving;
        [Tooltip("Same channel the workbench raises. Reuses the panel rather than adding one.")]
        [SerializeField] private GameEvent onOpenGarageUi;

        [SerializeField] private string prompt = "Open hood - inspect [E]";

        [Header("Debug")]
        [Tooltip("Dump the full part manifest to the console when the hood is opened.")]
        [SerializeField] private bool logManifestOnOpen = true;

        public bool CanInteract
        {
            get
            {
                if (garage == null || garage.ActiveCar == null) return false;
                if (isDriving != null && isDriving.Value) return false;
                return true;
            }
        }

        public string Prompt => prompt;

        private void Awake()
        {
            // A solid collider here adds contact points to the car's rigidbody, which
            // is exactly what used to multiply one landing into several crash events.
            // The raycast uses QueryTriggerInteraction.Collide, so a trigger is enough.
            var box = GetComponent<BoxCollider>();
            if (box && !box.isTrigger)
                GameLog.Warn(LogCat.Vehicle,
                    $"Hood inspection box '{name}' is not a trigger. It will generate physics " +
                    "contacts on the car body. Tick Is Trigger unless this collider is also " +
                    "doing real collision work.", this);

            if (inspectOnly == null)
                GameLog.Warn(LogCat.Vehicle,
                    $"Hood inspection box '{name}' has no inspectOnly BoolVariable assigned — " +
                    "the panel will open in full edit mode and the player will be able to fit parts " +
                    "from anywhere on the map.", this);
        }

        public void Interact(GameObject instigator)
        {
            var car = garage != null ? garage.ActiveCar : null;

            if (car == null)
            {
                GameLog.Refused(LogCat.Garage, "open hood", "no active car in the garage", this);
                return;
            }

            if (inspectOnly) inspectOnly.Value = true;

            GameLog.Action(LogCat.Garage, "HOOD OPENED (inspect only)",
                           $"car '{car.instanceId}' ({car.definitionId}), " +
                           $"{car.installedPartInstanceIds.Count} fitted part(s), " +
                           $"{car.odometerKm:0} km", this);

            if (logManifestOnOpen) LogManifest();

            onOpenGarageUi?.Raise();
        }

        /// The console copy of what the panel is about to show. Useful when the
        /// complaint is "a part feels dead" and you want the numbers in the log
        /// next to the crash lines that caused it.
        private void LogManifest()
        {
            var car = garage.ActiveCar;
            var db = garage.Database;
            var sb = new StringBuilder();

            sb.AppendLine($"Part manifest for '{car.nickname}' ({car.definitionId}):");
            sb.AppendLine($"  Tires   {car.tires.compound}  {1f - car.tires.wear:P0} remaining  " +
                          $"({car.tires.kmDriven:0} km on set)");

            int fitted = 0, empty = 0, broken = 0;

            foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
            {
                var part = car.PartInSlot(slot, garage, db);
                if (part == null)
                {
                    empty++;
                    sb.AppendLine($"  {slot,-14} -- empty --");
                    continue;
                }

                fitted++;
                var def = part.Definition(db);
                var tier = part.Tier(db);
                if (tier == DamageTier.Broken) broken++;

                sb.AppendLine($"  {slot,-14} {def?.displayName ?? part.definitionId}  " +
                              $"{part.condition:P0} ({tier})  " +
                              $"{part.kmSinceNew:0} km  " +
                              $"effectiveness {part.Effectiveness(db):P0}");
            }

            sb.Append($"  {fitted} fitted, {empty} empty, {broken} broken.");

            GameLog.Verbose(LogCat.Parts, sb.ToString(), this);
        }
    }
}