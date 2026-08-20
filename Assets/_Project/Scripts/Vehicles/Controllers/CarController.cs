using UnityEngine;
using RallyGame.Core;
using RallyGame.Utilities;
using RallyGame.Vehicles.Data;

namespace RallyGame.Vehicles.Controllers
{
    /// WheelCollider sim-cade controller with an explicit weight-transfer model.
    ///
    /// The old version wrote one grip number to all four wheels and only rewrote it
    /// when a stat changed. That is why it felt floaty: nothing the driver did could
    /// change where the grip was. Now each wheel gets its own load every step, load
    /// drives a sub-linear grip coefficient, and grip is written per wheel per step.
    /// Brake hard -> load leaves the rear -> the rear will step out. That is the whole
    /// point.
    ///
    /// Load comes from a model rather than only from Unity's suspension because the
    /// model responds on the same frame as the input, while a spring takes ~200ms to
    /// settle. `measuredLoadBlend` folds the real suspension force back in so bumps
    /// and landings still register.
    ///
    /// LOGGING RULE: FixedUpdate/Update never log directly. Gear, lights, engine and
    /// transmission mode are change-detected and only reported when they move.
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

            // Runtime. Derived, never authored.
            [System.NonSerialized] public bool front;
            [System.NonSerialized] public bool left;
            [System.NonSerialized] public bool grounded;
            [System.NonSerialized] public float staticLoadN = 2500f;
            [System.NonSerialized] public float loadN = 2500f;
            [System.NonSerialized] public float normalisedLoad = 1f;
            [System.NonSerialized] public float gripScale = 1f;
            [System.NonSerialized] public float extension01 = 1f;  // 1 = hanging, 0 = bottomed
            [System.NonSerialized] public float forwardSlip;
            [System.NonSerialized] public float sidewaysSlip;

            public float Compression01 => 1f - extension01;
        }

        [SerializeField] private Wheel[] wheels = new Wheel[4];
        [SerializeField] private CarDefinition fallbackDefinition;
        [SerializeField] private Light[] headlights;
        [SerializeField] private Light[] rallyLights;

        [Header("Feel")]
        [SerializeField] private float steerSmoothing = 8f;
        [Tooltip("Steering returns to centre faster than it goes on. Stops the car " +
                 "feeling like it is steering through treacle when you unwind.")]
        [SerializeField] private float steerReturnMultiplier = 1.6f;
        [SerializeField] private float antiRollForce = 5200f;
        [Tooltip("Extra yaw damping while all four are down. Small values only - this is " +
                 "the difference between 'planted' and 'on rails'.")]
        [SerializeField] private float yawDamping = 0.15f;

        [Header("Weight transfer")]
        [Tooltip("How much the modelled transfer is allowed to override Unity's own " +
                 "suspension load. 0 = vanilla WheelCollider, 1 = fully modelled. " +
                 "0.5-0.65 is the sim-cade sweet spot.")]
        [Range(0f, 1f)] [SerializeField] private float transferAuthority = 0.6f;
        [Tooltip("How much real measured suspension force is folded back into the model. " +
                 "Keeps bumps and landings honest.")]
        [Range(0f, 1f)] [SerializeField] private float measuredLoadBlend = 0.35f;
        [Tooltip("Low-pass on load. Too high = grip chatters on rough ground, too low = mushy.")]
        [SerializeField] private float loadFilterRate = 18f;
        [Tooltip("Low-pass on the g-force readings that feed transfer and the body rig.")]
        [SerializeField] private float gFilterRate = 12f;
        [Tooltip("Also slide the rigidbody's centre of mass under load, so Unity's own " +
                 "suspension dives and squats with you. Keep the travel small.")]
        [SerializeField] private bool dynamicCentreOfMass = true;
        [SerializeField] private float comShiftMetres = 0.09f;

        [Header("Traction")]
        [SerializeField] private bool tractionControl = true;
        [SerializeField] private float maxForwardSlip = 0.35f;
        [SerializeField] private float tractionCutRate = 8f;
        [Tooltip("Restore is deliberately slower than the cut so the two cannot oscillate, " +
                 "but too slow and the car never recovers from a slip on a climb.")]
        [SerializeField] private float tractionRestoreRate = 4f;
        [Tooltip("TC is never allowed to cut below this fraction of drive torque. Without " +
                 "a floor, repeated slip on a loose incline strangles the engine entirely.")]
        [Range(0.1f, 1f)] [SerializeField] private float minTractionScale = 0.4f;
        [Tooltip("Below this speed TC is bypassed. Pulling away IS wheelspin; cutting " +
                 "torque there just means you never move.")]
        [SerializeField] private float tractionMinSpeedKph = 6f;

        [Header("Gearbox")]
        [Tooltip("Road speed at which the clutch is fully locked. The clutch exists to " +
                 "launch the car, nothing else - above this it must never slip, or a " +
                 "climb turns into a feedback loop of slip -> less torque -> more slip.")]
        [SerializeField] private float clutchLockKph = 8f;
        [Tooltip("Speed below which reverse can be selected.")]
        [SerializeField] private float reverseEngageKph = 5f;
        [Tooltip("Auto mode upshifts at this fraction of redline.")]
        [Range(0.6f, 1f)] [SerializeField] private float autoUpshiftPoint = 0.92f;
        [Tooltip("Auto mode kickdown: full throttle below this fraction of redline drops " +
                 "a gear. This is what gets you up hills without lugging.")]
        [Range(0.2f, 0.8f)] [SerializeField] private float autoKickdownPoint = 0.55f;
        [Tooltip("Rev limiter cut duration. Short = a hard flutter you can hear and see.")]
        [SerializeField] private float limiterCutSeconds = 0.06f;

        [Header("Debug")]
        [SerializeField] private bool logGearChanges = true;

        private Rigidbody rb;
        private CarDefinition def;
        private ResolvedCarStats stats;
        private VehicleInput input;
        private bool controlEnabled;
        private bool engineRunning;

        private int gear = Gearbox.Neutral;
        private float shiftTimer;
        private float currentSteer;
        private float rpm;
        private float clutchLock = 1f;         // 1 = fully engaged
        private float tractionScale = 1f;
        private float surfaceGrip = 1f;
        private float limiterTimer;
        private float engineBrakeNow;

        // Chassis geometry, derived from the wheel rig.
        private float wheelbase = 2.5f;
        private float trackWidth = 1.5f;
        private float wheelRadius = 0.32f;     // WORLD radius, scale included
        private float derivedDrag = 0.45f;
        private Vector3 baseCentreOfMass;

        private Vector3 lastVelocity;
        private float longG, latG, vertG;
        private float downforceN;
        private CarTelemetry telemetry;

        // Change-detection state.
        private int lastLoggedGear = int.MinValue;
        private bool lastLoggedLights;
        private bool lightsLogPrimed;
        private TransmissionMode lastLoggedMode = TransmissionMode.Automatic;
        private bool modeLogPrimed;

        public Transform Root => transform;
        public float SpeedKph => rb ? rb.Velocity().magnitude * 3.6f : 0f;
        public float EngineRpm => rpm;
        public int Gear => gear;
        public bool EngineRunning => engineRunning;
        public float NormalisedRpm => def ? Mathf.InverseLerp(def.idleRpm, def.redlineRpm, rpm) : 0f;
        public Rigidbody Body => rb;
        public ResolvedCarStats Stats => stats;
        public float TractionScale => tractionScale;
        public float SurfaceGrip => surfaceGrip;
        public float ClutchLock => clutchLock;
        public TransmissionMode Transmission => input.transmission;
        public CarTelemetry Telemetry => telemetry;
        public VehicleInput CurrentInput => input;
        public bool ControlEnabled => controlEnabled;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            def = fallbackDefinition;
            if (def) ApplyDefinition(def);
            else GameLog.Verbose(LogCat.Vehicle, $"'{name}' has no fallback definition — waiting for CarAssembly.", this);
        }

        // ---- setup ---------------------------------------------------------

        public void ApplyDefinition(CarDefinition definition)
        {
            def = definition;
            rb.mass = def.massKg;
            baseCentreOfMass = def.centerOfMassOffset;
            rb.centerOfMass = baseCentreOfMass;

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

            RecomputeGeometry();
            ApplySuspension();

            GameLog.Action(LogCat.Vehicle, "Definition applied",
                           $"'{name}' = {def.displayName}: {def.massKg:0}kg, {def.drivetrain} " +
                           $"({drivenCount} driven), wheelbase {wheelbase:0.00}m, track {trackWidth:0.00}m, " +
                           $"rolling radius {wheelRadius:0.00}m, CG {def.cgHeightM:0.00}m", this);
        }

        public void ApplyStats(in ResolvedCarStats s)
        {
            stats = s;
            if (rb) rb.mass = Mathf.Max(200f, s.massKg);
            RecomputeGeometry();
            ApplySuspension();
            ApplyLights();

            GameLog.Verbose(LogCat.Vehicle,
                $"Stats applied to '{name}': {stats.peakTorqueNm:0}Nm, {stats.massKg:0}kg, " +
                $"grip fwd {stats.forwardGrip:0.00}/side {stats.sidewaysGrip:0.00}, " +
                $"brakes {stats.brakeTorque:0}, drag {derivedDrag:0.00}, canStart={stats.canStart}", this);

            if (!stats.canStart)
                GameLog.Warn(LogCat.Vehicle,
                    $"'{name}' cannot start: a required part (engine or electronics) is missing or dead.", this);
        }

        /// Measures the rig instead of trusting authored numbers, so a rescaled prefab
        /// still gets a correct wheelbase. Also derives drag from the power on hand.
        private void RecomputeGeometry()
        {
            if (def == null) return;

            // Mean local Z tells us which end each wheel is on.
            float meanZ = 0f; int n = 0;
            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                meanZ += transform.InverseTransformPoint(w.collider.transform.position).z;
                n++;
            }
            if (n == 0) return;
            meanZ /= n;

            float fz = 0f, rz = 0f, lx = 0f, rx = 0f, radius = 0f;
            int fn = 0, rn = 0, ln = 0, rrn = 0, cn = 0;

            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                Vector3 p = transform.InverseTransformPoint(w.collider.transform.position);
                w.front = p.z >= meanZ;
                w.left = p.x < 0f;

                if (w.front) { fz += p.z; fn++; } else { rz += p.z; rn++; }
                if (w.left) { lx += p.x; ln++; } else { rx += p.x; rrn++; }
                radius += w.collider.radius; cn++;
            }

            // Local-space distances: these are already in the same units the model uses.
            wheelbase = Mathf.Max(1.2f, Mathf.Abs((fn > 0 ? fz / fn : 1f) - (rn > 0 ? rz / rn : -1f)));
            trackWidth = Mathf.Max(0.8f, Mathf.Abs((ln > 0 ? lx / ln : -0.75f) - (rrn > 0 ? rx / rrn : 0.75f)));

            // Rolling radius must be WORLD scale. A prefab scaled 2x rolls twice as far
            // per revolution, so using the raw collider radius doubles the apparent
            // thrust and therefore the derived drag.
            float scale = Mathf.Abs(transform.lossyScale.y);
            wheelRadius = Mathf.Max(0.05f, (cn > 0 ? radius / cn : 0.32f) * (scale > 0.001f ? scale : 1f));

            // Static corner loads at 1g, the reference the load curve is normalised against.
            float total = rb.mass * 9.81f;
            int frontCount = 0, rearCount = 0;
            foreach (var w in wheels) { if (!w.collider) continue; if (w.front) frontCount++; else rearCount++; }
            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                w.staticLoadN = w.front
                    ? total * def.frontWeightBias / Mathf.Max(1, frontCount)
                    : total * (1f - def.frontWeightBias) / Mathf.Max(1, rearCount);
                if (w.loadN <= 1f) w.loadN = w.staticLoadN;
            }

            // Drag that makes topSpeedKph mean something: balance thrust in top gear.
            if (def.deriveDragFromTopSpeed)
            {
                float vMax = Mathf.Max(10f, def.topSpeedKph / 3.6f);
                float topRatio = def.gearRatios.Length > 0 ? def.gearRatios[def.gearRatios.Length - 1] : 1f;
                float torque = Mathf.Max(1f, stats.peakTorqueNm > 0f ? stats.peakTorqueNm : def.peakTorqueNm);
                float thrust = torque * def.torqueCurve.Evaluate(1f) * topRatio * def.finalDrive / wheelRadius;
                derivedDrag = Mathf.Clamp(thrust / (vMax * vMax), 0.05f, 4f);
            }
            else derivedDrag = def.aeroDrag;
        }

        /// Springs only. Friction is now written every step, not here.
        private void ApplySuspension()
        {
            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                var spring = w.collider.suspensionSpring;
                spring.spring = Mathf.Max(5000f, 22000f * stats.suspensionStiffness);
                spring.damper = Mathf.Max(500f, 3400f * stats.suspensionStiffness);
                w.collider.suspensionSpring = spring;
            }
        }

        public void SetInput(in VehicleInput i) => input = i;

        public void SetSurfaceGrip(float multiplier) => surfaceGrip = Mathf.Clamp(multiplier, 0.1f, 2f);

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
                if (wanted && !result)
                    GameLog.Refused(LogCat.Vehicle, $"start engine on '{name}'",
                                    "resolved stats say canStart = false", this);
                return;
            }

            engineRunning = result;
            rpm = engineRunning ? def.idleRpm : 0f;
            if (engineRunning && gear == Gearbox.Neutral) gear = 1;   // roll away in first

            GameLog.Action(LogCat.Vehicle, engineRunning ? "ENGINE STARTED" : "ENGINE STOPPED",
                           $"'{name}', gear {Gearbox.Label(gear)}, {SpeedKph:0} kph", this);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            GameLog.Action(LogCat.Vehicle, "Car teleported",
                           $"'{name}' {transform.position:0.0} -> {position:0.0}", this);

            rb.SetVelocity(Vector3.zero);
            rb.SetAngularVelocity(Vector3.zero);
            transform.SetPositionAndRotation(position, rotation);

            gear = 1; shiftTimer = 0f; tractionScale = 1f; surfaceGrip = 1f;
            longG = latG = 0f; vertG = 1f;
            lastVelocity = Vector3.zero;
            rb.centerOfMass = baseCentreOfMass;
            foreach (var w in wheels) { w.loadN = w.staticLoadN; w.normalisedLoad = 1f; w.gripScale = 1f; }
            ApplySuspension();
        }

        // ---- step ----------------------------------------------------------

        private void FixedUpdate()
        {
            if (def == null) return;
            float dt = Time.fixedDeltaTime;

            SampleChassis(dt);
            SampleWheels();
            UpdateAero();
            UpdateLoads(dt);
            UpdateTyres();
            UpdateSteering(dt);
            UpdateEngine(dt);
            UpdateDrive();
            UpdateBrakes();
            AntiRoll();
            UpdateCentreOfMass(dt);
            BuildTelemetry();

            ReportGearChange();
            ReportModeChange();
        }

        private void Update() => UpdateWheelVisuals();

        // ---- chassis -------------------------------------------------------

        /// Specific force, not raw acceleration: gravity is removed so that sitting
        /// still reads 1g down and freefall reads zero. On a climb this correctly
        /// reads as rearward transfer, which is why a FWD car loses its nose uphill.
        private void SampleChassis(float dt)
        {
            Vector3 v = rb.Velocity();
            Vector3 aWorld = (v - lastVelocity) / Mathf.Max(0.0001f, dt);
            lastVelocity = v;

            Vector3 aLocal = transform.InverseTransformDirection(aWorld - Physics.gravity);

            float k = 1f - Mathf.Exp(-gFilterRate * dt);   // framerate-independent low-pass
            longG = Mathf.Lerp(longG, Mathf.Clamp(aLocal.z / 9.81f, -3f, 3f), k);
            latG = Mathf.Lerp(latG, Mathf.Clamp(aLocal.x / 9.81f, -3f, 3f), k);
            vertG = Mathf.Lerp(vertG, Mathf.Clamp(aLocal.y / 9.81f, 0f, 4f), k);
        }

        private void SampleWheels()
        {
            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                w.grounded = w.collider.GetGroundHit(out var hit);
                if (w.grounded)
                {
                    w.forwardSlip = hit.forwardSlip;
                    w.sidewaysSlip = hit.sidewaysSlip;
                    float drop = -w.collider.transform.InverseTransformPoint(hit.point).y - w.collider.radius;
                    w.extension01 = Mathf.Clamp01(drop / Mathf.Max(0.01f, w.collider.suspensionDistance));
                }
                else
                {
                    w.forwardSlip = 0f;
                    w.sidewaysSlip = 0f;
                    w.extension01 = 1f;
                }
            }
        }

        private void UpdateAero()
        {
            Vector3 v = rb.Velocity();
            float speed = v.magnitude;
            if (speed < 0.5f) { downforceN = 0f; return; }

            // Drag. This is what gives lifting off a consequence and caps top speed.
            rb.AddForce(-v.normalized * (derivedDrag * speed * speed), ForceMode.Force);

            downforceN = def.downforceCoefficient * speed * speed;
            if (downforceN > 1f) rb.AddForce(-transform.up * downforceN, ForceMode.Force);

            // Gentle yaw damping so the car tracks straight instead of wandering.
            if (yawDamping > 0f && telemetry.groundedWheels >= 3)
                rb.AddTorque(-transform.up * (rb.angularVelocity.y * rb.mass * yawDamping), ForceMode.Force);
        }

        /// The core of the whole thing. Static corner load, plus longitudinal and
        /// lateral transfer, blended with what the suspension actually measured.
        private void UpdateLoads(float dt)
        {
            float mass = rb.mass;
            float totalN = mass * Mathf.Max(0f, vertG) * 9.81f + downforceN;

            int frontCount = 0, rearCount = 0;
            foreach (var w in wheels) { if (!w.collider) continue; if (w.front) frontCount++; else rearCount++; }
            frontCount = Mathf.Max(1, frontCount);
            rearCount = Mathf.Max(1, rearCount);

            float aeroFront = downforceN * def.aeroBalanceFront;
            float frontAxleN = (totalN - downforceN) * def.frontWeightBias + aeroFront;
            float rearAxleN = (totalN - downforceN) * (1f - def.frontWeightBias) + (downforceN - aeroFront);

            // Total load moved front<->rear and side<->side, in Newtons.
            float longTransfer = mass * longG * 9.81f * def.cgHeightM / wheelbase;
            float latTransfer = mass * latG * 9.81f * def.cgHeightM / trackWidth;

            float k = 1f - Mathf.Exp(-loadFilterRate * dt);

            foreach (var w in wheels)
            {
                if (!w.collider) continue;

                float load = w.front ? frontAxleN / frontCount : rearAxleN / rearCount;

                // Accelerating (+longG) unloads the front. Braking loads it.
                load += (w.front ? -1f : 1f) * longTransfer * 0.5f;

                // Turning right (+latG) loads the left. Roll share decides which end pays.
                float rollShare = w.front ? def.frontRollShare : 1f - def.frontRollShare;
                load += (w.left ? 1f : -1f) * latTransfer * rollShare;

                if (!w.grounded) load = 0f;
                load = Mathf.Max(0f, load);

                // Fold in the real suspension force so bumps and landings still count.
                if (w.grounded && measuredLoadBlend > 0f && w.collider.GetGroundHit(out var hit))
                    load = Mathf.Lerp(load, Mathf.Max(0f, hit.force), measuredLoadBlend);

                w.loadN = Mathf.Lerp(w.loadN, load, k);
                w.normalisedLoad = Mathf.Clamp(w.loadN / Mathf.Max(1f, w.staticLoadN), 0f, 3f);

                // Sub-linear grip: the loaded corner gains less than the light corner loses.
                float coefficient = def.loadSensitivity.Evaluate(w.normalisedLoad);
                float authority = Mathf.Lerp(1f, w.normalisedLoad, transferAuthority);
                w.gripScale = Mathf.Clamp(coefficient * authority, 0.12f, 2.5f);
            }
        }

        /// Per wheel, per step. This is the write that used to be edge-driven.
        private void UpdateTyres()
        {
            foreach (var w in wheels)
            {
                if (!w.collider) continue;

                float fwd = stats.forwardGrip * surfaceGrip * w.gripScale;
                float side = stats.sidewaysGrip * surfaceGrip * w.gripScale;

                // Handbrake kills rear lateral grip - that is the pivot.
                if (input.handbrake && w.handbraked) side *= 0.45f;

                var f = w.collider.forwardFriction;
                f.stiffness = Mathf.Max(0.15f, fwd);
                w.collider.forwardFriction = f;

                var s = w.collider.sidewaysFriction;
                s.stiffness = Mathf.Max(0.15f, side);
                w.collider.sidewaysFriction = s;
            }
        }

        // ---- driving -------------------------------------------------------

        private void UpdateSteering(float dt)
        {
            float speedFactor = Mathf.Lerp(1f, def.highSpeedSteerScale,
                Mathf.InverseLerp(0f, def.topSpeedKph, SpeedKph));
            float target = input.steer * stats.maxSteerAngle * speedFactor;

            // A light front end steers lazily. Free, and it reads as understeer on power.
            float frontLoad = AverageNormalisedLoad(true);
            float rate = steerSmoothing * stats.steerResponse * Mathf.Lerp(0.75f, 1.15f, Mathf.Clamp01(frontLoad));
            if (Mathf.Abs(target) < Mathf.Abs(currentSteer)) rate *= steerReturnMultiplier;

            currentSteer = Mathf.Lerp(currentSteer, target, Mathf.Clamp01(dt * rate));

            foreach (var w in wheels)
                if (w.steers && w.collider) w.collider.steerAngle = currentSteer;
        }

        /// Revs, clutch and gear selection.
        ///
        /// The clutch locks on ROAD SPEED, not on gear-multiplied rpm. Deriving it from
        /// rpm means a tall gear at low speed slips the clutch, which cuts torque, which
        /// lowers the speed, which slips it further - the car crawls to a stop on any
        /// incline and the free-revving engine hides it from the auto gearbox.
        private void UpdateEngine(float dt)
        {
            bool manual = input.transmission == TransmissionMode.Manual;
            float ratio = CurrentRatio();
            float coupledRpm = Mathf.Abs(AverageDrivenWheelRpm()) * Mathf.Abs(ratio) * def.finalDrive;

            float rolling = Mathf.InverseLerp(1f, Mathf.Max(2f, clutchLockKph), SpeedKph);
            float launch = Mathf.Clamp01(coupledRpm / Mathf.Max(1f, def.idleRpm * 1.15f));
            float autoClutch = gear == Gearbox.Neutral ? 0f : Mathf.Max(rolling, launch);

            float driverClutch = manual ? 1f - Mathf.Clamp01(input.clutch) : 1f;
            float wanted = Mathf.Min(autoClutch, driverClutch);
            if (shiftTimer > 0f) wanted = 0f;
            clutchLock = Mathf.MoveTowards(clutchLock, wanted, dt * 8f);

            if (!engineRunning) { rpm = 0f; shiftTimer = Mathf.Max(0f, shiftTimer - dt); return; }

            // Off the clutch the engine free-revs against its own inertia.
            float freeTarget = Mathf.Lerp(def.idleRpm, def.redlineRpm * 0.98f, input.throttle);
            float target = Mathf.Lerp(freeTarget, coupledRpm, clutchLock);
            float response = def.engineResponse * (clutchLock > 0.5f ? 1f : 1.8f);
            rpm = Mathf.Lerp(rpm, Mathf.Clamp(target, def.idleRpm, def.redlineRpm * 1.03f),
                             Mathf.Clamp01(dt * response));

            limiterTimer = Mathf.Max(0f, limiterTimer - dt);
            if (rpm >= def.redlineRpm && limiterTimer <= 0f) limiterTimer = limiterCutSeconds;

            if (shiftTimer > 0f) { shiftTimer -= dt; return; }
            if (!controlEnabled) return;

            if (manual) ManualShift();
            else AutoShift();
        }

        private void ManualShift()
        {
            int top = def.gearRatios.Length;

            if (input.shiftUp)
            {
                if (gear >= top)
                {
                    GameLog.Refused(LogCat.Vehicle, "upshift", $"already in top gear ({top})", this);
                    return;
                }
                EngageGear(gear + 1, blip: false);
            }
            else if (input.shiftDown)
            {
                if (gear == Gearbox.Reverse)
                {
                    GameLog.Refused(LogCat.Vehicle, "downshift", "already in reverse", this);
                    return;
                }
                if (gear == Gearbox.Neutral && SpeedKph > reverseEngageKph)
                {
                    GameLog.Refused(LogCat.Vehicle, "select reverse",
                                    $"still doing {SpeedKph:0} kph (limit {reverseEngageKph:0})", this);
                    return;
                }
                EngageGear(gear - 1, blip: true);
            }
        }

        private void AutoShift()
        {
            int top = def.gearRatios.Length;

            if (input.brake > 0.5f && SpeedKph < 2f && gear >= Gearbox.Neutral) { EngageGear(Gearbox.Reverse, false); return; }
            if (gear <= Gearbox.Neutral && input.throttle > 0.3f && SpeedKph < 2f) { EngageGear(1, false); return; }
            if (gear <= Gearbox.Neutral) return;

            if (rpm > def.redlineRpm * autoUpshiftPoint && gear < top) { EngageGear(gear + 1, false); return; }

            // Kickdown: asking for full power below the torque band means the gear is
            // too tall. Without this the box holds 5th up a climb and lugs to a stop.
            if (input.throttle > 0.75f && NormalisedRpm < autoKickdownPoint && gear > 1) { EngageGear(gear - 1, true); return; }

            if (rpm < def.idleRpm * 1.45f && gear > 1) EngageGear(gear - 1, true);
        }

        /// One place that changes gear, so the shift cut and the rev blip are consistent.
        private void EngageGear(int next, bool blip)
        {
            gear = Mathf.Clamp(next, Gearbox.Reverse, def.gearRatios.Length);
            shiftTimer = Mathf.Max(0.05f, stats.shiftTime);
            clutchLock = 0f;
            if (blip) rpm = Mathf.Min(def.redlineRpm, rpm + (def.redlineRpm - def.idleRpm) * 0.18f);
        }

        private void UpdateDrive()
        {
            if (!engineRunning || !controlEnabled || gear == Gearbox.Neutral)
            {
                SetMotorTorque(0f, 0f);
                tractionScale = 1f;
                engineBrakeNow = 0f;
                return;
            }

            float ratio = CurrentRatio();
            float curve = def.torqueCurve.Evaluate(Mathf.Clamp01(NormalisedRpm));
            float torque = stats.peakTorqueNm * curve * ratio * def.finalDrive * input.throttle * clutchLock;

            if (limiterTimer > 0f || shiftTimer > 0f) torque = 0f;

            // Engine braking as brake torque, not negative drive: a negative motor
            // torque will happily push a stationary car backwards.
            engineBrakeNow = 0f;
            if (input.throttle < 0.05f && clutchLock > 0.3f && SpeedKph > 2f)
                engineBrakeNow = def.engineBrakingNm * Mathf.Abs(ratio) * def.finalDrive * clutchLock * 0.35f;

            // TC is bypassed at crawling speed and floored everywhere else, so it can
            // trim wheelspin without ever strangling the engine on a loose climb.
            if (tractionControl && Mathf.Abs(torque) > 0f && SpeedKph > tractionMinSpeedKph)
            {
                float slip = PeakDrivenForwardSlip();
                float target = slip > maxForwardSlip ? Mathf.Clamp01(maxForwardSlip / slip) : 1f;
                target = Mathf.Max(target, minTractionScale);
                float rate = target < tractionScale ? tractionCutRate : tractionRestoreRate;
                tractionScale = Mathf.MoveTowards(tractionScale, target, Time.fixedDeltaTime * rate);
                torque *= tractionScale;
            }
            else
            {
                tractionScale = Mathf.MoveTowards(tractionScale, 1f, Time.fixedDeltaTime * tractionRestoreRate);
            }

            // AWD split so the balance knob in the definition actually does something.
            if (def.drivetrain == Drivetrain.AWD)
            {
                int fn = DrivenCount(true), rn = DrivenCount(false);
                float f = fn > 0 ? torque * def.awdFrontTorqueSplit / fn : 0f;
                float r = rn > 0 ? torque * (1f - def.awdFrontTorqueSplit) / rn : 0f;
                SetMotorTorque(f, r);
            }
            else
            {
                float per = torque / DrivenWheelCount();
                SetMotorTorque(per, per);
            }
        }

        /// Brake bias matters now: with the front loaded under braking, a rear-biased
        /// setup will lock the light end and spin you. That is intended.
        private void UpdateBrakes()
        {
            float total = input.brake * stats.brakeTorque;

            foreach (var w in wheels)
            {
                if (!w.collider) continue;

                float bias = w.front ? def.frontBrakeBias : 1f - def.frontBrakeBias;
                float t = total * bias * 2f;

                if (w.driven) t += engineBrakeNow;
                if (input.handbrake && w.handbraked) t = Mathf.Max(t, stats.handbrakeTorque);

                // Hold the car still rather than creeping when parked.
                if (!engineRunning && SpeedKph < 1f) t = Mathf.Max(t, 400f);

                w.collider.brakeTorque = t;
            }
        }

        /// Slides the CoM a few centimetres so Unity's own suspension dives and squats
        /// with the model. Small on purpose - large values make the car feel unstable.
        private void UpdateCentreOfMass(float dt)
        {
            if (!dynamicCentreOfMass) { rb.centerOfMass = baseCentreOfMass; return; }

            Vector3 offset = new Vector3(
                Mathf.Clamp(latG, -1f, 1f) * comShiftMetres * 0.6f,
                0f,
                Mathf.Clamp(longG, -1f, 1f) * comShiftMetres);

            rb.centerOfMass = Vector3.Lerp(rb.centerOfMass, baseCentreOfMass + offset, Mathf.Clamp01(dt * 10f));
        }

        private void AntiRoll()
        {
            ApplyAntiRoll(true, antiRollForce * def.frontRollShare * 2f);
            ApplyAntiRoll(false, antiRollForce * (1f - def.frontRollShare) * 2f);
        }

        private void ApplyAntiRoll(bool front, float strength)
        {
            Wheel l = null, r = null;
            foreach (var w in wheels)
            {
                if (!w.collider || w.front != front) continue;
                if (w.left) l = w; else r = w;
            }
            if (l == null || r == null) return;

            float force = (l.extension01 - r.extension01) * strength;
            if (l.grounded) rb.AddForceAtPosition(l.collider.transform.up * -force, l.collider.transform.position);
            if (r.grounded) rb.AddForceAtPosition(r.collider.transform.up * force, r.collider.transform.position);
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

        private void ApplyLights()
        {
            foreach (var l in headlights) if (l) { l.enabled = input.lights; l.range = Mathf.Max(30f, 30f + stats.lightRange); }
            foreach (var l in rallyLights) if (l) { l.enabled = input.lights && stats.lightRange > 0f; l.range = 40f + stats.lightRange; }

            if (!lightsLogPrimed || input.lights != lastLoggedLights)
            {
                lightsLogPrimed = true;
                lastLoggedLights = input.lights;
                GameLog.Action(LogCat.Vehicle, input.lights ? "Lights ON" : "Lights OFF",
                               $"'{name}': {headlights.Length} headlight(s), {rallyLights.Length} rally light(s)", this);
            }
        }

        // ---- telemetry -----------------------------------------------------

        private void BuildTelemetry()
        {
            float fc = 0f, rc = 0f, lc = 0f, rrc = 0f, slip = 0f, spin = 0f;
            int fn = 0, rn = 0, ln = 0, rrn = 0, grounded = 0;

            foreach (var w in wheels)
            {
                if (!w.collider) continue;
                if (w.grounded) grounded++;

                if (w.front) { fc += w.Compression01; fn++; } else { rc += w.Compression01; rn++; }
                if (w.left) { lc += w.Compression01; ln++; } else { rrc += w.Compression01; rrn++; }

                slip = Mathf.Max(slip, Mathf.Abs(w.sidewaysSlip) + Mathf.Abs(w.forwardSlip) * 0.5f);
                if (w.driven) spin = Mathf.Max(spin, Mathf.Abs(w.forwardSlip));
            }

            telemetry = new CarTelemetry
            {
                longitudinalG = longG,
                lateralG = latG,
                verticalG = vertG,
                pitchBias = Mathf.Clamp(-longG, -1.5f, 1.5f),
                rollBias = Mathf.Clamp(latG, -1.5f, 1.5f),
                frontCompression = fn > 0 ? fc / fn : 0f,
                rearCompression = rn > 0 ? rc / rn : 0f,
                leftCompression = ln > 0 ? lc / ln : 0f,
                rightCompression = rrn > 0 ? rrc / rrn : 0f,
                averageCompression = (fc + rc) / Mathf.Max(1, fn + rn),
                slip01 = Mathf.Clamp01(slip / 1.2f),
                wheelspin01 = Mathf.Clamp01(spin / 1.2f),
                speedKph = SpeedKph,
                normalisedRpm = NormalisedRpm,
                surfaceGrip = surfaceGrip,
                groundedWheels = grounded
            };
        }

        // ---- debug ---------------------------------------------------------

        private void ReportGearChange()
        {
            if (!logGearChanges || gear == lastLoggedGear) return;
            string from = lastLoggedGear == int.MinValue ? "-" : Gearbox.Label(lastLoggedGear);
            lastLoggedGear = gear;

            GameLog.Action(LogCat.Vehicle, "Gear change",
                           $"'{name}' {from} -> {Gearbox.Label(gear)} at {SpeedKph:0} kph, {rpm:0} rpm " +
                           $"({input.transmission})", this);
        }

        private void ReportModeChange()
        {
            if (modeLogPrimed && input.transmission == lastLoggedMode) return;
            modeLogPrimed = true;
            lastLoggedMode = input.transmission;

            GameLog.Action(LogCat.Vehicle, "Transmission mode",
                           $"'{name}' now {input.transmission}", this);
        }

        // ---- helpers -------------------------------------------------------

        private float CurrentRatio()
        {
            if (gear == Gearbox.Reverse) return -def.reverseRatio;
            if (gear == Gearbox.Neutral) return 0f;
            return def.gearRatios[Mathf.Clamp(gear - 1, 0, def.gearRatios.Length - 1)];
        }

        private void SetMotorTorque(float frontPerWheel, float rearPerWheel)
        {
            foreach (var w in wheels)
                if (w.collider && w.driven) w.collider.motorTorque = w.front ? frontPerWheel : rearPerWheel;
        }

        private int DrivenWheelCount()
        {
            int n = 0;
            foreach (var w in wheels) if (w.driven) n++;
            return Mathf.Max(1, n);
        }

        private int DrivenCount(bool front)
        {
            int n = 0;
            foreach (var w in wheels) if (w.driven && w.front == front) n++;
            return n;
        }

        private float AverageDrivenWheelRpm()
        {
            float sum = 0f; int n = 0;
            foreach (var w in wheels) if (w.collider && w.driven) { sum += w.collider.rpm; n++; }
            return n == 0 ? 0f : sum / n;
        }

        private float AverageNormalisedLoad(bool front)
        {
            float sum = 0f; int n = 0;
            foreach (var w in wheels) { if (!w.collider || w.front != front) continue; sum += w.normalisedLoad; n++; }
            return n == 0 ? 1f : sum / n;
        }

        /// Airborne wheels are skipped - they read as infinite slip and would kill
        /// drive on every jump.
        private float PeakDrivenForwardSlip()
        {
            float peak = 0f;
            foreach (var w in wheels)
            {
                if (!w.collider || !w.driven || !w.grounded) continue;
                peak = Mathf.Max(peak, Mathf.Abs(w.forwardSlip));
            }
            return peak;
        }
    }
}