using System.Text;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Utilities;

namespace RallyGame.Vehicles.Controllers
{
    /// Temporary diagnostic. Answers one question: are the wheels touching the ground?
    ///
    /// Drop it on the car prefab root and press play. It finds its own wheel colliders,
    /// so there is nothing to assign. Delete the component once the rig is trusted.
    ///
    /// Reading the output:
    ///   0/4 grounded  -> the car is resting on some other collider. The startup report
    ///                    lists every collider on the prefab and how low each one sits
    ///                    relative to the wheels; whatever hangs below them is the
    ///                    culprit. This also explains crashes on spawn.
    ///   4/4 grounded  -> the wheels are down and simply spinning. Look at the rpm and
    ///                    motorTorque columns: high rpm with the car stationary is
    ///                    wheelspin, and the fix is in the drive model, not the rig.
    [RequireComponent(typeof(CarController))]
    [RequireComponent(typeof(Rigidbody))]
    public class WheelDiagnostics : MonoBehaviour
    {
        [Tooltip("Seconds between report lines. 0 logs every frame — do not.")]
        [SerializeField] private float intervalSeconds = 1f;
        [Tooltip("Only report while the car is actually being driven. Off = report always.")]
        [SerializeField] private bool onlyWhileMoving = false;
        [Tooltip("List every collider on the prefab once at startup, sorted by how low it sits.")]
        [SerializeField] private bool reportCollidersOnStart = true;

        private CarController controller;
        private Rigidbody rb;
        private WheelCollider[] wheels;
        private float next;

        private void Awake()
        {
            controller = GetComponent<CarController>();
            rb = GetComponent<Rigidbody>();
            wheels = GetComponentsInChildren<WheelCollider>(true);

            if (wheels.Length == 0)
                GameLog.Error(LogCat.Vehicle,
                    $"'{name}' has no WheelColliders anywhere in its hierarchy. " +
                    "The car cannot drive at all — the wheel rig was never built.", this);
        }

        private void Start()
        {
            if (reportCollidersOnStart) ReportColliders();
        }

        // ---- one-shot startup report ---------------------------------------

        /// The question "what is the car actually resting on" is answered by whichever
        /// collider reaches lowest. If anything sits below the bottom of the wheels, the
        /// wheels will never reach the ground.
        private void ReportColliders()
        {
            float wheelBottom = float.MaxValue;
            foreach (var w in wheels)
                if (w) wheelBottom = Mathf.Min(wheelBottom, w.transform.position.y - w.radius);

            var sb = new StringBuilder();
            sb.AppendLine($"Collider inventory for '{name}':");
            sb.AppendLine($"  Wheels reach down to y = {wheelBottom:0.000} " +
                          $"({wheels.Length} wheel(s), radius {(wheels.Length > 0 && wheels[0] ? wheels[0].radius : 0f):0.000} m)");
            sb.AppendLine($"  Rigidbody: {rb.mass:0} kg, centre of mass {rb.centerOfMass}");

            int below = 0;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c is WheelCollider) continue;

                float bottom = c.bounds.min.y;
                bool isLower = bottom < wheelBottom;
                if (isLower && !c.isTrigger) below++;

                sb.AppendLine($"  {(isLower && !c.isTrigger ? "!!" : "  ")} '{c.name}' " +
                              $"({c.GetType().Name}){(c.isTrigger ? " [trigger]" : "")} " +
                              $"bottom y = {bottom:0.000}" +
                              (isLower ? $"  <-- {(wheelBottom - bottom) * 100f:0} cm BELOW the wheels" : ""));
            }

            if (below > 0)
            {
                sb.Append($"\n  {below} solid collider(s) hang below the wheels. " +
                          "The car will rest on those and the wheels will never touch the ground.");
                GameLog.Error(LogCat.Vehicle, sb.ToString(), this);
            }
            else
            {
                sb.Append("\n  No solid collider hangs below the wheels. The rig geometry is fine.");
                GameLog.Action(LogCat.Vehicle, "Collider inventory", sb.ToString(), this);
            }
        }

        // ---- per-interval report -------------------------------------------

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + Mathf.Max(0.1f, intervalSeconds);

            if (onlyWhileMoving && !controller.EngineRunning) return;

            int grounded = 0;
            var sb = new StringBuilder();


var s = controller.Stats;
sb.Append($"Wheel rig — body {controller.SpeedKph:0.0} kph, " +
          $"{controller.EngineRpm:0} rpm, gear {controller.Gear}, " +
          $"engine {(controller.EngineRunning ? "on" : "off")}");
sb.Append($"\n  STATS: peakTorque={s.peakTorqueNm:0.0}Nm  mass={s.massKg:0}kg  " +
          $"gripFwd={s.forwardGrip:0.00}  brakeTorque={s.brakeTorque:0}  " +
          $"canStart={s.canStart}  tractionScale={controller.TractionScale:0.00}");

            foreach (var w in wheels)
            {
                if (!w) continue;
                if (w.isGrounded) grounded++;

                // Expected wheel rpm if the tire were rolling without slip, for comparison.
                float rollingRpm = w.radius > 0.001f
                    ? (rb.Velocity().magnitude / (2f * Mathf.PI * w.radius)) * 60f
                    : 0f;

                sb.Append($"\n  {w.name,-8} r={w.radius:0.000}m  " +
                          $"grounded={(w.isGrounded ? "YES" : "no ")}  " +
                          $"{w.rpm,7:0} rpm (rolling would be {rollingRpm:0})  " +
                          $"motor={w.motorTorque,7:0}  brake={w.brakeTorque:0}");
            }

            sb.Append($"\n  {grounded}/{wheels.Length} on the ground.");

            if (wheels.Length == 0) return;

            if (grounded == 0)
                GameLog.Warn(LogCat.Vehicle,
                    sb + "\n  Nothing is touching the ground — the car is sitting on something else.", this);
            else if (grounded < wheels.Length)
                GameLog.Warn(LogCat.Vehicle, sb.ToString(), this);
            else
                GameLog.Action(LogCat.Vehicle, "Wheel rig OK", sb.ToString(), this);
        }
    }
}