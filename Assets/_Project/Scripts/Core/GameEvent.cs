using System;
using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.Core
{
    /// Payload-free signal asset. Raiser and listener never reference each other.
    [CreateAssetMenu(menuName = "Rally/Events/Game Event", fileName = "Evt_")]
    public class GameEvent : ScriptableObject
    {
        private readonly List<Action> listeners = new List<Action>();

        public void Raise()
        {
            // Reverse iterate: a listener may unregister itself during the callback.
            for (int i = listeners.Count - 1; i >= 0; i--) listeners[i]?.Invoke();
        }

        public void Register(Action cb) { if (cb != null && !listeners.Contains(cb)) listeners.Add(cb); }
        public void Unregister(Action cb) { listeners.Remove(cb); }
        private void OnDisable() { listeners.Clear(); }
    }

    /// Typed variant for signals that carry data.
    public abstract class GameEvent<T> : ScriptableObject
    {
        private readonly List<Action<T>> listeners = new List<Action<T>>();

        public void Raise(T payload)
        {
            for (int i = listeners.Count - 1; i >= 0; i--) listeners[i]?.Invoke(payload);
        }

        public void Register(Action<T> cb) { if (cb != null && !listeners.Contains(cb)) listeners.Add(cb); }
        public void Unregister(Action<T> cb) { listeners.Remove(cb); }
        private void OnDisable() { listeners.Clear(); }
    }
}
