using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Garage;
using RallyGame.Parts.Data;
using RallyGame.Parts.Runtime;
using RallyGame.Utilities;
using RallyGame.Vehicles.Data;

namespace RallyGame.Vehicles.Controllers
{
    /// Binds save-layer data (OwnedCar) to a spawned car object. Owns everything the
    /// controller must not know: parts, wear, impact damage, visual body damage.
    ///
    /// Wear accumulates every 50 metres, which is far too often to log — that path is
    /// throttled hard and only reports at odometer milestones. Crashes are discrete
    /// and get a full breakdown.
    ///
    /// Crash DETECTION lives in CarImpactSensor, not here. This class only receives
    /// an already-scored damage fraction and distributes it. That split exists because
    /// the old inline path scored crashes from collision.impulse, which counts PhysX
    /// depenetration work and so read a freshly spawned car sitting in the garage floor
    /// as a 200%+ impact.
    [RequireComponent(typeof(CarController))]
    public class CarAssembly : MonoBehaviour
    {
        [SerializeField] private GarageState garage;
        [SerializeField] private TireCompoundTable tireTable;
        [SerializeField] private WeatherVariable weather;

        [Header("Channels")]
        [SerializeField] private GameEvent onCarStateChanged;   // condition/tires changed
        [SerializeField] private GameEvent onCrash;

        [Header("Visual damage")]
        [Tooltip("Renderers that receive the _DamageAmount material property.")]
        [SerializeField] private Renderer[] bodyRenderers;
        [SerializeField] private string damageProperty = "_DamageAmount";

        [Header("Impact")]
        [Tooltip("A single-frame position jump larger than this is a teleport, not driving. " +
                 "It is excluded from the odometer and re-arms the impact sensor. " +
                 "Keep it well above one frame of travel at top speed.")]
        [SerializeField] private float teleportDistanceMetres = 15f;

        [Header("Debug")]
        [Tooltip("Log an odometer line every this many km driven. 0 disables mileage logging.")]
        [SerializeField] private float odometerLogEveryKm = 1f;

        private CarController controller;
        private CarImpactSensor impactSensor;
        private MaterialPropertyBlock mpb;
        private OwnedCar car;
        private Vector3 lastPosition;
        private float kmAccumulator;
        private float nextOdometerLog;

        public OwnedCar Car => car;
        public IVehicleController Vehicle => controller;

        private void Awake()
        {
            controller = GetComponent<CarController>();
            impactSensor = GetComponent<CarImpactSensor>();
            mpb = new MaterialPropertyBlock();

            if (impactSensor == null)
                GameLog.Warn(LogCat.Vehicle,
                    $"'{name}' has no CarImpactSensor — the car will take no crash damage at all. " +
                    "Add the component to the car prefab root.", this);
        }

        /// Entry point used by the spawner when the player changes cars.
        public void Bind(OwnedCar owned)
        {
            car = owned;
            controller.ApplyDefinition(car.Definition(garage.Database));
            lastPosition = transform.position;
            nextOdometerLog = car.odometerKm + odometerLogEveryKm;

            GameLog.Action(LogCat.Vehicle, "Car bound to save data",
                           $"'{name}' <- OwnedCar '{owned.instanceId}' ({owned.definitionId}) — " +
                           $"{owned.installedPartInstanceIds.Count} part(s), {owned.odometerKm:0} km, " +
                           $"tires {owned.tires.compound} at {1f - owned.tires.wear:P0}", this);

            // Unresolvable parts are a common save-migration failure; name them.
            foreach (var id in owned.installedPartInstanceIds)
            {
                var p = garage.GetOwnedPart(id);
                if (p == null)
                    GameLog.Warn(LogCat.Parts,
                        $"Car '{owned.instanceId}' lists part instance '{id}' which is not in the garage inventory.", this);
            }

            Refresh();
        }

        private void OnEnable() { if (garage && garage.OnGarageChanged) garage.OnGarageChanged.Register(Refresh); }
        private void OnDisable() { if (garage && garage.OnGarageChanged) garage.OnGarageChanged.Unregister(Refresh); }

        /// Recompute stats from current fitment/condition/tires/weather.
        public void Refresh()
        {
            if (car == null) return;
            var stats = CarStatsResolver.Resolve(car, garage, garage.Database, tireTable,
                weather ? weather.Value : WeatherType.Sunny);
            controller.ApplyStats(stats);
            controller.SetEngineRunning(controller.EngineRunning && stats.canStart);
            PushVisualDamage();
        }

        private void Update()
        {
            if (car == null) return;
            AccumulateDistance();
        }

        // ---- wear ----------------------------------------------------------

        private void AccumulateDistance()
        {
            float metres = Vector3.Distance(transform.position, lastPosition);
            lastPosition = transform.position;
            if (metres < 0.01f) return;

            // A teleport shows up here as a huge single-frame delta. Charging it to the
            // odometer would apply kilometres of wear in one frame, and the landing would
            // register as a crash, so swallow both.
            if (metres > teleportDistanceMetres)
            {
                GameLog.Verbose(LogCat.Vehicle,
                    $"Ignoring {metres:0} m single-frame jump on '{name}' (teleport/respawn) — " +
                    "no distance or wear accrued.", this);
                impactSensor?.SuppressForTeleport();
                return;
            }

            kmAccumulator += metres / 1000f;
            if (kmAccumulator < 0.05f) return;   // batch: wear applies every 50 m

            float km = kmAccumulator;
            kmAccumulator = 0f;

            car.odometerKm += km;
            car.tires.AccumulateWear(km, tireTable);

            foreach (var id in car.installedPartInstanceIds)
                garage.GetOwnedPart(id)?.AccumulateWear(km, garage.Database);

            // This method fires every 50 m of driving, so it reports only at
            // odometer milestones - roughly one line per kilometre, not per frame.
            if (odometerLogEveryKm > 0f && car.odometerKm >= nextOdometerLog)
            {
                nextOdometerLog = car.odometerKm + odometerLogEveryKm;
                GameLog.Verbose(LogCat.Vehicle,
                    $"Odometer {car.odometerKm:0.0} km — tires {car.tires.compound} at {1f - car.tires.wear:P0} remaining", this);
            }

            Refresh();
        }

        // ---- impact damage -------------------------------------------------

        /// Applies a crash the sensor has already validated and scored. This method
        /// deliberately does no filtering and no severity maths — it clamps, distributes
        /// and reports. The clamp is the hard guarantee that a single impact can never
        /// remove more than 100% condition, which the old impulse path allowed.
        public void ApplyImpact(float conditionLost, string context = null)
        {
            if (car == null) return;

            float total = Mathf.Clamp01(conditionLost);
            if (total <= 0f) return;

            GameLog.Action(LogCat.Vehicle, "CRASH",
                           $"'{name}' at {controller.SpeedKph:0} kph — {total:P1} total condition lost" +
                           (string.IsNullOrEmpty(context) ? string.Empty : $" [{context}]"), this);

            DistributeDamage(total);
            onCrash?.Raise();
            onCarStateChanged?.Raise();
            Refresh();
        }

        /// Kept so any UnityEvent still wired to the old impulse signature keeps compiling.
        /// Nothing in the project should call this — the sensor calls ApplyImpact directly.
        [System.Obsolete("Impulse-based damage was removed. Use ApplyImpact(fraction) via CarImpactSensor.")]
        public void HandleImpact(float impulse)
        {
            GameLog.Warn(LogCat.Vehicle,
                $"HandleImpact({impulse:0}) called on '{name}'. This path is retired — " +
                "re-wire the caller to CarImpactSensor. Ignoring.", this);
        }

        /// Bodywork absorbs first (GDD: never degrades over time, first to break on impact),
        /// spillover is shared by the other fitted parts using their impactWeight.
        private void DistributeDamage(float amount)
        {
            var body = car.PartInSlot(PartSlot.Bodywork, garage, garage.Database);
            float spill = amount;

            if (body != null)
            {
                float absorbed = Mathf.Min(body.condition, amount * 0.6f);
                float before = body.condition;
                body.ApplyDamage(absorbed, DamageType.Impact);
                spill = amount - absorbed;

                GameLog.Verbose(LogCat.Parts,
                    $"  bodywork absorbed {absorbed:P1}: {before:P0} -> {body.condition:P0}", this);
            }
            else
            {
                GameLog.Verbose(LogCat.Parts, "  no bodywork fitted — full impact passes to other parts", this);
            }

            if (spill <= 0f) return;

            var others = new List<OwnedPart>();
            float weightSum = 0f;
            foreach (var id in car.installedPartInstanceIds)
            {
                var p = garage.GetOwnedPart(id);
                var d = p?.Definition(garage.Database);
                if (p == null || d == null || d.slot == PartSlot.Bodywork) continue;
                others.Add(p);
                weightSum += d.impactWeight;
            }

            if (weightSum <= 0f)
            {
                GameLog.Verbose(LogCat.Parts, $"  {spill:P1} spillover discarded — no parts carry impact weight", this);
                return;
            }

            GameLog.Verbose(LogCat.Parts,
                $"  {spill:P1} spillover shared across {others.Count} part(s)", this);

            foreach (var p in others)
            {
                var d = p.Definition(garage.Database);
                float share = spill * (d.impactWeight / weightSum);
                float before = p.condition;
                p.ApplyDamage(share, DamageType.Impact);

                // Only call out parts that actually broke - the rest is noise.
                if (before > 0.01f && p.condition <= 0.01f)
                    GameLog.Warn(LogCat.Parts,
                        $"Part DESTROYED by impact: '{d.displayName}' ({d.slot}) on car '{car.instanceId}'", this);
            }
        }

        // ---- visual damage -------------------------------------------------

        /// Simple scratch/dirt overlay driven by bodywork condition only (GDD).
        private void PushVisualDamage()
        {
            var body = car.PartInSlot(PartSlot.Bodywork, garage, garage.Database);
            float damage = body == null ? 0f : 1f - body.condition;

            foreach (var r in bodyRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetFloat(damageProperty, damage);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}