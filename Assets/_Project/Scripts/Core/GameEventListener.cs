using UnityEngine;
using UnityEngine.Events;

namespace RallyGame.Core
{
    /// Inspector bridge: lets a scene object react to a GameEvent asset with no code.
    public class GameEventListener : MonoBehaviour
    {
        [SerializeField] private GameEvent channel;
        [SerializeField] private UnityEvent response;

        private void OnEnable() { if (channel) channel.Register(OnRaised); }
        private void OnDisable() { if (channel) channel.Unregister(OnRaised); }
        private void OnRaised() => response?.Invoke();
    }
}
