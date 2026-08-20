using UnityEngine;
using RallyGame.Core;
using RallyGame.Vehicles.Controllers;

namespace RallyGame.Vehicles.Visuals
{
    /// Moves the visual shell on top of the physics body: dive, squat, roll, heave.
    ///
    /// This NEVER touches the rigidbody or the WheelColliders. Point it at a child
    /// pivot that holds only the body mesh (and the interior camera, if you want the
    /// camera to inherit the lean
    ///
    /// Two sources are blended: suspension compression (honest, follows bumps, but
    /// lags the driver) and chassis g-force (instant, reads as intent). Compression
    /// alone feels late; g-force alone feels like the body is on a spring of its own.
    [RequireComponent(typeof(CarController))]
    public class CarBodyRig : MonoBehaviour
    {
        [Tooltip("Child transform holding the body mesh. Must NOT contain WheelColliders.")]
        [SerializeField] private Transform body;

        [Header("Lean")]
        [Tooltip("Degrees of nose-down pitch per g of braking.")]
        [SerializeField] private float pitchDegreesPerG = 2.6f;
        [Tooltip("Degrees of roll per g of cornering. Rally cars roll more than you think.")]
        [SerializeField] private float rollDegreesPerG = 3.4f;
        [Tooltip("Extra lean taken straight from suspension compression rather than g.")]
        [Range(0f, 1f)] [SerializeField] private float suspensionShare = 0.45f;
        [SerializeField] private float suspensionPitchDegrees = 4f;
        [SerializeField] private float suspensionRollDegrees = 5f;

        [Header("Heave")]
        [Tooltip("Vertical travel of the shell, metres. Squats on landing, floats on crests.")]
        [SerializeField] private float heaveMetres = 0.05f;
        [Tooltip("Longitudinal shift under load. Tiny - a couple of centimetres reads as mass.")]
        [SerializeField] private float surgeMetres = 0.025f;

        [Header("Response")]
        [Tooltip("How fast the shell chases its target pose. Low = boat, high = rigid.")]
        [SerializeField] private float responseRate = 9f;
        [Tooltip("Overshoot on direction changes. 0 = critically damped, 0.3 = lively.")]
        [Range(0f, 0.5f)] [SerializeField] private float springiness = 0.18f;

        [Header("Limits")]
        [SerializeField] private float maxPitchDegrees = 7f;
        [SerializeField] private float maxRollDegrees = 8f;

        private CarController car;
        private Vector3 basePosition;
        private Quaternion baseRotation;

        private float pitch, roll, heave, surge;
        private float pitchVel, rollVel;

        private void Awake()
        {
            car = GetComponent<CarController>();

            if (body == null)
            {
                GameLog.Warn(LogCat.Vehicle,
                    $"'{name}' has a CarBodyRig with no body transform — the shell will not lean. " +
                    "Create an empty child (e.g. 'Body'), parent the car mesh and interior camera " +
                    "under it, leave the WheelColliders on the root, then assign it here.", this);
                enabled = false;
                return;
            }

            basePosition = body.localPosition;
            baseRotation = body.localRotation;
        }

        /// Visual only, so LateUpdate: runs after physics and after wheel poses.
        private void LateUpdate()
        {
            var t = car.Telemetry;
            float dt = Time.deltaTime;

            // Suspension term: front compressed more than rear = nose down.
            float suspPitch = (t.frontCompression - t.rearCompression) * suspensionPitchDegrees;
            float suspRoll = (t.leftCompression - t.rightCompression) * suspensionRollDegrees;

            float targetPitch = Mathf.Lerp(t.pitchBias * pitchDegreesPerG, suspPitch, suspensionShare);
            float targetRoll = Mathf.Lerp(t.rollBias * rollDegreesPerG, suspRoll, suspensionShare);

            targetPitch = Mathf.Clamp(targetPitch, -maxPitchDegrees, maxPitchDegrees);
            targetRoll = Mathf.Clamp(targetRoll, -maxRollDegrees, maxRollDegrees);

            // Airborne the body hangs on its springs instead of leaning to nothing.
            if (t.Airborne) { targetPitch *= 0.3f; targetRoll *= 0.3f; }

            pitch = Spring(pitch, targetPitch, ref pitchVel, dt);
            roll = Spring(roll, targetRoll, ref rollVel, dt);

            float targetHeave = (t.averageCompression - 0.5f) * -2f * heaveMetres;
            float targetSurge = Mathf.Clamp(t.longitudinalG, -1f, 1f) * surgeMetres;
            float k = 1f - Mathf.Exp(-responseRate * dt);
            heave = Mathf.Lerp(heave, targetHeave, k);
            surge = Mathf.Lerp(surge, targetSurge, k);

            body.localRotation = baseRotation * Quaternion.Euler(pitch, 0f, -roll);
            body.localPosition = basePosition + new Vector3(0f, heave, surge);
        }

        /// Damped spring rather than a lerp, so the body settles with a little wobble
        /// instead of gliding into place.
        private float Spring(float current, float target, ref float velocity, float dt)
        {
            float omega = responseRate;
            float damping = Mathf.Lerp(1f, 0.55f, springiness / 0.5f);
            float force = (target - current) * omega * omega - velocity * 2f * damping * omega;
            velocity += force * dt;
            return current + velocity * dt;
        }
    }
}