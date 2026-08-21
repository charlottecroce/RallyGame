using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Parts.Data
{
    /// One slot per fitted part. GDD roster - keep this list closed; new behaviour
    /// should come from stat modifiers, not new slots.
    public enum PartSlot
    {
        Bodywork, Engine, Turbo, Radiator, Transmission, Clutch,
        Suspension, Steering, Brakes, Headlights, RallyLights, Electronics
    }

    /// Static template: "what kinds of parts exist". Never holds player state.
    [CreateAssetMenu(menuName = "Rally/Definitions/Part", fileName = "Part_")]
    public class PartDefinition : ScriptableObject
    {
        /// Matches the CreateAssetMenu fileName above. DefinitionId uses it to
        /// recognise an ID nobody has filled in yet.
        public const string IdPrefix = "Part_";

        [Header("Identity")]
        [Tooltip("Stable save ID. Left blank on a new asset and stamped from the file name — " +
                 "there is deliberately no inline default, because a default that looks like a " +
                 "real ID gets written into save files and then fails to resolve forever after. " +
                 "Never change it once a save exists.")]
        public string id;
        public string displayName = "New Part";
        [TextArea] public string description;
        public PartSlot slot;

        [Header("Quality")]
        [Range(0f, 1f)] public float quality = 0.5f;   // scales modifier magnitude and price
        public int basePrice = 500;
        [Tooltip("Cost to repair from 0 to full condition.")]
        public int fullRepairCost = 300;

        [Header("Wear")]
        [Tooltip("Condition lost per km driven. Bodywork should be 0 - it only takes impacts.")]
        public float degradePerKm = 0.0015f;
        [Tooltip("Share of impact damage this slot absorbs, relative to other fitted parts.")]
        public float impactWeight = 0.2f;
        [Tooltip("Below this condition the part starts penalising performance.")]
        [Range(0f, 1f)] public float healthyThreshold = 0.5f;

        [Header("Requirements")]
        [Tooltip("Car will not start if this slot is missing or at zero condition (Electronics).")]
        public bool requiredToStart;

        [Header("Stat modifiers (multiplier unless noted)")]
        public float enginePower = 1f;
        public float turboBoost = 1f;
        public float coolingRate = 1f;
        public float gearShiftSpeed = 1f;
        public float clutchEfficiency = 1f;
        public float suspensionStiffness = 1f;
        public float steeringResponse = 1f;
        public float brakeForce = 1f;
        public float gripBonus = 1f;
        [Tooltip("Added kg. Can be negative for lightweight parts.")]
        public float massDelta = 0f;
        [Tooltip("Extra headlight range in metres.")]
        public float lightRange = 0f;

        public int PriceForCondition(float condition) => Mathf.RoundToInt(basePrice * Mathf.Lerp(0.35f, 1f, condition));
        public int RepairCost(float condition) => Mathf.RoundToInt(fullRepairCost * (1f - Mathf.Clamp01(condition)));

        private void OnValidate() { id = DefinitionId.Resolve(id, name, IdPrefix); }
    }
}