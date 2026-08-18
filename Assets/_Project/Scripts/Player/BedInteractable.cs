using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Player
{
    /// Optional sleep (GDD: never required). Advances the clock, which rolls weather
    /// and - if it crosses Monday - the racebook and dealer stock.
    public class BedInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private float wakeHour = 7f;
        [SerializeField] private CanvasGroup fade;
        [SerializeField] private float fadeSeconds = 0.6f;

        public bool CanInteract => true;
        public string Prompt => $"Sleep until {wakeHour:00}:00 [E]";

        public void Interact(GameObject instigator) => StartCoroutine(SleepRoutine());

        private System.Collections.IEnumerator SleepRoutine()
        {
            yield return Fade(1f);
            clock.SleepUntil(wakeHour);
            yield return Fade(0f);
        }

        private System.Collections.IEnumerator Fade(float target)
        {
            if (!fade) yield break;
            float start = fade.alpha, t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.deltaTime;
                fade.alpha = Mathf.Lerp(start, target, t / fadeSeconds);
                yield return null;
            }
            fade.alpha = target;
        }
    }
}
