using UnityEngine;

namespace RallyGame.Vehicles.Controllers
{
    public enum TransmissionMode { Automatic, Manual }

    /// Per-frame driver intent. Struct so input sources stay allocation-free.
    public struct VehicleInput
    {
        public float throttle;   // 0..1, ramped by the input source (not a raw key state)
        public float brake;      // 0..1
        public float steer;      // -1..1
        public float clutch;     // 0..1, 1 = fully disengaged. Manual only; ignored in Automatic.
        public bool handbrake;
        public bool shiftUp;
        public bool shiftDown;
        public bool lights;
        public TransmissionMode transmission;
    }

    public interface IVehicleController
    {
        Transform Root { get; }
        float SpeedKph { get; }
        float EngineRpm { get; }
        float NormalisedRpm { get; }
        int Gear { get; }
        bool EngineRunning { get; }

        void SetInput(in VehicleInput input);
        void ApplyStats(in ResolvedCarStats stats);
        void SetControlEnabled(bool enabled);
        void SetEngineRunning(bool running);
        void Teleport(Vector3 position, Quaternion rotation);
    }

    /// Gear index convention lives here so HUD, logs and controller cannot disagree.
    /// -1 = reverse, 0 = neutral, 1..N = forward.
    public static class Gearbox
    {
        public const int Reverse = -1;
        public const int Neutral = 0;

        public static string Label(int gear)
            => gear == Reverse ? "R" : gear == Neutral ? "N" : gear.ToString();
    }
}