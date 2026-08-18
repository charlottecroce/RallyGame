using UnityEngine;

namespace RallyGame.Vehicles.Controllers
{
    /// Per-frame driver intent. Struct so input sources stay allocation-free.
    public struct VehicleInput
    {
        public float throttle;   // 0..1
        public float brake;      // 0..1
        public float steer;      // -1..1
        public bool handbrake;
        public bool shiftUp;
        public bool shiftDown;
        public bool lights;
    }

    /// The contract every drivable thing satisfies. Player, AI, replay and the
    /// race manager all talk to this, never to a concrete controller.
    public interface IVehicleController
    {
        Transform Root { get; }
        float SpeedKph { get; }
        float EngineRpm { get; }
        int Gear { get; }
        bool EngineRunning { get; }

        void SetInput(in VehicleInput input);
        void ApplyStats(in ResolvedCarStats stats);
        void SetControlEnabled(bool enabled);
        void SetEngineRunning(bool running);
        void Teleport(Vector3 position, Quaternion rotation);
    }
}
