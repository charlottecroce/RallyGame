using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Races.Data
{
    /// A rally venue. Groups stages and holds the service-park / entry-tent anchors.
    [CreateAssetMenu(menuName = "Rally/Definitions/Location", fileName = "Location_")]
    public class LocationDefinition : ScriptableObject
    {
        /// Matches the CreateAssetMenu fileName above.
        public const string IdPrefix = "Location_";

        [Tooltip("Stable save ID. Stamped from the file name on a new asset; no inline default, " +
                 "because a default that looks real ends up in save files (a RaceEvent stores " +
                 "locationId). Never change it once a save exists.")]
        public string id;
        public string displayName = "New Location";
        public List<StageDefinition> stages = new List<StageDefinition>();

        [Header("World anchors")]
        public Vector3 entryTentPosition;
        public Vector3 servicePosition;
        [Tooltip("Does this venue have a service park between stages?")]
        public bool hasServicePark = true;

        [Header("Economy")]
        [Tooltip("Purse for a single casual stage race here.")]
        public int casualPurse = 800;
        [Tooltip("Purse for the full rally weekend.")]
        public int rallyPurse = 6000;
        public int fieldSize = 24;

        private void OnValidate() { id = DefinitionId.Resolve(id, name, IdPrefix); }
    }
}