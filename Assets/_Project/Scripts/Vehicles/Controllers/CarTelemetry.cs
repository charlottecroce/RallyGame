namespace RallyGame.Vehicles.Controllers
{
    /// One read-only snapshot of chassis state, rebuilt each physics step.
    /// Visuals, camera, audio and HUD read this instead of poking at wheels.
    public struct CarTelemetry
    {
        // Specific force in g, chassis local. +long = accelerating, +lat = turning right.
        public float longitudinalG;
        public float lateralG;
        public float verticalG;

        // Load split, -1..1. +pitch = nose loaded (braking). +roll = left side loaded (right turn).
        public float pitchBias;
        public float rollBias;

        // Suspension compression, 0 = hanging, 1 = bottomed.
        public float frontCompression;
        public float rearCompression;
        public float leftCompression;
        public float rightCompression;
        public float averageCompression;

        // 0..1 combined tyre slip across all wheels — drives shake, dust, tyre squeal.
        public float slip01;
        public float wheelspin01;      // driven-wheel forward slip only
        public float speedKph;
        public float normalisedRpm;
        public float surfaceGrip;
        public int groundedWheels;

        public bool Airborne => groundedWheels == 0;
    }
}