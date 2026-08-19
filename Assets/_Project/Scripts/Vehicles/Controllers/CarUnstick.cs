using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Vehicles.Controllers
{
    /// Frees a car that has wedged itself against scenery.
    ///
    /// "Stuck" here means one specific, measurable thing: the driver is asking for
    /// movement, the car is not moving, and it is in contact with something that is
    /// not the ground. That is the state you land in after ramming a tree - the body
    /// rides up the prop collider, the wheels lose the ground, and reverse does
    /// nothing because there is no contact patch to push against.
    ///
    /// Recovery escalates in two stages so the player never feels teleported for a
    /// scrape:
    ///   NUDGE  - a short push back along the contact normal plus downward force to
    ///            re-seat the wheels. Usually enough; the player barely notices.
    ///   RESET  - hands off to CarResetService after the nudge has failed for a while.
    ///
    /// Nothing here logs per frame. The two escalations are discrete and each gets
    /// one line.
    [RequireComponent(typeof(CarController))]
    [RequireComponent(typeof(Rigidbody))]
    public class CarUnstick : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Below this speed the car counts as not moving.")]
        [SerializeField] private float stuckSpeedKph = 2f;
        [Tooltip("Driver demand (throttle or brake) needed before we call it stuck. " +
                 "Prevents a parked car being 'rescued'.")]
        [Range(0f, 1f)] [SerializeField] private float minDriverDemand = 0.15f;
        [Tooltip("Seconds of demanding-but-not-moving before the first nudge.")]
        [SerializeField] private float nudgeAfterSeconds = 1.2f;
        [Tooltip("Seconds of continuous stuck state before giving up and calling for a reset. " +
                 "0 disables auto-reset — the player uses the button instead.")]
        [SerializeField] private float resetAfterSeconds = 6f;

        [Header("Nudge")]
        [Tooltip("Impulse away from the contact, as m/s of velocity change. Keep it small — " +
                 "this is a shove, not a cannon.")]
        [SerializeField] private float separationSpeed = 1.6f;
        [Tooltip("Extra downward push to plant the wheels again while beached.")]
        [SerializeField] private float reseatForce = 6000f;
        [Tooltip("Minimum gap between nudges so they cannot stack into a launch.")]
        [SerializeField] private float nudgeCooldown = 0.6f;
        [Tooltip("Surfaces flat enough to count as ground rather than an obstacle.")]
        [Range(0f, 89f)] [SerializeField] private float groundNormalAngle = 40f;

        [Header("Wiring")]
        [Tooltip("Optional. Raised when the nudge has failed and the car needs a full reset. " +
                 "CarResetService listens to this.")]
        [SerializeField] private GameEvent onStuckBeyondRecovery;

        private CarController controller;
        private Rigidbody rb;
        private WheelCollider[] wheels;

        private float stuckFor;
        private float lastNudgeTime = -999f;
        private bool obstacleTouching;
        private Vector3 obstacleNormal;
        private string obstacleName;

        public bool IsStuck => stuckFor >= nudgeAfterSeconds;

        private void Awake()
        {
            controller = GetComponent<CarController>();
            rb = GetComponent<Rigidbody>();
            wheels = GetComponentsInChildren<WheelCollider>(true);
        }

        // ---- contact tracking ----------------------------------------------
        // OnCollisionStay is the only cheap way to know we are still resting on
        // something. The averaged normal is what we push away from.

        private void OnCollisionStay(Collision collision)
        {
            if (collision.contactCount == 0) return;

            Vector3 sum = Vector3.zero;
            int obstacleContacts = 0;

            for (int i = 0; i < collision.contactCount; i++)
            {
                var n = collision.GetContact(i).normal;
                if (Vector3.Angle(n, Vector3.up) < groundNormalAngle) continue;   // that is floor, not a tree
                sum += n;
                obstacleContacts++;
            }

            if (obstacleContacts == 0) return;

            obstacleTouching = true;
            obstacleNormal = sum.normalized;
            obstacleName = collision.collider ? collision.collider.name : "<unknown>";
        }

        // ---- resolution ----------------------------------------------------

        private void FixedUpdate()
        {
            bool wedged = obstacleTouching;
            obstacleTouching = false;                 // consumed; refilled by the next OnCollisionStay

            float demand = Mathf.Max(controller.CurrentInput.throttle, controller.CurrentInput.brake);
            bool trying = controller.ControlEnabled && controller.EngineRunning && demand >= minDriverDemand;
            bool crawling = controller.SpeedKph < stuckSpeedKph;

            if (!trying || !crawling || !wedged)
            {
                stuckFor = 0f;
                return;
            }

            stuckFor += Time.fixedDeltaTime;

            if (resetAfterSeconds > 0f && stuckFor >= resetAfterSeconds)
            {
                stuckFor = 0f;
                GameLog.Action(LogCat.Vehicle, "STUCK — nudge failed",
                               $"'{name}' pinned on '{obstacleName}' for {resetAfterSeconds:0.#}s, requesting reset", this);
                onStuckBeyondRecovery?.Raise();
                return;
            }

            if (stuckFor < nudgeAfterSeconds) return;
            if (Time.time - lastNudgeTime < nudgeCooldown) return;

            Nudge();
        }

        /// Push out along the contact normal, biased horizontally so we never pop the
        /// car into the air, and press down so the tyres find the ground again.
        private void Nudge()
        {
            lastNudgeTime = Time.time;

            Vector3 away = obstacleNormal;
            away.y = Mathf.Max(0f, away.y * 0.25f);      // mostly sideways, never a jump
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            away.Normalize();

            rb.AddForce(away * separationSpeed, ForceMode.VelocityChange);

            int grounded = GroundedWheels();
            if (grounded < wheels.Length)
                rb.AddForce(Vector3.down * reseatForce, ForceMode.Force);

            GameLog.Action(LogCat.Vehicle, "Unstick nudge",
                           $"'{name}' off '{obstacleName}' — {grounded}/{wheels.Length} wheel(s) grounded, " +
                           $"{separationSpeed:0.0} m/s along {away:0.00}", this);
        }

        private int GroundedWheels()
        {
            int n = 0;
            foreach (var w in wheels) if (w && w.isGrounded) n++;
            return n;
        }
    }
}