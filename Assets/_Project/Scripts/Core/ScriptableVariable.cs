using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace RallyGame.Core
{
    /// Shared mutable runtime state stored as an asset.
    /// Systems reference the asset directly - no singleton, no scene lookup, no load-order problem.
    ///
    /// Every write funnels through the Value setter, so instrumenting it here gives
    /// a running diff of the whole game's state (prompt text, isDriving, money,
    /// playerInGarage, weather, ...) for free.
    ///
    /// IMPORTANT: set Log Policy to Never on any variable written every frame
    /// (Var_TimeOfDay is the obvious one). If you forget, the throttle will
    /// auto-mute it after ~16 suppressed lines and tell you which asset to fix.
    public abstract class ScriptableVariable<T> : ScriptableObject
    {
        [SerializeField] private T initialValue;
        [Tooltip("Optional channel raised whenever Value changes.")]
        [SerializeField] private GameEvent changed;

        [Header("Debug")]
        [Tooltip("Throttled: at most one line per half second, auto-mutes if it changes every frame.\n" +
                 "Always: every write. Never: silent — use for continuous values like time of day.")]
        [SerializeField] private LogPolicy logPolicy = LogPolicy.Throttled;
        [Tooltip("Which channel these writes appear under. State keeps them separable from gameplay logs.")]
        [SerializeField] private LogCat logCategory = LogCat.State;

        [System.NonSerialized] private T runtime;
        [System.NonSerialized] private bool primed;

        public T Value
        {
            get { Prime(); return runtime; }
            set
            {
                Prime();
                if (EqualityComparer<T>.Default.Equals(runtime, value)) return;

                T previous = runtime;
                runtime = value;
                LogWrite(previous, value, false);

                if (changed) changed.Raise();
            }
        }

        /// Write without firing listeners. Used by the save loader so half-restored
        /// state never triggers UI/gameplay reactions.
        public void SetSilent(T value)
        {
            Prime();
            if (EqualityComparer<T>.Default.Equals(runtime, value)) return;

            T previous = runtime;
            runtime = value;
            LogWrite(previous, value, true);
        }

        public void ResetToInitial()
        {
            runtime = initialValue;
            primed = true;
            GameLog.Verbose(logCategory, $"{name} reset to authored value {initialValue}", this);
        }

        private void Prime() { if (!primed) ResetToInitial(); }

        // Asset load / domain reload: drop runtime state so play mode always starts authored.
        private void OnEnable() { primed = false; }

        // ---- debug ---------------------------------------------------------

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("RALLY_LOGS")]
        private void LogWrite(T from, T to, bool silent)
        {
            if (logPolicy == LogPolicy.Never) return;
            if (logPolicy == LogPolicy.Throttled && !GameLog.Throttle(this)) return;

            GameLog.Change(logCategory, silent ? $"{name} (silent)" : name, from, to, this);
        }
    }
}