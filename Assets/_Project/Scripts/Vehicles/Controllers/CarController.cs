using UnityEngine;
using RallyGame.Core;
using RallyGame.Utilities;
using RallyGame.Vehicles.Data;

namespace RallyGame.Vehicles.Controllers
{
    /// WheelCollider-based arcade-sim controller. Knows nothing about parts, saves
    /// or races - it only consumes VehicleInput and ResolvedCarStats.
    ///
    /// LOGGING RULE FOR THIS FILE: FixedUpdate runs 50x/second and Update runs every
    /// frame. Nothing inside them logs directly. Instead, gear/light/engine state is
    /// compared against the last logged value and only reported when it CHANGES.
    /// Throttle, brake, steer, RPM and wheel poses are never logged at all.
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour, IVehicleController
    {
        [System.Serializable]
        public class Wheel
        {
            public WheelCollider collider;
            public Transform visual;
            public bool steers;
            public bool driven;
            public bool handbraked;
        }

        [SerializeField] private Wheel[] wheels = new Wheel[4];
        [SerializeField] private CarDefinition fallbackDefinition;  // used if nothing assembles the car
        [SerializeField] private Light[] headlights;
        [SerializeField] private Light[] rallyLights;

        [Header("Feel")]
        [SerializeField] private float steerSmoothing = 8f;
        [SerializeField] private float engineBrakeTorque = 180f;
        [SerializeField] private float antiRollForce = 4000f;

        [Header("Traction")]
        [Tooltip("Cuts drive torque when the driven wheels start spinning. Off = raw torque, " +
                 "which on a light FWD car means wheelspin from every standstill.")]
        [SerializeField] private bool tractionControl = true;
        [Tooltip("Forward slip allowed before torque is pulled back. Lower = more grip, less drama.")]
        [SerializeField] private float maxForwardSlip = 0.35f;
        [Tooltip("How fast torque is cut once slip is detected.")]
        [SerializeField] private float tractionCutRate = 8f;
        [Tooltip("How fast torque returns once grip is back. Deliberately slower than the cut " +
                 "so the two rates cannot oscillate against each other.")]
        [SerializeField] private float tractionRestoreRate = 2f;

        [Header("Debug")]
        [Tooltip("Log every gear change. Turn off if automatic shifting makes this chatty.")]
        [SerializeField] private bool logGearChanges = true;

        private Rigidbody rb;
        private CarDefinition def;
        private ResolvedCarStats stats;
        private VehicleInput input;
        private bool controlEnabled;
        private bool engineRunning;

        private int gear = 1;              // 0 = reverse, 1..N = forward
        private float shiftTimer;
        private float currentSteer;
        private float rpm;
        private float tractionScale = 1f;

        // Change-detection state so per-frame code can log without spamming.
        private int lastLoggedGear = int.MinValue;
        private bool lastLoggedLights;
        private bool lightsLogPrimed;

        public Transform Root => transform;
        public float SpeedKph => rb ? rb.Velocity().magnitude * 3.6f : 0f;
        public float EngineRpm => rpm;
        public int Gear => gear;
        public bool EngineRunning => engineRunning;
        public float NormalisedRpm => def ? Mathf.InverseLerp(def.idleRpm, def.redlineRpm, rpm) : 0f;
        public Rigidbody Body => rb;
        public ResolvedCarStats Stats => stats;
        public float TractionScale => tractionScale;
        
        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            def = fallbackDefinition;
            if (def) ApplyDefinition(def);
            else GameLog.Verbose(LogCat.Vehicle, $"'{name}' has no fallback definition — waiting for CarAssembly.", this);
        }

        /// Called by CarAssembly once the OwnedCar is known.
        public void ApplyDefinition(CarDefinition definition)
        {
            def = definition;
            rb.mass = def.massKg;
            rb.centerOfMass = def.centerOfMassOffset;

            int drivenCount = 0;
            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                bool drive = def.drivetrain switch
                {
                    Drivetrain.FWD => w.steers,
                    Drivetrain.RWD => !w.steers,
                    _ => true
                };
                w.driven = drive;
                if (drive) drivenCount++;
            }

            GameLog.Action(LogCat.Vehicle, "Definition applied",
                           $"'{name}' = {def.displayName}: {def.massKg:0}kg, {def.drivetrain} " +
                           $"({drivenCount} driven wheel(s)), redline {def.redlineRpm:0}, " +
                           $"{def.gearRatios.Length} forward gear(s)", this);
        }

        public void ApplyStats(in ResolvedCarStats s)
        {
            stats = s;
            if (rb) rb.mass = Mathf.Max(200f, s.massKg);
            ApplyFriction();
            ApplyLights();

            // Fires on fitment/wear/weather change - not per frame - so it can log.
            GameLog.Verbose(LogCat.Vehicle,
                $"Stats applied to '{name}': {stats.peakTorqueNm:0}Nm, {stats.massKg:0}kg, " +
                $"grip fwd {stats.forwardGrip:0.00}/side {stats.sidewaysGrip:0.00}, " +
                $"brakes {stats.brakeTorque:0}, canStart={stats.canStart}", this);

            if (!stats.canStart)
                GameLog.Warn(LogCat.Vehicle,
                    $"'{name}' cannot start: a required part (engine or electronics) is missing or dead.", this);
        }

        public void SetInput(in VehicleInput i) => input = i;   // per-frame, never logged

        public void SetControlEnabled(bool enabled)
        {
            if (controlEnabled == enabled) return;

            controlEnabled = enabled;
            if (!enabled) input = default;

            GameLog.Action(LogCat.Vehicle,
                           enabled ? "Driver control ENABLED" : "Driver control DISABLED",
                           $"'{name}' at {SpeedKph:0} kph", this);
        }

        public void SetEngineRunning(bool running)
        {
            bool wanted = running;
            bool result = running && stats.canStart;

            if (engineRunning == result)
            {
                // Requested a start but the car refused - worth one line.
                if (wanted && !result)
                    GameLog.Refused(LogCat.Vehicle, $"start engine on '{name}'",
                                    "resolved stats say canStart = false", this);
                return;
            }

            engineRunning = result;
            if (!engineRunning) rpm = 0f;

            GameLog.Action(LogCat.Vehicle, engineRunning ? "ENGINE STARTED" : "ENGINE STOPPED",
                           $"'{name}', gear {GearName(gear)}, {SpeedKph:0} kph", this);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            GameLog.Action(LogCat.Vehicle, "Car teleported",
                           $"'{name}' {transform.position:0.0} -> {position:0.0} " +
                           $"(velocity zeroed, gear reset to 1)", this);

            rb.SetVelocity(Vector3.zero);
            rb.SetAngularVelocity(Vector3.zero);
            transform.SetPositionAndRotation(position, rotation);
            gear = 1; shiftTimer = 0f;
            tractionScale = 1f;
        }

        private void FixedUpdate()
        {
            if (def == null) return;
            UpdateSteering();
            UpdateDrive();
            UpdateBrakes();
            AntiRoll();

            ReportGearChange();   // change-detection only: silent unless the gear moved
        }

        private void Update() => UpdateWheelVisuals();

        // ---- driving -------------------------------------------------------

        private void UpdateSteering()
        {
            // Steering authority tapers with speed for stability at pace.
            float speedFactor = Mathf.Lerp(1f, def.highSpeedSteerScale,
                Mathf.InverseLerp(0f, def.topSpeedKph, SpeedKph));
            float target = input.steer * stats.maxSteerAngle * speedFactor;
            currentSteer = Mathf.Lerp(currentSteer, target, Time.fixedDeltaTime * steerSmoothing * stats.steerResponse);

            foreach (var w in wheels)
                if (w.steers && w.collider) w.collider.steerAngle = currentSteer;
        }

        private void UpdateDrive()
        {
            float wheelRpm = AverageDrivenWheelRpm();
            float ratio = CurrentRatio();
            float targetRpm = Mathf.Abs(wheelRpm) * ratio * def.finalDrive;
            rpm = engineRunning
                ? Mathf.Lerp(rpm, Mathf.Clamp(targetRpm, def.idleRpm, def.redlineRpm), Time.fixedDeltaTime * 6f)
                : 0f;

            if (!engineRunning || !controlEnabled) { SetMotorTorque(0f); tractionScale = 1f; return; }

            AutoShift(targetRpm);

            float curve = def.torqueCurve.Evaluate(Mathf.Clamp01(NormalisedRpm));
            float torque = stats.peakTorqueNm * curve * ratio * def.finalDrive * input.throttle;

            // Cut drive during a shift and above redline.
            if (shiftTimer > 0f || rpm >= def.redlineRpm * 0.99f) torque = 0f;

            // Engine braking when coasting.
            if (input.throttle < 0.05f && SpeedKph > 2f) torque = -engineBrakeTorque * ratio;

            // There is no clutch slip or driveline inertia in this model, so full
            // first-gear torque lands in a single physics step and the tires are
            // already sliding before the car has moved. Scale drive back toward
            // whatever the contact patch will actually take.
            if (tractionControl && torque > 0f)
            {
                float slip = PeakDrivenForwardSlip();
                float target = slip > maxForwardSlip ? Mathf.Clamp01(maxForwardSlip / slip) : 1f;
                float rate = target < tractionScale ? tractionCutRate : tractionRestoreRate;
                tractionScale = Mathf.MoveTowards(tractionScale, target, Time.fixedDeltaTime * rate);
                torque *= tractionScale;
            }
            else
            {
                tractionScale = 1f;
            }

            SetMotorTorque(torque / DrivenWheelCount());
        }

        private void AutoShift(float targetRpm)
        {
            if (shiftTimer > 0f) { shiftTimer -= Time.fixedDeltaTime; return; }

            // Manual requests win over automatic logic.
            if (input.shiftUp && gear < def.gearRatios.Length) { gear++; shiftTimer = stats.shiftTime; return; }
            if (input.shiftDown && gear > 0) { gear--; shiftTimer = stats.shiftTime; return; }

            // Reverse only from a near stop.
            if (input.brake > 0.5f && SpeedKph < 2f && gear == 1) { gear = 0; shiftTimer = stats.shiftTime; return; }
            if (gear == 0 && input.throttle > 0.5f && SpeedKph < 2f) { gear = 1; shiftTimer = stats.shiftTime; return; }

            if (gear == 0) return;
            if (rpm > def.redlineRpm * 0.92f && gear < def.gearRatios.Length) { gear++; shiftTimer = stats.shiftTime; }
            else if (rpm < def.idleRpm * 1.4f && gear > 1) { gear--; shiftTimer = stats.shiftTime; }
        }

        private void UpdateBrakes()
        {
            float brake = input.brake * stats.brakeTorque;
            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                float t = brake;
                if (input.handbrake && w.handbraked) t = Mathf.Max(t, stats.handbrakeTorque);
                w.collider.brakeTorque = t;
            }
        }

        private void ApplyFriction()
        {
            foreach (var w in wheels)
            {
                if (!w.collider) continue;

                var fwd = w.collider.forwardFriction;
                fwd.stiffness = Mathf.Max(0.2f, stats.forwardGrip);
                w.collider.forwardFriction = fwd;

                var side = w.collider.sidewaysFriction;
                // Handbrake slides need a rear grip drop; do it on the friction curve, not by faking torque.
                side.stiffness = Mathf.Max(0.2f, stats.sidewaysGrip * (input.handbrake && w.handbraked ? 0.55f : 1f));
                w.collider.sidewaysFriction = side;

                var spring = w.collider.suspensionSpring;
                spring.spring = Mathf.Max(5000f, 22000f * stats.suspensionStiffness);
                spring.damper = Mathf.Max(500f, 3000f * stats.suspensionStiffness);
                w.collider.suspensionSpring = spring;
            }
        }

        private void ApplyLights()
        {
            foreach (var l in headlights) if (l) { l.enabled = input.lights; l.range = Mathf.Max(30f, 30f + stats.lightRange); }
            foreach (var l in rallyLights) if (l) { l.enabled = input.lights && stats.lightRange > 0f; l.range = 40f + stats.lightRange; }

            // Edge-triggered: only when the switch actually flips.
            if (!lightsLogPrimed || input.lights != lastLoggedLights)
            {
                lightsLogPrimed = true;
                lastLoggedLights = input.lights;
                GameLog.Action(LogCat.Vehicle, input.lights ? "Lights ON" : "Lights OFF",
                               $"'{name}': {headlights.Length} headlight(s), {rallyLights.Length} rally light(s), " +
                               $"bonus range {stats.lightRange:0}", this);
            }
        }

        /// Cheap anti-roll bar: keeps the car from tripping over itself on jumps.
        private void AntiRoll()
        {
            for (int i = 0; i + 1 < wheels.Length; i += 2)
            {
                float l = Travel(wheels[i].collider);
                float r = Travel(wheels[i + 1].collider);
                float force = (l - r) * antiRollForce;
                if (wheels[i].collider.isGrounded)
                    rb.AddForceAtPosition(wheels[i].collider.transform.up * -force, wheels[i].collider.transform.position);
                if (wheels[i + 1].collider.isGrounded)
                    rb.AddForceAtPosition(wheels[i + 1].collider.transform.up * force, wheels[i + 1].collider.transform.position);
            }
        }

        private float Travel(WheelCollider wc)
        {
            if (!wc) return 1f;
            if (!wc.GetGroundHit(out var hit)) return 1f;
            return (-wc.transform.InverseTransformPoint(hit.point).y - wc.radius) / wc.suspensionDistance;
        }

        private void UpdateWheelVisuals()
        {
            foreach (var w in wheels)
            {
                if (!w.collider || !w.visual) continue;
                w.collider.GetWorldPose(out var pos, out var rot);
                w.visual.SetPositionAndRotation(pos, rot);
            }
        }

        // ---- debug ---------------------------------------------------------

        /// Called from FixedUpdate but only speaks when the gear actually moved.
        private void ReportGearChange()
        {
            if (!logGearChanges || gear == lastLoggedGear) return;

            string from = GearName(lastLoggedGear);
            lastLoggedGear = gear;

            GameLog.Action(LogCat.Vehicle, "Gear change",
                           $"'{name}' {from} -> {GearName(gear)} at {SpeedKph:0} kph, {rpm:0} rpm", this);
        }

        private static string GearName(int g)
            => g == int.MinValue ? "-" : g == 0 ? "R" : g.ToString();

        // ---- helpers -------------------------------------------------------

        private float CurrentRatio() => gear == 0 ? -def.reverseRatio : def.gearRatios[Mathf.Clamp(gear - 1, 0, def.gearRatios.Length - 1)];

        private void SetMotorTorque(float perWheel)
        {
            foreach (var w in wheels)
                if (w.collider && w.driven) w.collider.motorTorque = perWheel;
        }

        private int DrivenWheelCount()
        {
            int n = 0;
            foreach (var w in wheels) if (w.driven) n++;
            return Mathf.Max(1, n);
        }

        private float AverageDrivenWheelRpm()
        {
            float sum = 0f; int n = 0;
            foreach (var w in wheels) if (w.collider && w.driven) { sum += w.collider.rpm; n++; }
            return n == 0 ? 0f : sum / n;
        }

        /// Worst forward slip across the driven wheels. WheelCollider reports this in
        /// its own units: near zero is rolling, larger means the patch is sliding.
        /// Airborne wheels are skipped — GetGroundHit fails and they would otherwise
        /// read as infinite slip and kill drive on every jump.
        private float PeakDrivenForwardSlip()
        {
            float peak = 0f;
            foreach (var w in wheels)
            {
                if (!w.collider || !w.driven) continue;
                if (!w.collider.GetGroundHit(out var hit)) continue;
                peak = Mathf.Max(peak, Mathf.Abs(hit.forwardSlip));
            }
            return peak;
        }
    }
}