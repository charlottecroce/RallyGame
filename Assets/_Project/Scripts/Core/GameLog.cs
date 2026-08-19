using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RallyGame.Core
{
    /// Log channels. Bitmask so any combination can be muted at runtime or from
    /// the Rally > Logging editor menu without touching code.
    [Flags]
    public enum LogCat
    {
        None     = 0,
        Core     = 1 << 0,   // bootstrap, definition lookups, generic plumbing
        Clock    = 1 << 1,   // hour/day/week rolls, sleeping, time skips
        State    = 1 << 2,   // ScriptableVariable writes
        Events   = 1 << 3,   // GameEvent raises + listener fan-out
        Input    = 1 << 4,   // discrete presses only, never axes
        Player   = 1 << 5,   // on-foot <-> driving transitions
        Interact = 1 << 6,   // raycast focus changes, prompts, E presses
        Vehicle  = 1 << 7,   // spawn, engine, gearbox, lights, damage
        Garage   = 1 << 8,   // zones, workbench, fitment
        Parts    = 1 << 9,   // install/remove/wear/repair
        Economy  = 1 << 10,  // every money movement
        Dealer   = 1 << 11,  // stock rolls, purchases
        Race     = 1 << 12,  // event lifecycle, service windows, payouts
        Stage    = 1 << 13,  // gates, timers, penalties
        World    = 1 << 14,  // weather, trigger volumes, streaming
        UI       = 1 << 15,  // panel open/close, view rebuilds
        Save     = 1 << 16,  // save/load/new game
        Audio    = 1 << 17,
        All      = ~0
    }

    /// How aggressively a given source is allowed to log.
    /// Throttled is the safe default: a value that changes every frame still
    /// produces at most a couple of lines before it auto-mutes itself.
    public enum LogPolicy
    {
        Throttled = 0,
        Always    = 1,
        Never     = 2
    }

    /// Central logging front-end.
    ///
    /// Every public write method is [Conditional], so in a non-development player
    /// the compiler strips the call *and its arguments* - the string interpolation
    /// at the call site costs nothing in a shipping build.
    ///
    /// Design rule for this project: log DISCRETE events only. Anything that fires
    /// from Update/FixedUpdate on a continuous value goes through Throttle() or
    /// does not get logged at all.
    public static class GameLog
    {
        // ---- configuration -------------------------------------------------

        /// Channels currently printing. Flip at runtime, from the editor menu, or
        /// from a boot script: GameLog.Enabled = LogCat.Race | LogCat.Stage;
        public static LogCat Enabled = LogCat.All;

        /// Master kill switch, independent of the category mask.
        public static bool Muted;

        public static bool ShowFrame     = true;
        public static bool ShowGameClock = true;
        public static bool ShowRealTime  = false;

        /// GameClock installs this on Awake so every line can carry the in-game
        /// date/time. Kept as a delegate so GameLog stays dependency-free.
        public static Func<string> ClockStamp;

        /// Default minimum seconds between two logs from the same throttle key.
        public const float DefaultThrottle = 0.5f;

        /// After this many suppressed lines in a row, a key mutes itself for good
        /// and prints one warning naming the offender. This is what stops a
        /// per-frame value from ever reaching hundreds of lines per second.
        public const int AutoMuteAfter = 16;

        public static bool IsOn(LogCat cat) => !Muted && (Enabled & cat) != 0;

        // ---- write methods -------------------------------------------------

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("RALLY_LOGS")]
        public static void Info(LogCat cat, string message, UnityEngine.Object context = null)
        {
            if (!IsOn(cat)) return;
            Debug.Log(Compose(cat, message), context);
        }

        /// Low-signal detail. Same channel mask, but skipped unless Verbose is on.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("RALLY_LOGS")]
        public static void Verbose(LogCat cat, string message, UnityEngine.Object context = null)
        {
            if (!VerboseEnabled || !IsOn(cat)) return;
            Debug.Log(Compose(cat, message), context);
        }

        public static bool VerboseEnabled;

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("RALLY_LOGS")]
        public static void Warn(LogCat cat, string message, UnityEngine.Object context = null)
        {
            if (Muted) return;                 // warnings ignore the category mask
            Debug.LogWarning(Compose(cat, message), context);
        }

        /// Errors are never conditional and never muted - a broken reference must
        /// surface in a shipping build too.
        public static void Error(LogCat cat, string message, UnityEngine.Object context = null)
        {
            Debug.LogError(Compose(cat, message), context);
        }

        // ---- structured helpers --------------------------------------------

        /// "Something happened, here is the surrounding detail."
        /// GameLog.Action(LogCat.Vehicle, "Engine started", $"car={name} rpm={rpm:0}", this);
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("RALLY_LOGS")]
        public static void Action(LogCat cat, string what, string detail = null, UnityEngine.Object context = null)
        {
            if (!IsOn(cat)) return;
            Debug.Log(Compose(cat, string.IsNullOrEmpty(detail) ? what : $"{what}  <i>({detail})</i>"), context);
        }

        /// A value transition. Prints old -> new so the console reads as a diff.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("RALLY_LOGS")]
        public static void Change(LogCat cat, string subject, object from, object to, UnityEngine.Object context = null)
        {
            if (!IsOn(cat)) return;
            Debug.Log(Compose(cat, $"{subject}: {Fmt(from)} -> {Fmt(to)}"), context);
        }

        /// A request that was refused, with the reason. These are the lines that
        /// actually explain "why did nothing happen when I pressed E".
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("RALLY_LOGS")]
        public static void Refused(LogCat cat, string what, string reason, UnityEngine.Object context = null)
        {
            if (!IsOn(cat)) return;
            Debug.Log(Compose(cat, $"REFUSED {what}  <i>({reason})</i>"), context);
        }

        // ---- throttling ----------------------------------------------------

        private struct Gate
        {
            public float lastTime;
            public int   suppressed;
            public bool  muted;
        }

        private static readonly Dictionary<int, Gate> gates = new Dictionary<int, Gate>(64);

        /// Returns true if the caller may log right now.
        /// Not [Conditional] (it returns a value), so guard the call site with
        /// #if or accept the tiny dictionary lookup in release.
        ///
        /// key       stable identifier - prefer the Object overload below for assets/components
        /// interval  minimum seconds between lines from this key
        public static bool Throttle(int key, float interval = DefaultThrottle, string label = null)
        {
            if (Muted) return false;

            float now = Time.unscaledTime;
            gates.TryGetValue(key, out var g);

            if (g.muted) return false;

            if (g.lastTime > 0f && now - g.lastTime < interval)
            {
                g.suppressed++;
                if (g.suppressed >= AutoMuteAfter)
                {
                    g.muted = true;
                    gates[key] = g;
                    Debug.LogWarning(Compose(LogCat.Core,
                        $"Auto-muted a continuous log source{(label != null ? $" '{label}'" : "")} after " +
                        $"{AutoMuteAfter} suppressed lines. This value changes every frame - set its " +
                        $"Log Policy to Never on the asset, or call GameLog.Unmute() to re-enable."));
                    return false;
                }
                gates[key] = g;
                return false;
            }

            g.lastTime = now;
            g.suppressed = 0;
            gates[key] = g;
            return true;
        }

        /// Preferred overload for assets and components.
        ///
        /// Deliberately does NOT use GetInstanceID(): Unity 6.3 deprecated it in
        /// favour of GetEntityId(), which does not exist on earlier versions.
        /// RuntimeHelpers.GetHashCode gives a stable per-object identity with no
        /// Unity API involved, so this compiles on every version.
        public static bool Throttle(UnityEngine.Object source, float interval = DefaultThrottle)
            => Throttle(RuntimeHelpers.GetHashCode(source), interval, source ? source.name : null);

        public static void Unmute(int key)
        {
            if (gates.TryGetValue(key, out var g)) { g.muted = false; g.suppressed = 0; gates[key] = g; }
        }

        public static void Unmute(UnityEngine.Object source) => Unmute(RuntimeHelpers.GetHashCode(source));

        public static void ResetThrottles() => gates.Clear();

        // ---- formatting ----------------------------------------------------

        private static string Compose(LogCat cat, string message)
        {
            var head = string.Empty;

            if (ShowGameClock && ClockStamp != null) head += ClockStamp() + " ";
            if (ShowFrame)     head += $"f{Time.frameCount} ";
            if (ShowRealTime)  head += $"{Time.realtimeSinceStartup:0.00}s ";

#if UNITY_EDITOR
            return $"<color=#7f7f7f>{head}</color><color={Colour(cat)}><b>[{cat}]</b></color> {message}";
#else
            return $"{head}[{cat}] {message}";
#endif
        }

        private static string Fmt(object o)
        {
            if (o == null) return "<null>";
            if (o is float f) return f.ToString("0.###");
            if (o is string s) return s.Length == 0 ? "\"\"" : $"\"{s}\"";
            if (o is bool b) return b ? "true" : "false";
            return o.ToString();
        }

        private static string Colour(LogCat cat)
        {
            switch (cat)
            {
                case LogCat.Clock:    return "#8fbcd4";
                case LogCat.State:    return "#9d8fd4";
                case LogCat.Events:   return "#c58fd4";
                case LogCat.Input:    return "#8f9fd4";
                case LogCat.Player:   return "#6fd48f";
                case LogCat.Interact: return "#ffd166";
                case LogCat.Vehicle:  return "#ef8354";
                case LogCat.Garage:   return "#d4b28f";
                case LogCat.Parts:    return "#d4c98f";
                case LogCat.Economy:  return "#6fd4b8";
                case LogCat.Dealer:   return "#4fb3a5";
                case LogCat.Race:     return "#ff6b6b";
                case LogCat.Stage:    return "#ff9f9f";
                case LogCat.World:    return "#7fd4d4";
                case LogCat.UI:       return "#c0c0c0";
                case LogCat.Save:     return "#a0a0ff";
                case LogCat.Audio:    return "#b88fd4";
                default:              return "#bfbfbf";
            }
        }

        // ---- editor menu ---------------------------------------------------
#if UNITY_EDITOR
        private const string PrefMask    = "RallyGame.GameLog.Mask";
        private const string PrefVerbose = "RallyGame.GameLog.Verbose";
        private const string PrefMuted   = "RallyGame.GameLog.Muted";

        [UnityEditor.InitializeOnLoadMethod]
        private static void LoadPrefs()
        {
            Enabled        = (LogCat)UnityEditor.EditorPrefs.GetInt(PrefMask, (int)LogCat.All);
            VerboseEnabled = UnityEditor.EditorPrefs.GetBool(PrefVerbose, false);
            Muted          = UnityEditor.EditorPrefs.GetBool(PrefMuted, false);
        }

        [UnityEditor.MenuItem("Rally/Logging/Enable All Channels")]
        private static void MenuAll()
        {
            Enabled = LogCat.All; Muted = false;
            UnityEditor.EditorPrefs.SetInt(PrefMask, (int)Enabled);
            UnityEditor.EditorPrefs.SetBool(PrefMuted, false);
            Debug.Log("[GameLog] All channels enabled.");
        }

        [UnityEditor.MenuItem("Rally/Logging/Mute Everything")]
        private static void MenuMute()
        {
            Muted = true;
            UnityEditor.EditorPrefs.SetBool(PrefMuted, true);
            Debug.Log("[GameLog] Muted.");
        }

        [UnityEditor.MenuItem("Rally/Logging/Toggle Verbose")]
        private static void MenuVerbose()
        {
            VerboseEnabled = !VerboseEnabled;
            UnityEditor.EditorPrefs.SetBool(PrefVerbose, VerboseEnabled);
            Debug.Log($"[GameLog] Verbose {(VerboseEnabled ? "ON" : "OFF")}.");
        }

        [UnityEditor.MenuItem("Rally/Logging/Gameplay Only (no State or Events)")]
        private static void MenuGameplay()
        {
            Enabled = LogCat.All & ~LogCat.State & ~LogCat.Events;
            Muted = false;
            UnityEditor.EditorPrefs.SetInt(PrefMask, (int)Enabled);
            UnityEditor.EditorPrefs.SetBool(PrefMuted, false);
            Debug.Log("[GameLog] Gameplay channels only.");
        }

        [UnityEditor.MenuItem("Rally/Logging/Reset Auto-Muted Sources")]
        private static void MenuResetThrottles()
        {
            ResetThrottles();
            Debug.Log("[GameLog] Throttle gates cleared.");
        }
#endif
    }
}