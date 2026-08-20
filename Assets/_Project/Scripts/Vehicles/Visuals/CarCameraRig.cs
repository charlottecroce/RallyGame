using UnityEngine;
using RallyGame.Core;
using RallyGame.Vehicles.Controllers;

namespace RallyGame.Vehicles.Visuals
{
    /// Speed feel. FOV opens with speed and on power, the view shakes with tyre slip
    /// and rough ground, and the camera turns slightly toward where the car is
    /// actually going so a slide reads as a slide.
    ///
    /// Put this on the interior camera. If that camera is parented under the
    /// CarBodyRig's body transform it inherits the lean too, which is what you want.
    public class CarCameraRig : MonoBehaviour
    {
        [SerializeField] private CarController car;
        [SerializeField] private Camera targetCamera;

        [Header("FOV")]
        [SerializeField] private float baseFov = 62f;
        [Tooltip("FOV at top speed. 12-18 degrees of range is plenty; more induces nausea.")]
        [SerializeField] private float maxFov = 78f;
        [Tooltip("Extra FOV while on full throttle, on top of the speed term.")]
        [SerializeField] private float throttleKick = 3.5f;
        [SerializeField] private float fovRate = 3.5f;

        [Header("Shake")]
        [Tooltip("Positional shake at full slip, metres.")]
        [SerializeField] private float slipShake = 0.012f;
        [Tooltip("Shake from rough surfaces, scaled by speed.")]
        [SerializeField] private float surfaceShake = 0.010f;
        [SerializeField] private float shakeFrequency = 22f;
        [Tooltip("Landing punch, metres. Fires when vertical g spikes.")]
        [SerializeField] private float impactPunch = 0.06f;

        [Header("Look into the slide")]
        [Tooltip("Degrees of yaw toward the velocity vector at full slip angle. " +
                 "This is the single biggest rally-feel win. Keep it under ~12.")]
        [SerializeField] private float slideLookDegrees = 9f;
        [SerializeField] private float slideLookRate = 5f;

        private Vector3 basePosition;
        private Quaternion baseRotation;
        private float fov;
        private float slideYaw;
        private float punch;
        private float noiseSeed;

        private void Awake()
        {
            if (!car) car = GetComponentInParent<CarController>();
            if (!targetCamera) targetCamera = GetComponent<Camera>();

            if (!car || !targetCamera)
            {
                GameLog.Warn(LogCat.Vehicle,
                    $"'{name}' CarCameraRig is missing its CarController or Camera — disabling.", this);
                enabled = false;
                return;
            }

            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
            fov = baseFov;
            noiseSeed = Random.value * 100f;
        }

        private void LateUpdate()
        {
            var t = car.Telemetry;
            float dt = Time.deltaTime;
            float speed01 = Mathf.Clamp01(t.speedKph / 160f);

            // FOV: speed sets the floor, throttle adds the punch.
            float targetFov = Mathf.Lerp(baseFov, maxFov, speed01 * speed01)
                            + car.CurrentInput.throttle * throttleKick * speed01;
            fov = Mathf.Lerp(fov, targetFov, Mathf.Clamp01(dt * fovRate));
            targetCamera.fieldOfView = fov;

            // Yaw toward the direction of travel so oversteer is legible from inside.
            float slideAngle = SlideAngle();
            float targetYaw = Mathf.Clamp(slideAngle, -45f, 45f) / 45f * slideLookDegrees;
            slideYaw = Mathf.Lerp(slideYaw, targetYaw, Mathf.Clamp01(dt * slideLookRate));

            // Shake. Perlin rather than Random so it is smooth, not a strobe.
            float amount = t.slip01 * slipShake
                         + (1f - Mathf.Clamp01(t.surfaceGrip)) * surfaceShake * speed01;
            float ti = Time.time * shakeFrequency;
            Vector3 shake = new Vector3(
                (Mathf.PerlinNoise(noiseSeed, ti) - 0.5f) * 2f,
                (Mathf.PerlinNoise(noiseSeed + 13f, ti) - 0.5f) * 2f,
                0f) * amount;

            // Landing punch, decayed manually so it reads as one hit not a rumble.
            if (t.verticalG > 2.2f) punch = Mathf.Max(punch, Mathf.Min(1f, (t.verticalG - 2.2f) * 0.5f));
            punch = Mathf.MoveTowards(punch, 0f, dt * 4f);
            shake.y -= punch * impactPunch;

            transform.localPosition = basePosition + shake;
            transform.localRotation = baseRotation * Quaternion.Euler(0f, slideYaw, 0f);
        }

        /// Signed angle between where the car points and where it is moving.
        private float SlideAngle()
        {
            Vector3 v = car.Body.linearVelocity;
            v.y = 0f;
            if (v.sqrMagnitude < 4f) return 0f;   // below ~7 kph this is just noise
            Vector3 fwd = car.Root.forward;
            fwd.y = 0f;
            return Vector3.SignedAngle(fwd, v.normalized, Vector3.up);
        }
    }
}