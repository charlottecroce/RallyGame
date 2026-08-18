using UnityEngine;
using RallyGame.Core;
using RallyGame.Parts.Data;

namespace RallyGame.Parts.Runtime
{
    /// A specific copy the player owns. Plain serializable C# so it round-trips
    /// through JsonUtility. Holds mutable state a shared SO never could.
    [System.Serializable]
    public class OwnedPart : IDamageable, IRepairable
    {
        public string instanceId;      // unique per copy
        public string definitionId;    // shared template
        [Range(0f, 1f)] public float condition = 1f;
        public float kmSinceNew;

        [System.NonSerialized] private PartDefinition cached;

        public OwnedPart() { }

        public OwnedPart(PartDefinition def, float startCondition = 1f)
        {
            instanceId = System.Guid.NewGuid().ToString("N");
            definitionId = def.id;
            condition = Mathf.Clamp01(startCondition);
            cached = def;
        }

        /// Resolve template through the database once, then cache for the session.
        public PartDefinition Definition(DefinitionDatabase db)
        {
            if (cached == null || cached.id != definitionId) cached = db.GetPart(definitionId);
            return cached;
        }

        public float Condition => condition;
        public float RepairCost => cached != null ? cached.RepairCost(condition) : 0f;

        public void ApplyDamage(float amount, DamageType type)
            => condition = Mathf.Clamp01(condition - Mathf.Max(0f, amount));

        public void Repair() => condition = 1f;

        /// Distance-based wear. Bodywork has degradePerKm = 0 per GDD.
        public void AccumulateWear(float km, DefinitionDatabase db)
        {
            kmSinceNew += km;
            var def = Definition(db);
            if (def == null || def.degradePerKm <= 0f) return;
            condition = Mathf.Clamp01(condition - def.degradePerKm * km);
        }

        /// 0..1 effectiveness curve. Full effect above the healthy threshold,
        /// falling off to zero at destruction.
        public float Effectiveness(DefinitionDatabase db)
        {
            var def = Definition(db);
            if (def == null) return 0f;
            if (condition >= def.healthyThreshold) return 1f;
            return Mathf.InverseLerp(0f, def.healthyThreshold, condition);
        }

        public DamageTier Tier(DefinitionDatabase db)
        {
            if (condition > 0.75f) return DamageTier.Fine;
            if (condition > 0.45f) return DamageTier.Light;
            if (condition > 0.15f) return DamageTier.Heavy;
            return DamageTier.Broken;
        }
    }

    public enum DamageTier { Fine, Light, Heavy, Broken }
}
