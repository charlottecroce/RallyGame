using UnityEngine;
using RallyGame.Core;
using RallyGame.World.Roads;

namespace RallyGame.Vehicles.Controllers
{
    /// Asks each wheel what it is standing on and hands the answer to the controller
    /// as a single grip multiplier.
    ///
    /// The WheelCollider already has the ground hit from the physics step, so this
    /// costs one dictionary lookup per wheel and no raycasts of its own.
    ///
    /// The multiplier is averaged across grounded wheels rather than taken from a
    /// majority vote, so a car straddling the tarmac edge gets a blend instead of a
    /// grip step you can feel through the wheel. It only reaches the controller when
    /// it actually changes by a meaningful amount — friction curves are not rewritten
    /// fifty times a second.
    [RequireComponent(typeof(CarController))]
    public class CarSurfaceSampler : MonoBehaviour
    {
        [SerializeField] private RoadSurfaceTable surfaceTable;
        [SerializeField] private WeatherVariable weather;

        [Header("Response")]
        [Tooltip("How fast grip moves toward the new surface. High = instant and steppy, " +
                 "low = a slide that carries onto the verge. 6 feels about right.")]
        [SerializeField] private float blendRate = 6f;
        [Tooltip("Change needed before the controller is told. Below this it is noise.")]
        [SerializeField] private float updateThreshold = 0.01f;
        [Tooltip("Physics steps between samples. 2 = 25 Hz, plenty for a surface change.")]
        [Range(1, 10)] [SerializeField] private int stepsBetweenSamples = 2;

        [Header("Debug")]
        [Tooltip("Log when the dominant surface under the car changes. Discrete, so it is safe.")]
        [SerializeField] private bool logSurfaceChanges = true;

        private CarController controller;
        private WheelCollider[] wheels;

        private float current = 1f;
        private float lastSent = 1f;
        private int stepCounter;
        private RoadSurface lastLoggedSurface;
        private bool surfaceLogPrimed;

        public float CurrentGrip => current;

        private void Awake()
        {
            controller = GetComponent<CarController>();
            wheels = GetComponentsInChildren<WheelCollider>(true);

            if (surfaceTable == null)
                GameLog.Warn(LogCat.Vehicle,
                    $"'{name}' has no RoadSurfaceTable — every surface will read as neutral grip " +
                    "and dirt will feel exactly like tarmac.", this);
        }

        private void FixedUpdate()
        {
            if (surfaceTable == null) return;
            if (++stepCounter < stepsBetweenSamples) return;
            stepCounter = 0;

            var w = weather ? weather.Value : WeatherType.Sunny;
            float sum = 0f;
            int grounded = 0;
            RoadSurface dominant = null;

            foreach (var wheel in wheels)
            {
                if (!wheel || !wheel.GetGroundHit(out var hit)) continue;

                var surface = RoadSurfaceTag.Resolve(hit.collider);   // null = not on a road
                sum += surfaceTable.GripMultiplier(surface, w);
                grounded++;
                if (dominant == null) dominant = surface;
            }

            // Airborne: hold the last value rather than snapping to off-road, so a jump
            // does not land you on a different friction curve than you took off with.
            float target = grounded > 0 ? sum / grounded : current;

            current = Mathf.MoveTowards(current, target, blendRate * Time.fixedDeltaTime * stepsBetweenSamples);

            if (Mathf.Abs(current - lastSent) >= updateThreshold)
            {
                lastSent = current;
                controller.SetSurfaceGrip(current);
            }

            ReportSurfaceChange(dominant, grounded);
        }

        /// Edge-detected: one line when you leave the tarmac, not one per step.
        private void ReportSurfaceChange(RoadSurface surface, int grounded)
        {
            if (!logSurfaceChanges || grounded == 0) return;
            if (surfaceLogPrimed && surface == lastLoggedSurface) return;

            surfaceLogPrimed = true;
            lastLoggedSurface = surface;

            string from = surface ? surface.displayName : (surfaceTable.OffRoad ? surfaceTable.OffRoad.displayName : "off-road");
            GameLog.Action(LogCat.Vehicle, "Surface change",
                           $"'{name}' now on {from}, grip x{current:0.00} ({grounded} wheel(s) down)", this);
        }
    }
}