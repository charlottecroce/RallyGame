using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Parts.Data;
using RallyGame.Parts.Runtime;
using RallyGame.Vehicles.Data;

namespace RallyGame.Garage
{
    /// Owns every OwnedCar and OwnedPart the player has. Runtime state on an asset,
    /// so UI/dealers/race code reference it directly and the save layer has one place to read.
    [CreateAssetMenu(menuName = "Rally/State/Garage State", fileName = "GarageState")]
    public class GarageState : ScriptableObject, IPartResolver
    {
        [SerializeField] private DefinitionDatabase database;

        [Header("Channels")]
        [SerializeField] private GameEvent onGarageChanged;   // inventory or fitment changed
        [SerializeField] private GameEvent onActiveCarChanged;

        [System.NonSerialized] public List<OwnedCar> ownedCars = new List<OwnedCar>();
        [System.NonSerialized] public List<OwnedPart> allParts = new List<OwnedPart>();  // installed + loose
        [System.NonSerialized] public string activeCarInstanceId;

        public DefinitionDatabase Database => database;
        public GameEvent OnGarageChanged => onGarageChanged;

        private void OnEnable() { ownedCars = new List<OwnedCar>(); allParts = new List<OwnedPart>(); activeCarInstanceId = null; }

        // ---- lookups -------------------------------------------------------

        public OwnedPart GetOwnedPart(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            foreach (var p in allParts) if (p.instanceId == instanceId) return p;
            return null;
        }

        public OwnedCar GetCar(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            foreach (var c in ownedCars) if (c.instanceId == instanceId) return c;
            return null;
        }

        public OwnedCar ActiveCar => GetCar(activeCarInstanceId);

        /// Parts not fitted to any car - the garage inventory in GDD terms.
        public List<OwnedPart> LooseParts()
        {
            var loose = new List<OwnedPart>();
            foreach (var p in allParts)
            {
                bool installed = false;
                foreach (var c in ownedCars) if (c.HasPart(p.instanceId)) { installed = true; break; }
                if (!installed) loose.Add(p);
            }
            return loose;
        }

        public List<OwnedPart> LoosePartsInSlot(PartSlot slot)
        {
            var result = new List<OwnedPart>();
            foreach (var p in LooseParts())
            {
                var def = p.Definition(database);
                if (def != null && def.slot == slot) result.Add(p);
            }
            return result;
        }

        // ---- mutation ------------------------------------------------------

        public OwnedPart AddPart(PartDefinition def, float condition = 1f)
        {
            var part = new OwnedPart(def, condition);
            allParts.Add(part);
            onGarageChanged?.Raise();
            return part;
        }

        public void AddPart(OwnedPart part) { allParts.Add(part); onGarageChanged?.Raise(); }

        public void RemovePart(OwnedPart part)
        {
            foreach (var c in ownedCars) c.installedPartInstanceIds.Remove(part.instanceId);
            allParts.Remove(part);
            onGarageChanged?.Raise();
        }

        public OwnedCar AddCar(CarDefinition def, bool withDefaultParts = true)
        {
            var car = new OwnedCar(def);
            ownedCars.Add(car);

            if (withDefaultParts)
                foreach (var partDef in def.defaultParts)
                {
                    if (!partDef) continue;
                    var part = AddPart(partDef);
                    car.installedPartInstanceIds.Add(part.instanceId);
                }

            if (string.IsNullOrEmpty(activeCarInstanceId)) SetActiveCar(car.instanceId);
            onGarageChanged?.Raise();
            return car;
        }

        public void RemoveCar(OwnedCar car)
        {
            foreach (var partId in new List<string>(car.installedPartInstanceIds))
            {
                var part = GetOwnedPart(partId);
                if (part != null) allParts.Remove(part);
            }
            ownedCars.Remove(car);
            if (activeCarInstanceId == car.instanceId)
                SetActiveCar(ownedCars.Count > 0 ? ownedCars[0].instanceId : null);
            onGarageChanged?.Raise();
        }

        public void SetActiveCar(string instanceId)
        {
            if (activeCarInstanceId == instanceId) return;
            activeCarInstanceId = instanceId;
            onActiveCarChanged?.Raise();
            onGarageChanged?.Raise();
        }

        public void Clear()
        {
            ownedCars.Clear(); allParts.Clear(); activeCarInstanceId = null;
            onGarageChanged?.Raise();
        }
    }
}
