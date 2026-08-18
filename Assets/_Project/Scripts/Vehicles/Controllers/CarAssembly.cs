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

        [Header("Impact tuning")]
        [Tooltip("Collision impulse below this is ignored (kerbs, scrapes).")]
        [SerializeField] private float impactThreshold = 2500f;
        [Tooltip("Condition removed per unit impulse above the threshold.")]
        [SerializeField] private float damagePerImpulse = 0.00004f;

        private CarController controller;
        private MaterialPropertyBlock mpb;
        private OwnedCar car;
        private Vector3 lastPosition;
        private float kmAccumulator;

        public OwnedCar Car => car;
        public IVehicleController Vehicle => controller;

        private void Awake()
        {
            controller = GetComponent<CarController>();
            mpb = new MaterialPropertyBlock();
        }

        /// Entry point used by the spawner when the player changes cars.
        public void Bind(OwnedCar owned)
        {
            car = owned;
            controller.ApplyDefinition(car.Definition(garage.Database));
            lastPosition = transform.position;
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

            kmAccumulator += metres / 1000f;
            if (kmAccumulator < 0.05f) return;   // batch: wear applies every 50 m

            float km = kmAccumulator;
            kmAccumulator = 0f;

            car.odometerKm += km;
            car.tires.AccumulateWear(km, tireTable);

            foreach (var id in car.installedPartInstanceIds)
                garage.GetOwnedPart(id)?.AccumulateWear(km, garage.Database);

            Refresh();
        }

        // ---- impact damage -------------------------------------------------

        private void OnCollisionEnter(Collision collision) => HandleImpact(collision.impulse.magnitude);

        public void HandleImpact(float impulse)
        {
            if (car == null || impulse < impactThreshold) return;

            float total = (impulse - impactThreshold) * damagePerImpulse;
            DistributeDamage(total);
            onCrash?.Raise();
            onCarStateChanged?.Raise();
            Refresh();
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
                body.ApplyDamage(absorbed, DamageType.Impact);
                spill = amount - absorbed;
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

            if (weightSum <= 0f) return;
            foreach (var p in others)
            {
                var d = p.Definition(garage.Database);
                p.ApplyDamage(spill * (d.impactWeight / weightSum), DamageType.Impact);
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
