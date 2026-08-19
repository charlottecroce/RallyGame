using UnityEngine;
using RallyGame.Core;
using RallyGame.World.Physics;

namespace RallyGame.Vehicles.Controllers
{
    /// Stamps the shared CollisionProfile onto this car at spawn, so the prefab
    /// carries no loose physics numbers and every car behaves the same on impact.
    ///
    /// Also runs one startup audit. The single most common cause of "the car climbed
    /// the tree and reverse did nothing" is a body collider that reaches lower than
    /// the wheels: it becomes the contact patch, the WheelColliders never touch
    /// ground, and drive torque has nothing to push against.
    [RequireComponent(typeof(Rigidbody))]
    public class CarBodyCollision : MonoBehaviour
    {
        [SerializeField] private CollisionProfile profile;

        [Header("Audit")]
        [Tooltip("Warn if a body collider hangs below the bottom of the wheels.")]
        [SerializeField] private bool auditOnStart = true;
        [Tooltip("Slack allowed before a collider counts as hanging low.")]
        [SerializeField] private float lowColliderTolerance = 0.02f;

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            if (profile == null)
            {
                GameLog.Warn(LogCat.Vehicle,
                    $"'{name}' has no CollisionProfile assigned — the car keeps Unity's default " +
                    "0.6-friction material and will stick to scenery on impact.", this);
                return;
            }

            Apply();
        }

        private void Start() { if (auditOnStart) Audit(); }

        /// Public so a respawn or a live profile tweak can re-stamp without a reload.
        public void Apply()
        {
            rb.maxDepenetrationVelocity = Mathf.Max(0.5f, profile.maxDepenetrationVelocity);
            rb.solverIterations = profile.solverIterations;
            rb.solverVelocityIterations = profile.solverVelocityIterations;
            rb.collisionDetectionMode = profile.collisionDetection;

            int stamped = 0;
            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;                       // hood box, interaction volumes
                if (col is WheelCollider) continue;                // wheels use friction curves, not materials
                col.sharedMaterial = profile.carBodyMaterial;
                stamped++;
            }

            GameLog.Action(LogCat.Vehicle, "Collision profile applied",
                           $"'{name}': {stamped} body collider(s) -> " +
                           $"'{(profile.carBodyMaterial ? profile.carBodyMaterial.name : "<none>")}', " +
                           $"depen {rb.maxDepenetrationVelocity:0.0} m/s, {rb.solverIterations} solver iterations", this);
        }

        // ---- audit ---------------------------------------------------------

        private void Audit()
        {
            var wheels = GetComponentsInChildren<WheelCollider>(true);
            if (wheels.Length == 0) return;

            float wheelBottom = float.MaxValue;
            foreach (var w in wheels)
                wheelBottom = Mathf.Min(wheelBottom, w.transform.position.y - w.radius);

            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger || col is WheelCollider) continue;

                float bottom = col.bounds.min.y;
                if (bottom >= wheelBottom - lowColliderTolerance) continue;

                GameLog.Warn(LogCat.Vehicle,
                    $"Collider '{col.name}' on '{name}' reaches {wheelBottom - bottom:0.000} m below the wheels. " +
                    "It will take the ground contact instead of the tyres, which reads as " +
                    "'no drive, cannot reverse' after any hard impact. Raise it or shrink it.", col);
            }
        }
    }
}