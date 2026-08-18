using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Parts.Data;
using RallyGame.Parts.Runtime;

namespace RallyGame.Vehicles.Data
{
    /// A specific car the player owns. Installed parts are referenced by instance ID,
    /// so the garage inventory stays the single owner of every OwnedPart object.
    [System.Serializable]
    public class OwnedCar
    {
        public string instanceId;
        public string definitionId;
        public string nickname;
        public List<string> installedPartInstanceIds = new List<string>();
        public TireState tires = new TireState();
        public float odometerKm;

        [System.NonSerialized] private CarDefinition cached;

        public OwnedCar() { }

        public OwnedCar(CarDefinition def)
        {
            instanceId = System.Guid.NewGuid().ToString("N");
            definitionId = def.id;
            nickname = def.displayName;
            tires.Fit(def.defaultTireCompound);
            cached = def;
        }

        public CarDefinition Definition(DefinitionDatabase db)
        {
            if (cached == null || cached.id != definitionId) cached = db.GetCar(definitionId);
            return cached;
        }

        public bool HasPart(string partInstanceId) => installedPartInstanceIds.Contains(partInstanceId);

        /// Slot occupancy is derived from the installed list, not stored twice.
        public OwnedPart PartInSlot(PartSlot slot, IPartResolver resolver, DefinitionDatabase db)
        {
            foreach (var id in installedPartInstanceIds)
            {
                var part = resolver.GetOwnedPart(id);
                if (part != null && part.Definition(db) != null && part.Definition(db).slot == slot) return part;
            }
            return null;
        }
    }

    /// Lets OwnedCar look parts up without knowing about the garage/save layer.
    public interface IPartResolver { OwnedPart GetOwnedPart(string instanceId); }
}
