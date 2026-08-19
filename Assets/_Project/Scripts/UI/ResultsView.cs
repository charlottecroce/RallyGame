using System.Text;
using TMPro;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Races.Runtime;
using RallyGame.Utilities;

namespace RallyGame.UI
{
    /// Post-stage / post-event summary panel.
    public class ResultsView : MonoBehaviour
    {
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private GameEvent onStageFinished;
        [SerializeField] private GameEvent onRaceFinished;
        [SerializeField] private TMP_Text body;
        [SerializeField] private GameObject panel;

        private void OnEnable()
        {
            if (onStageFinished) onStageFinished.Register(ShowStage);
            if (onRaceFinished) onRaceFinished.Register(ShowEvent);
        }

        private void OnDisable()
        {
            if (onStageFinished) onStageFinished.Unregister(ShowStage);
            if (onRaceFinished) onRaceFinished.Unregister(ShowEvent);
        }

        private void ShowStage()
        {
            var results = raceManager.DayResults;
            if (results.Count == 0)
            {
                GameLog.Warn(LogCat.UI, "Stage-finished raised but RaceManager.DayResults is empty — nothing to show.", this);
                return;
            }
            var r = results[results.Count - 1];

            var sb = new StringBuilder();
            sb.AppendLine($"STAGE {results.Count} COMPLETE");
            sb.AppendLine($"Time      {Format.LapTime(r.rawTimeSeconds)}");
            if (r.penaltySeconds > 0f) sb.AppendLine($"Penalty   +{r.penaltySeconds:0}s ({r.missedCheckpoints} missed)");
            sb.AppendLine($"Total     {Format.LapTime(r.TotalSeconds)}");
            sb.AppendLine($"Position  {Format.Ordinal(r.placement)} / {r.fieldSize}");

            GameLog.Action(LogCat.UI, "Results panel: stage summary",
                           $"stage {results.Count}, {Format.LapTime(r.TotalSeconds)}, P{r.placement}/{r.fieldSize}", this);
            Show(sb.ToString());
        }

        private void ShowEvent()
        {
            float total = 0f;
            var sb = new StringBuilder("EVENT COMPLETE\n");
            foreach (var r in raceManager.DayResults)
            {
                total += r.TotalSeconds;
                sb.AppendLine($"  {r.stageId}  {Format.LapTime(r.TotalSeconds)}  {Format.Ordinal(r.placement)}");
            }
            sb.AppendLine($"TOTAL {Format.LapTime(total)}");

            GameLog.Action(LogCat.UI, "Results panel: event summary",
                           $"{raceManager.DayResults.Count} stage(s), total {Format.LapTime(total)}", this);
            Show(sb.ToString());
        }

        private void Show(string text)
        {
            if (panel) panel.SetActive(true);
            if (body) body.text = text;
        }

        public void Hide()
        {
            if (panel && panel.activeSelf)
            {
                GameLog.Action(LogCat.UI, "Results panel closed", null, this);
                panel.SetActive(false);
            }
        }
    }
}
