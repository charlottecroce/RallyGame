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

        [Header("Debug")]
        [Tooltip("Also log non-player colliders that pass through. Useful when the tag is wrong.")]
        [SerializeField] private bool logRejectedColliders = false;

        private void Reset() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(playerTag))
            {
                GameLog.Action(LogCat.Garage, "ENTERED garage zone",
                               $"zone '{name}', by '{other.name}'", this);
                playerInGarage.Value = true;
            }
            else if (logRejectedColliders)
            {
                GameLog.Verbose(LogCat.World,
                    $"'{other.name}' entered zone '{name}' but is tagged '{other.tag}', not '{playerTag}' — ignored.", this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag(playerTag))
            {
                GameLog.Action(LogCat.Garage, "LEFT garage zone",
                               $"zone '{name}', by '{other.name}'", this);
                playerInGarage.Value = false;
            }
        }
    }
}
