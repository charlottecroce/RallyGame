using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Garage
{
    /// Trigger volume that flags "player is in the garage". Fitment is gated on this
    /// so the same UI cannot be abused from the middle of a stage.
    [RequireComponent(typeof(Collider))]
    public class GarageZone : MonoBehaviour
    {
        [SerializeField] private BoolVariable playerInGarage;
        [SerializeField] private string playerTag = "Player";

        private void Reset() => GetComponent<Collider>().isTrigger = true;
        private void OnTriggerEnter(Collider other) { if (other.CompareTag(playerTag)) playerInGarage.Value = true; }
        private void OnTriggerExit(Collider other) { if (other.CompareTag(playerTag)) playerInGarage.Value = false; }
    }
}
