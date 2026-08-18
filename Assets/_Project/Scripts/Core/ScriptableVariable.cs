using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.Core
{
    /// Shared mutable runtime state stored as an asset.
    /// Systems reference the asset directly - no singleton, no scene lookup, no load-order problem.
    public abstract class ScriptableVariable<T> : ScriptableObject
    {
        [SerializeField] private T initialValue;
        [Tooltip("Optional channel raised whenever Value changes.")]
        [SerializeField] private GameEvent changed;

        [System.NonSerialized] private T runtime;
        [System.NonSerialized] private bool primed;

        public T Value
        {
            get { Prime(); return runtime; }
            set
            {
                Prime();
                if (EqualityComparer<T>.Default.Equals(runtime, value)) return;
                runtime = value;
                if (changed) changed.Raise();
            }
        }

        /// Write without firing listeners. Used by the save loader so half-restored
        /// state never triggers UI/gameplay reactions.
        public void SetSilent(T value) { Prime(); runtime = value; }

        public void ResetToInitial() { runtime = initialValue; primed = true; }

        private void Prime() { if (!primed) ResetToInitial(); }

        // Asset load / domain reload: drop runtime state so play mode always starts authored.
        private void OnEnable() { primed = false; }
    }
}
