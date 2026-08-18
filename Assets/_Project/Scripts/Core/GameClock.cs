using UnityEngine;

namespace RallyGame.Core
{
    public enum Weekday { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }

    /// Single source of truth for in-game time. Writes to SO variables so nothing
    /// has to reference this component. Pausing it is how race mode freezes the world.
    public class GameClock : MonoBehaviour
    {
        [Header("State (SO assets)")]
        [SerializeField] private FloatVariable timeOfDay;   // hours, 0..24
        [SerializeField] private IntVariable dayIndex;      // 0 = Monday of week 1
        [SerializeField] private BoolVariable paused;

        [Header("Channels")]
        [SerializeField] private GameEvent onDayRolled;     // any midnight
        [SerializeField] private GameEvent onWeekRolled;    // Monday 00:00 - racebook + dealer stock
        [SerializeField] private GameEvent onHourRolled;

        [Header("Tuning")]
        [Tooltip("In-game minutes elapsed per real second.")]
        [SerializeField] private float minutesPerSecond = 1f;

        public Weekday Weekday => (Weekday)(((dayIndex.Value % 7) + 7) % 7);
        public float TimeOfDay => timeOfDay.Value;
        public int DayIndex => dayIndex.Value;
        public bool IsNight => timeOfDay.Value < 6f || timeOfDay.Value >= 20f;

        private int lastHour;

        private void Update()
        {
            if (paused && paused.Value) return;
            Advance(Time.deltaTime * minutesPerSecond / 60f);
        }

        /// Advance the clock by whole hours (sleeping, race time-skip, service windows).
        public void Advance(float hours)
        {
            if (hours <= 0f) return;
            float t = timeOfDay.Value + hours;

            while (t >= 24f)
            {
                t -= 24f;
                dayIndex.Value = dayIndex.Value + 1;
                onDayRolled?.Raise();
                if (Weekday == Weekday.Monday) onWeekRolled?.Raise();
            }

            timeOfDay.Value = t;

            int h = Mathf.FloorToInt(t);
            if (h != lastHour) { lastHour = h; onHourRolled?.Raise(); }
        }

        /// Sleep to a target hour, rolling into tomorrow if needed.
        public void SleepUntil(float targetHour)
        {
            float delta = targetHour - timeOfDay.Value;
            if (delta <= 0f) delta += 24f;
            Advance(delta);
        }

        public void SetTime(int day, float hour)
        {
            dayIndex.SetSilent(day);
            timeOfDay.SetSilent(Mathf.Repeat(hour, 24f));
            lastHour = Mathf.FloorToInt(timeOfDay.Value);
        }

        /// True if 'now' falls inside a race window on the given weekday.
        public bool IsWithinWindow(Weekday day, float startHour, float endHour)
            => Weekday == day && timeOfDay.Value >= startHour && timeOfDay.Value < endHour;
    }
}
