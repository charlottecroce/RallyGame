using UnityEngine;
using RallyGame.Core;
using RallyGame.Input;
using RallyGame.Races.Runtime;
using RallyGame.World.Roads;

namespace RallyGame.Vehicles.Controllers
{
    /// Puts the car back somewhere sane: on a road, the right way up, pointing the
    /// way it was going.
    ///
    /// Three candidate destinations, tried in order, because each one can legitimately
    /// be unavailable:
    ///   1. NEAREST ROAD  — the good answer. Needs a baked RoadNetwork.
    ///   2. LAST SAFE POSE — recorded while driving normally. Covers the case where no
    ///      road exists yet, and is better than a road 800 m away.
    ///   3. GARAGE PAD    — the guaranteed-valid fallback.
    ///
    /// Whatever is chosen is then dropped onto the ground by raycast, so the car never
    /// spawns buried or floating regardless of which branch produced the point.
    public class CarResetService : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CarSpawner spawner;
        [SerializeField] private RoadNetwork roadNetwork;
        [SerializeField] private InputReader input;
        [SerializeField] private Transform garageSpawnPoint;

        [Header("State")]
        [SerializeField] private BoolVariable isDriving;
        [SerializeField] private BoolVariable inputLocked;
        [SerializeField] private RaceState raceState;

        [Header("Channels")]
        [Tooltip("Optional. Listened to, so anything can request a reset — the HUD button, " +
                 "CarUnstick, a debug menu.")]
        [SerializeField] private GameEvent onResetRequested;
        [Tooltip("Optional. Raised after a successful reset.")]
        [SerializeField] private GameEvent onCarReset;

        [Header("Placement")]
        [Tooltip("Give up on the road network beyond this. Past it, the last safe pose is better.")]
        [SerializeField] private float maxRoadSearchDistance = 300f;
        [Tooltip("Height above the ground the car is dropped from. Small, so it settles instantly.")]
        [SerializeField] private float dropHeight = 0.6f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Safe pose recording")]
        [Tooltip("Seconds between samples of 'the car was fine here'.")]
        [SerializeField] private float sampleInterval = 0.75f;
        [Tooltip("Minimum speed before a pose counts as safe. Stops us recording the moment " +
                 "the car settled against a tree.")]
        [SerializeField] private float minSafeSpeedKph = 12f;
        [Tooltip("Maximum tilt from upright for a pose to count as safe.")]
        [Range(0f, 60f)] [SerializeField] private float maxSafeTiltDegrees = 25f;

        [Header("Rules")]
        [Tooltip("Refuse mid-stage. Off by default — being stuck is worse than the exploit.")]
        [SerializeField] private bool blockDuringRace = false;
        [Tooltip("Minimum gap between resets, so a held key does not strobe the car.")]
        [SerializeField] private float cooldownSeconds = 1.5f;

        private Vector3 safePosition;
        private Quaternion safeRotation;
        private bool hasSafePose;
        private float nextSample;
        private float lastResetTime = -999f;

        public bool CanReset => spawner && spawner.Current != null && Time.time - lastResetTime >= cooldownSeconds;

        private void OnEnable() { if (onResetRequested) onResetRequested.Register(RequestReset); }
        private void OnDisable() { if (onResetRequested) onResetRequested.Unregister(RequestReset); }

        private void Update()
        {
            RecordSafePose();

            if (input != null && input.ResetPressed)
            {
                if (inputLocked && inputLocked.Value)
                    GameLog.Refused(LogCat.Vehicle, "reset car", "input is locked (UI open?)", this);
                else
                    RequestReset();
            }
        }

        // ---- safe pose -----------------------------------------------------

        /// Sampled, not continuous. A pose only qualifies while the car is actually
        /// driving upright at speed, which is exactly the situation you want to be
        /// returned to.
        private void RecordSafePose()
        {
            if (Time.time < nextSample) return;
            nextSample = Time.time + Mathf.Max(0.1f, sampleInterval);

            var car = spawner ? spawner.Current : null;
            if (car == null) return;
            if (isDriving != null && !isDriving.Value) return;

            var t = car.transform;
            if (car.Vehicle.SpeedKph < minSafeSpeedKph) return;
            if (Vector3.Angle(t.up, Vector3.up) > maxSafeTiltDegrees) return;

            safePosition = t.position;
            safeRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized, Vector3.up);
            hasSafePose = true;
        }

        // ---- reset ---------------------------------------------------------

        /// Wire a UI Button's OnClick straight to this.
        public void RequestReset()
        {
            var car = spawner ? spawner.Current : null;

            if (car == null)
            {
                GameLog.Refused(LogCat.Vehicle, "reset car", "no car spawned in the world", this);
                return;
            }
            if (Time.time - lastResetTime < cooldownSeconds)
            {
                GameLog.Refused(LogCat.Vehicle, "reset car",
                                $"cooldown, {cooldownSeconds - (Time.time - lastResetTime):0.0}s remaining", this);
                return;
            }
            if (blockDuringRace && raceState != null && raceState.inRace && raceState.phase == RacePhase.Running)
            {
                GameLog.Refused(LogCat.Vehicle, "reset car", "a stage is running", this);
                return;
            }

            if (!ResolveDestination(car.transform, out Vector3 position, out Quaternion rotation, out string source))
            {
                GameLog.Warn(LogCat.Vehicle,
                    "Reset requested but there is nowhere to put the car — no roads baked, no safe pose " +
                    "recorded, and no garage spawn point assigned.", this);
                return;
            }

            lastResetTime = Time.time;

            GameLog.Action(LogCat.Vehicle, "CAR RESET",
                           $"'{car.name}' {car.transform.position:0.0} -> {position:0.0} via {source}", car);

            car.Vehicle.Teleport(position, rotation);
            car.GetComponent<CarImpactSensor>()?.SuppressForTeleport();

            onCarReset?.Raise();
        }

        /// First branch that produces a point wins. Each returns a heading too, so the
        /// car faces down the road rather than across it.
        private bool ResolveDestination(Transform car, out Vector3 position, out Quaternion rotation, out string source)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            source = null;

            Vector3 heading = Vector3.ProjectOnPlane(car.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.001f) heading = Vector3.forward;
            heading.Normalize();

            // 1. Nearest road.
            if (roadNetwork != null &&
                roadNetwork.TryFindNearest(car.position, out var road, maxRoadSearchDistance))
            {
                // Run the way the driver was already going, not the way the spline was drawn.
                Vector3 forward = Vector3.Dot(road.forward, heading) < 0f ? -road.forward : road.forward;

                position = Ground(road.position + Vector3.up * 2f);
                rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up).normalized, Vector3.up);
                source = $"road '{road.road.name}' ({road.distance:0} m away)";
                return true;
            }

            // 2. Last safe pose.
            if (hasSafePose)
            {
                position = Ground(safePosition + Vector3.up * 2f);
                rotation = safeRotation;
                source = "last safe pose";
                return true;
            }

            // 3. Garage pad.
            if (garageSpawnPoint != null)
            {
                position = Ground(garageSpawnPoint.position + Vector3.up * 2f);
                rotation = garageSpawnPoint.rotation;
                source = "garage spawn point";
                return true;
            }

            return false;
        }

        /// Drops a candidate point onto whatever is beneath it. Without this a road
        /// point sampled at build time and a terrain edited since would disagree, and
        /// the car would spawn inside the hill.
        private Vector3 Ground(Vector3 from)
        {
            if (UnityEngine.Physics.Raycast(from, Vector3.down, out var hit, 50f, groundMask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * dropHeight;

            GameLog.Verbose(LogCat.Vehicle, $"Reset point {from:0.0} found no ground below it — placing as-is.", this);
            return from;
        }
    }
}