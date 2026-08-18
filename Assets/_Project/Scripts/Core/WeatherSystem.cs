using UnityEngine;

namespace RallyGame.Core
{
    public enum WeatherType { Sunny, Cloudy, Rainy }

    /// Rolls weather at midnight. Grip multiplier is consumed by the tire model,
    /// so gameplay never reads this component directly.
    public class WeatherSystem : MonoBehaviour
    {
        [System.Serializable]
        public class WeatherProfile
        {
            public WeatherType type;
            [Range(0f, 1f)] public float weight = 1f;
            public Material skybox;
            public Color fogColor = Color.gray;
            public float fogDensity = 0.01f;
            [Tooltip("Multiplier applied to tire grip. 1 = dry.")]
            public float gripMultiplier = 1f;
            public GameObject rainVfx;
        }

        [SerializeField] private WeatherVariable current;
        [SerializeField] private GameEvent onDayRolled;      // listens, does not own the clock
        [SerializeField] private GameEvent onWeatherChanged;
        [SerializeField] private WeatherProfile[] profiles;

        public float GripMultiplier => Find(current.Value).gripMultiplier;

        private void OnEnable() { if (onDayRolled) onDayRolled.Register(Roll); Apply(current.Value); }
        private void OnDisable() { if (onDayRolled) onDayRolled.Unregister(Roll); }

        public void Roll()
        {
            float total = 0f;
            foreach (var p in profiles) total += p.weight;
            float pick = Random.value * total;
            foreach (var p in profiles)
            {
                pick -= p.weight;
                if (pick > 0f) continue;
                current.Value = p.type;
                Apply(p.type);
                onWeatherChanged?.Raise();
                return;
            }
        }

        /// Called by the save loader after restoring the stored weather.
        public void Apply(WeatherType type)
        {
            var p = Find(type);
            if (p.skybox) RenderSettings.skybox = p.skybox;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
            foreach (var other in profiles)
                if (other.rainVfx) other.rainVfx.SetActive(other.type == type);
        }

        private WeatherProfile Find(WeatherType t)
        {
            foreach (var p in profiles) if (p.type == t) return p;
            return profiles.Length > 0 ? profiles[0] : new WeatherProfile();
        }
    }
}
