using UnityEngine;
using RallyGame.Core;
using RallyGame.Utilities;

namespace RallyGame.Vehicles.Controllers
{
    /// Owns every decision about whether a collision counts as a crash, and how bad
    /// it was.
    ///
    /// Nothing here logs per frame. Rejected contacts are silent unless you ask.
    [RequireComponent(typeof(CarAssembly))]
    [RequireComponent(typeof(Rigidbody))]
    public class CarImpactSensor : MonoBehaviour
    {
        [Header("Severity curve")]
        [Tooltip("Closing speed below this is free. Kerbs, scrapes and parking nudges.")]
        [SerializeField] private float impactThresholdKph = 15f;
        [Tooltip("Closing speed treated as a full-severity crash. Damage plateaus here.")]
        [SerializeField] private float severeCrashKph = 90f;
        [Tooltip("Condition removed by a crash at severeCrashKph, before mass scaling.")]
        [Range(0f, 1f)] [SerializeField] private float damageAtSevere = 0.35f;
        [Tooltip("Shape between threshold and severe. 1 = linear, higher = low-speed hits are gentler.")]
        [SerializeField] private float severityExponent = 1.6f;
        [Tooltip("Absolute ceiling for one impact. This is the guarantee that >100% can never happen again.")]
        [Range(0f, 1f)] [SerializeField] private float maxDamagePerImpact = 0.5f;

        [Header("Mass scaling")]
        [Tooltip("Car mass that scores exactly the severity curve above.")]
        [SerializeField] private float referenceMassKg = 1200f;
        [Tooltip("How much mass moves severity. 0 = every car crashes identically.")]
        [Range(0f, 1f)] [SerializeField] private float massInfluence = 0.5f;

        [Header("Gating")]
        [Tooltip("Impacts are ignored for this long after the car spawns or is enabled.")]
        [SerializeField] private float spawnGraceSeconds = 1.5f;
        [Tooltip("Impacts are ignored for this long after a teleport or respawn.")]
        [SerializeField] private float teleportGraceSeconds = 1f;
        [Tooltip("Minimum gap between two counted crashes. Stops a wall scrape becoming twenty impacts.")]
        [SerializeField] private float reimpactCooldown = 0.25f;
        [Tooltip("Layers that can never damage the car (player capsule, triggers, debris).")]
        [SerializeField] private LayerMask ignoreLayers;
        [Tooltip("Against static geometry only, the car must be moving at least this fast. Secondary guard against settling.")]
        [SerializeField] private float minOwnSpeedKph = 3f;

        [Header("Debug")]
        [Tooltip("Log contacts that were rejected, and why. Noisy — for tuning sessions only.")]
        [SerializeField] private bool logRejectedContacts = false;

        private CarAssembly assembly;
        private Rigidbody rb;

        private float armedAt;
        private float lastImpactTime = -999f;

        // Worst contact seen in the current physics step, flushed once next FixedUpdate.
        private bool hasPending;
        private float pendingKph;
        private float pendingOwnKph;
        private bool pendingOtherIsDynamic;
        private string pendingWith;
        private int pendingContactCount;

        private void Awake()
        {
            assembly = GetComponent<CarAssembly>();
            rb = GetComponent<Rigidbody>();
        }

        private void OnEnable() => Suppress(spawnGraceSeconds, "spawn");

        /// Re-arms the grace window. Called on spawn, and by CarAssembly when it
        /// detects a single-frame position jump (teleport, respawn, service park).
        public void Suppress(float seconds, string reason)
        {
            float until = Time.time + Mathf.Max(0f, seconds);
            if (until <= armedAt) return;

            armedAt = until;
            hasPending = false;

            GameLog.Verbose(LogCat.Vehicle,
                $"Impact sensor disarmed for {seconds:0.00}s ({reason}) on '{name}'", this);
        }

        public void SuppressForTeleport() => Suppress(teleportGraceSeconds, "teleport");

        // ---- collection ----------------------------------------------------

        private void OnCollisionEnter(Collision collision) => Consider(collision);

        private void Consider(Collision collision)
        {
            if (Time.time < armedAt)
            {
                Reject(collision, "inside spawn/teleport grace window");
                return;
            }

            // Child colliders on our own rigidbody should never register as a crash.
            if (collision.rigidbody == rb) return;
            if (collision.collider && collision.collider.transform.IsChildOf(transform)) return;

            if ((ignoreLayers.value & (1 << collision.gameObject.layer)) != 0)
            {
                Reject(collision, $"layer '{LayerMask.LayerToName(collision.gameObject.layer)}' is on the ignore mask");
                return;
            }

            float kph = ClosingSpeedKph(collision);
            if (kph < impactThresholdKph)
            {
                Reject(collision, $"closing speed {kph:0.0} kph below threshold {impactThresholdKph:0} kph");
                return;
            }

            if (hasPending && kph <= pendingKph) return;

            hasPending = true;
            pendingKph = kph;
            pendingOwnKph = rb.Velocity().magnitude * 3.6f;
            pendingOtherIsDynamic = collision.rigidbody != null && !collision.rigidbody.isKinematic;
            pendingWith = collision.collider ? collision.collider.name : "<unknown>";
            pendingContactCount = collision.contactCount;
        }

        /// Callbacks arrive after the physics step, so the flush happens at the top
        /// of the next one. One crash per step, maximum.
        private void FixedUpdate()
        {
            if (!hasPending) return;
            hasPending = false;
            Resolve();
        }

        private void Resolve()
        {
            if (Time.time - lastImpactTime < reimpactCooldown)
            {
                if (logRejectedContacts)
                    GameLog.Verbose(LogCat.Vehicle,
                        $"Impact of {pendingKph:0.0} kph folded into the previous one " +
                        $"({Time.time - lastImpactTime:0.00}s ago, cooldown {reimpactCooldown:0.00}s)", this);
                return;
            }

            // A stationary car resting against static geometry is settling, not crashing.
            if (!pendingOtherIsDynamic && pendingOwnKph < minOwnSpeedKph)
            {
                if (logRejectedContacts)
                    GameLog.Verbose(LogCat.Vehicle,
                        $"Contact with static '{pendingWith}' ignored — car was doing {pendingOwnKph:0.0} kph", this);
                return;
            }

            float t = Mathf.InverseLerp(impactThresholdKph, severeCrashKph, pendingKph);
            float damage = damageAtSevere * Mathf.Pow(t, Mathf.Max(0.1f, severityExponent));
            damage *= MassScale();
            damage = Mathf.Clamp(damage, 0f, maxDamagePerImpact);

            lastImpactTime = Time.time;

            assembly.ApplyImpact(damage,
                $"{pendingKph:0} kph closing into '{pendingWith}' " +
                $"({pendingContactCount} contact point(s), severity {t:P0}, {rb.mass:0} kg)");
        }

        // ---- maths ---------------------------------------------------------

        /// Relative velocity projected onto the averaged contact normal. This is the
        /// component that actually deforms metal — a glancing scrape along a wall
        /// scores near zero even at 100 kph, and a spawn overlap scores zero because
        /// neither body is moving no matter how large the solver's impulse is.
        private static float ClosingSpeedKph(Collision collision)
        {
            if (collision.contactCount == 0)
                return collision.relativeVelocity.magnitude * 3.6f;

            Vector3 normal = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++)
                normal += collision.GetContact(i).normal;

            if (normal.sqrMagnitude < 1e-6f)
                return collision.relativeVelocity.magnitude * 3.6f;

            normal.Normalize();
            return Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal)) * 3.6f;
        }

        private float MassScale()
        {
            if (massInfluence <= 0f) return 1f;
            float raw = rb.mass / Mathf.Max(1f, referenceMassKg);
            return Mathf.Lerp(1f, Mathf.Clamp(raw, 0.6f, 1.6f), massInfluence);
        }

        private void Reject(Collision collision, string why)
        {
            if (!logRejectedContacts) return;
            GameLog.Verbose(LogCat.Vehicle,
                $"Contact with '{(collision.collider ? collision.collider.name : "<null>")}' ignored — {why}", this);
        }

        private void OnValidate()
        {
            if (severeCrashKph <= impactThresholdKph)
                severeCrashKph = impactThresholdKph + 1f;
            if (maxDamagePerImpact < damageAtSevere)
                maxDamagePerImpact = damageAtSevere;
        }
    }
}