using UnityEngine;

namespace RallyGame.Vehicles.Controllers
{
    /// Shared layered RPM rig (GDD): one sample set for every car, pitched by RPM.
    /// Per-car character comes from the pitch range on the CarDefinition side.
    [RequireComponent(typeof(CarController))]
    public class CarAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource idleLayer;
        [SerializeField] private AudioSource onThrottleLayer;
        [SerializeField] private AudioSource offThrottleLayer;

        [Header("Pitch mapping")]
        [SerializeField] private float minPitch = 0.6f;
        [SerializeField] private float maxPitch = 2.2f;
        [SerializeField] private float pitchOffset = 0f;   // per-car character
        [SerializeField] private float blendSpeed = 8f;

        private CarController car;
        private float load;

        private void Awake() => car = GetComponent<CarController>();

        private void Update()
        {
            if (!car.EngineRunning) { SetVolumes(0f, 0f, 0f); return; }

            float t = Mathf.Clamp01(car.NormalisedRpm);
            float pitch = Mathf.Lerp(minPitch, maxPitch, t) + pitchOffset;

            // Approximate engine load from RPM rise; avoids plumbing throttle through.
            load = Mathf.Lerp(load, car.Gear > 0 && car.SpeedKph > 1f ? 1f : 0.2f, Time.deltaTime * blendSpeed);

            ApplyPitch(idleLayer, pitch);
            ApplyPitch(onThrottleLayer, pitch);
            ApplyPitch(offThrottleLayer, pitch);

            SetVolumes(Mathf.Clamp01(1f - t * 2f), load * t, (1f - load) * t);
        }

        private void ApplyPitch(AudioSource s, float p) { if (s) s.pitch = p; }

        private void SetVolumes(float idle, float on, float off)
        {
            if (idleLayer) idleLayer.volume = idle;
            if (onThrottleLayer) onThrottleLayer.volume = on;
            if (offThrottleLayer) offThrottleLayer.volume = off;
        }
    }
}
