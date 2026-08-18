using UnityEngine;

namespace RallyGame.Economy
{
    /// Placement -> money. Early cars finish low and earn little but cost little to
    /// run; later cars reach the top ten where the curve pays off (GDD economy sketch).
    [CreateAssetMenu(menuName = "Rally/Definitions/Payout Table", fileName = "PayoutTable")]
    public class PayoutTable : ScriptableObject
    {
        [Tooltip("Normalised placement (0 = winner, 1 = last) -> share of the event purse.")]
        public AnimationCurve placementShare = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.25f, 0.45f), new Keyframe(0.6f, 0.15f), new Keyframe(1f, 0.04f));

        [Tooltip("Flat entry fee deducted when a race is started.")]
        public int entryFee = 0;

        public int Payout(int purse, int placement, int fieldSize)
        {
            float t = fieldSize <= 1 ? 0f : Mathf.Clamp01((placement - 1f) / (fieldSize - 1f));
            return Mathf.RoundToInt(purse * placementShare.Evaluate(t));
        }
    }
}
