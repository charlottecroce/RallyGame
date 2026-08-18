using System.Collections.Generic;
using RallyGame.Races.Data;
using RallyGame.Utilities;

namespace RallyGame.Races.Runtime
{
    /// No AI cars exist (cut list) - rivals are numbers generated around par time.
    /// Deterministic per (event, stage) so the same run always scores the same.
    public static class RivalTimes
    {
        public static List<float> Generate(RaceEvent evt, StageDefinition stage, int fieldSize)
        {
            var rng = new DeterministicRandom(evt.eventId.GetHashCode(), stage.id);
            var times = new List<float>(fieldSize);

            // Spread: fast entries beat par, backmarkers well off it.
            for (int i = 0; i < fieldSize; i++)
            {
                float skill = (float)i / System.Math.Max(1, fieldSize - 1);
                float factor = 0.88f + skill * 0.42f + rng.Range(-0.03f, 0.03f);
                times.Add(stage.parTimeSeconds * factor);
            }

            times.Sort();
            return times;
        }

        /// 1-based placement of the player's time within the rival field.
        public static int Placement(List<float> rivalTimes, float playerTime)
        {
            int place = 1;
            foreach (var t in rivalTimes) { if (playerTime > t) place++; else break; }
            return place;
        }
    }
}
