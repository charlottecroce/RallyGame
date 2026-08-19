using UnityEngine;
using UnityEngine.Events;

namespace RallyGame.Core
{
    /// Inspector bridge: lets a scene object react to a GameEvent asset with no code.
    ///
    /// Worth logging because these are the hardest reactions to trace - the wiring
    /// lives in the inspector, so the console is the only place it shows up.
    public class GameEventListener : MonoBehaviour
    {
        [SerializeField] private GameEvent channel;
        [SerializeField] private UnityEvent response;

        [Header("Debug")]
        [SerializeField] private LogPolicy logPolicy = LogPolicy.Throttled;

        private void OnEnable()
        {
            if (channel)
            {
                channel.Register(OnRaised);
                GameLog.Verbose(LogCat.Events, $"{name} listening to {channel.name}", this);
            }
            else
            {
                GameLog.Warn(LogCat.Events, $"{name} has a GameEventListener with no channel assigned.", this);
            }
        }

        private void OnDisable()
        {
            if (channel) channel.Unregister(OnRaised);
        }

        private void OnRaised()
        {
            if (logPolicy != LogPolicy.Never &&
                (logPolicy == LogPolicy.Always || GameLog.Throttle(this)))
            {
                int calls = response == null ? 0 : response.GetPersistentEventCount();
                GameLog.Action(LogCat.Events, $"{name} reacting to {channel.name}",
                               $"{calls} inspector call{(calls == 1 ? "" : "s")}", this);
            }

            response?.Invoke();
        }
    }
}