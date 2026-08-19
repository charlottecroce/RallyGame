using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace RallyGame.Core
{
    /// Payload-free signal asset. Raiser and listener never reference each other.
    ///
    /// Because every system in this project communicates through these assets,
    /// instrumenting Raise() here logs essentially every meaningful game event
    /// (door opened, week rolled, stock restocked, race started, save written)
    /// without touching the systems that raise them.
    [CreateAssetMenu(menuName = "Rally/Events/Game Event", fileName = "Evt_")]
    public class GameEvent : ScriptableObject
    {
        [Header("Debug")]
        [Tooltip("Throttled: at most one line per half second, auto-mutes if it fires every frame.\n" +
                 "Always: every raise. Never: silent (use for high-frequency channels).")]
        [SerializeField] private LogPolicy logPolicy = LogPolicy.Throttled;
        [Tooltip("Also list who received the signal. Noisy but invaluable when a listener is silently missing.")]
        [SerializeField] private bool logListeners = false;

        private readonly List<Action> listeners = new List<Action>();

        public int ListenerCount => listeners.Count;

        public void Raise()
        {
            LogRaise();

            // Reverse iterate: a listener may unregister itself during the callback.
            for (int i = listeners.Count - 1; i >= 0; i--) listeners[i]?.Invoke();
        }

        public void Register(Action cb)
        {
            if (cb != null && !listeners.Contains(cb))
            {
                listeners.Add(cb);
                GameLog.Verbose(LogCat.Events, $"{name} <- register {Describe(cb)} (now {listeners.Count})", this);
            }
        }

        public void Unregister(Action cb)
        {
            if (listeners.Remove(cb))
                GameLog.Verbose(LogCat.Events, $"{name} -> unregister {Describe(cb)} (now {listeners.Count})", this);
        }

        private void OnDisable() { listeners.Clear(); }

        // ---- debug ---------------------------------------------------------

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("RALLY_LOGS")]
        private void LogRaise()
        {
            if (logPolicy == LogPolicy.Never) return;
            if (logPolicy == LogPolicy.Throttled && !GameLog.Throttle(this)) return;

            if (listeners.Count == 0)
            {
                GameLog.Info(LogCat.Events, $"{name} raised — <b>no listeners</b>", this);
                return;
            }

            if (logListeners) GameLog.Info(LogCat.Events, $"{name} raised -> {ListenerNames()}", this);
            else              GameLog.Info(LogCat.Events, $"{name} raised ({listeners.Count} listener{(listeners.Count == 1 ? "" : "s")})", this);
        }

        private string ListenerNames()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < listeners.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Describe(listeners[i]));
            }
            return sb.ToString();
        }

        internal static string Describe(Delegate cb)
        {
            if (cb == null) return "<null>";
            string owner = cb.Target is UnityEngine.Object uo && uo ? uo.name
                         : cb.Target != null ? cb.Target.GetType().Name
                         : "static";
            return $"{owner}.{cb.Method.Name}";
        }
    }

    /// Typed variant for signals that carry data.
    public abstract class GameEvent<T> : ScriptableObject
    {
        [Header("Debug")]
        [SerializeField] private LogPolicy logPolicy = LogPolicy.Throttled;

        private readonly List<Action<T>> listeners = new List<Action<T>>();

        public int ListenerCount => listeners.Count;

        public void Raise(T payload)
        {
            if (logPolicy != LogPolicy.Never &&
                (logPolicy == LogPolicy.Always || GameLog.Throttle(this)))
            {
                GameLog.Info(LogCat.Events,
                    $"{name} raised with <b>{payload}</b> ({listeners.Count} listener{(listeners.Count == 1 ? "" : "s")})", this);
            }

            for (int i = listeners.Count - 1; i >= 0; i--) listeners[i]?.Invoke(payload);
        }

        public void Register(Action<T> cb)
        {
            if (cb != null && !listeners.Contains(cb))
            {
                listeners.Add(cb);
                GameLog.Verbose(LogCat.Events, $"{name} <- register {GameEvent.Describe(cb)} (now {listeners.Count})", this);
            }
        }

        public void Unregister(Action<T> cb)
        {
            if (listeners.Remove(cb))
                GameLog.Verbose(LogCat.Events, $"{name} -> unregister {GameEvent.Describe(cb)} (now {listeners.Count})", this);
        }

        private void OnDisable() { listeners.Clear(); }
    }
}