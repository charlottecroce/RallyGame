using UnityEngine;

namespace RallyGame.Core
{
    public enum Weekday { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }

    /// Single source of truth for in-game time. Writes to SO variables so nothing
    /// has to reference this component. Pausing it is how race mode freezes the world.
    ///
    /// Logging note: Advance() runs every frame with a sub-second delta, so the
    /// continuous case is deliberately silent. Only discrete transitions are logged
    /// - hour rolls, day rolls, week rolls, sleeps, pauses and explicit time skips.
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

        [Header("Debug")]
        [Tooltip("Any explicit Advance() of at least this many hours is logged as a time skip.")]
        [SerializeField] private float skipLogThreshold = 0.05f;
        [Tooltip("Log every hour boundary. Turn off if you only care about days.")]
        [SerializeField] private bool logHourRolls = true;

        public Weekday Weekday => (Weekday)(((dayIndex.Value % 7) + 7) % 7);
        public float TimeOfDay => timeOfDay.Value;
        public int DayIndex => dayIndex.Value;
        public bool IsNight => timeOfDay.Value < 6f || timeOfDay.Value >= 20f;

        private int lastHour;
        private bool lastPaused;

        private void Awake()
        {
            // Every log line from any system now carries the in-game date/time.
            GameLog.ClockStamp = Stamp;
            lastHour = Mathf.FloorToInt(timeOfDay.Value);
            GameLog.Action(LogCat.Clock, "Clock online",
                           $"day {dayIndex.Value} ({Weekday}) {HHMM(timeOfDay.Value)}, {minutesPerSecond} min/sec", this);
        }

        private void OnDestroy()
        {
            if (GameLog.ClockStamp == (System.Func<string>)Stamp) GameLog.ClockStamp = null;
        }

        private void Update()
        {
            bool isPaused = paused && paused.Value;
            if (isPaused != lastPaused)
            {
                lastPaused = isPaused;
                GameLog.Action(LogCat.Clock, isPaused ? "Clock PAUSED" : "Clock RESUMED",
                               $"at {HHMM(timeOfDay.Value)}", this);
            }

            if (isPaused) return;
            Advance(Time.deltaTime * minutesPerSecond / 60f, false);
        }

        /// Advance the clock by whole hours (sleeping, race time-skip, service windows).
        public void Advance(float hours) => Advance(hours, true);

        private void Advance(float hours, bool explicitCall)
        {
            if (hours <= 0f) return;

            if (explicitCall && hours >= skipLogThreshold)
                GameLog.Action(LogCat.Clock, "Time skip requested",
                               $"+{hours:0.##}h from {HHMM(timeOfDay.Value)} (day {dayIndex.Value})", this);

            float t = timeOfDay.Value + hours;

            while (t >= 24f)
            {
                t -= 24f;
                dayIndex.Value = dayIndex.Value + 1;

                GameLog.Action(LogCat.Clock, "Day rolled",
                               $"now day {dayIndex.Value} — {Weekday}", this);
                onDayRolled?.Raise();

                if (Weekday == Weekday.Monday)
                {
                    GameLog.Action(LogCat.Clock, "WEEK rolled",
                                   $"week {(dayIndex.Value / 7) + 1} begins — racebook and dealer stock refresh", this);
                    onWeekRolled?.Raise();
                }
            }

            timeOfDay.Value = t;

            int h = Mathf.FloorToInt(t);
            if (h != lastHour)
            {
                lastHour = h;
                if (logHourRolls)
                    GameLog.Action(LogCat.Clock, "Hour rolled", $"{h:00}:00 — {Weekday}{(IsNight ? " (night)" : "")}", this);
                onHourRolled?.Raise();
            }
        }

        /// Sleep to a target hour, rolling into tomorrow if needed.
        public void SleepUntil(float targetHour)
        {
            float delta = targetHour - timeOfDay.Value;
            if (delta <= 0f) delta += 24f;

            GameLog.Action(LogCat.Clock, "Sleeping",
                           $"{HHMM(timeOfDay.Value)} -> {HHMM(targetHour)} ({delta:0.##}h)", this);
            Advance(delta, false);
            GameLog.Action(LogCat.Clock, "Woke up", $"day {dayIndex.Value} ({Weekday}) {HHMM(timeOfDay.Value)}", this);
        }

        public void SetTime(int day, float hour)
        {
            GameLog.Change(LogCat.Clock, "Clock forced",
                           $"day {dayIndex.Value} {HHMM(timeOfDay.Value)}",
                           $"day {day} {HHMM(hour)}", this);

            dayIndex.SetSilent(day);
            timeOfDay.SetSilent(Mathf.Repeat(hour, 24f));
            lastHour = Mathf.FloorToInt(timeOfDay.Value);
        }

        /// True if 'now' falls inside a race window on the given weekday.
        public bool IsWithinWindow(Weekday day, float startHour, float endHour)
            => Weekday == day && timeOfDay.Value >= startHour && timeOfDay.Value < endHour;

        // ---- debug ---------------------------------------------------------

        private string Stamp() => $"D{dayIndex.Value}|{HHMM(timeOfDay.Value)}";

        private static string HHMM(float hours)
        {
            int h = Mathf.FloorToInt(hours);
            int m = Mathf.FloorToInt((hours - h) * 60f);
            return $"{h:00}:{m:00}";
        }
    }
}
