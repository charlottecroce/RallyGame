using UnityEngine;

namespace RallyGame.Core
{
    public enum WeatherType { Sunny, Cloudy, Rainy }

    /// Rolls weather at midnight. Grip multiplier is consumed by the tire model,
    /// so gameplay never reads this component directly.
    ///
    /// Weather changes once a day, which makes it cheap to log and very useful:
    /// "why is the car suddenly sliding" is answered by one line.
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

        private void OnEnable()
        {
            if (onDayRolled) onDayRolled.Register(Roll);
            Apply(current.Value);
            GameLog.Verbose(LogCat.World,
                $"Weather system online: {profiles.Length} profile(s), starting on {current.Value}", this);
        }

        private void OnDisable() { if (onDayRolled) onDayRolled.Unregister(Roll); }

        public void Roll()
        {
            float total = 0f;
            foreach (var p in profiles) total += p.weight;

            if (total <= 0f)
            {
                GameLog.Warn(LogCat.World, "Weather roll skipped — every profile has zero weight.", this);
                return;
            }

            var previous = current.Value;
            float pick = Random.value * total;

            foreach (var p in profiles)
            {
                pick -= p.weight;
                if (pick > 0f) continue;

                current.Value = p.type;
                Apply(p.type);

                GameLog.Action(LogCat.World, "WEATHER ROLLED",
                               $"{previous} -> {p.type}, grip multiplier {p.gripMultiplier:0.00}", this);

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

            GameLog.Verbose(LogCat.World,
                $"Weather visuals applied: {type} (fog {p.fogDensity:0.0000}, grip {p.gripMultiplier:0.00})", this);
        }

        private WeatherProfile Find(WeatherType t)
        {
            foreach (var p in profiles) if (p.type == t) return p;

            GameLog.Warn(LogCat.World,
                $"No weather profile authored for {t} — falling back to the first entry.", this);

            return profiles.Length > 0 ? profiles[0] : new WeatherProfile();
        }
    }
}
