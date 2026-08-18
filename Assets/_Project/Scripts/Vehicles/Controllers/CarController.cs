using UnityEngine;
using RallyGame.Utilities;
using RallyGame.Vehicles.Data;

namespace RallyGame.Vehicles.Controllers
{
    /// WheelCollider-based arcade-sim controller. Knows nothing about parts, saves
    /// or races - it only consumes VehicleInput and ResolvedCarStats.
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

        public Transform Root => transform;
        public float SpeedKph => rb ? rb.Velocity().magnitude * 3.6f : 0f;
        public float EngineRpm => rpm;
        public int Gear => gear;
        public bool EngineRunning => engineRunning;
        public float NormalisedRpm => def ? Mathf.InverseLerp(def.idleRpm, def.redlineRpm, rpm) : 0f;
        public Rigidbody Body => rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            def = fallbackDefinition;
            if (def) ApplyDefinition(def);
        }

        /// Called by CarAssembly once the OwnedCar is known.
        public void ApplyDefinition(CarDefinition definition)
        {
            def = definition;
            rb.mass = def.massKg;
            rb.centerOfMass = def.centerOfMassOffset;

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
            }
        }

        public void ApplyStats(in ResolvedCarStats s)
        {
            stats = s;
            if (rb) rb.mass = Mathf.Max(200f, s.massKg);
            ApplyFriction();
            ApplyLights();
        }

        public void SetInput(in VehicleInput i) => input = i;

        public void SetControlEnabled(bool enabled)
        {
            controlEnabled = enabled;
            if (!enabled) input = default;
        }

        public void SetEngineRunning(bool running)
        {
            engineRunning = running && stats.canStart;
            if (!engineRunning) rpm = 0f;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            rb.SetVelocity(Vector3.zero);
            rb.SetAngularVelocity(Vector3.zero);
            transform.SetPositionAndRotation(position, rotation);
            gear = 1; shiftTimer = 0f;
        }

        private void FixedUpdate()
        {
            if (def == null) return;
            UpdateSteering();
            UpdateDrive();
            UpdateBrakes();
            AntiRoll();
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

            if (!engineRunning || !controlEnabled) { SetMotorTorque(0f); return; }

            AutoShift(targetRpm);

            float curve = def.torqueCurve.Evaluate(Mathf.Clamp01(NormalisedRpm));
            float torque = stats.peakTorqueNm * curve * ratio * def.finalDrive * input.throttle;

            // Cut drive during a shift and above redline.
            if (shiftTimer > 0f || rpm >= def.redlineRpm * 0.99f) torque = 0f;

            // Engine braking when coasting.
            if (input.throttle < 0.05f && SpeedKph > 2f) torque = -engineBrakeTorque * ratio;

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
    }
}
