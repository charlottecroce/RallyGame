using UnityEngine;

namespace RallyGame.Utilities
{
    /// Display formatting shared by HUD, results and racebook.
    public static class Format
    {
        public static string LapTime(float seconds)
        {
            if (seconds < 0f) return "--:--.---";
            int m = Mathf.FloorToInt(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m:00}:{s:00.000}";
        }

        public static string Delta(float seconds)
            => (seconds >= 0f ? "+" : "-") + LapTime(Mathf.Abs(seconds));

        public static string Clock24(float hours)
        {
            int h = Mathf.FloorToInt(hours);
            int m = Mathf.FloorToInt((hours - h) * 60f);
            return $"{h:00}:{m:00}";
        }

        public static string Money(float amount) => $"${amount:N0}";

        public static string Ordinal(int n)
        {
            if (n % 100 is >= 11 and <= 13) return n + "th";
            return (n % 10) switch { 1 => n + "st", 2 => n + "nd", 3 => n + "rd", _ => n + "th" };
        }

        public static string Percent(float unit01) => Mathf.RoundToInt(Mathf.Clamp01(unit01) * 100f) + "%";
    }
}
