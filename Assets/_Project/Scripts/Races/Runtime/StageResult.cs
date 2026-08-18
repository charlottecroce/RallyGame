namespace RallyGame.Races.Runtime
{
    /// Immutable outcome of one stage run.
    [System.Serializable]
    public class StageResult
    {
        public string stageId;
        public float rawTimeSeconds;
        public float penaltySeconds;
        public int missedCheckpoints;
        public int placement;
        public int fieldSize;
        public bool didNotFinish;

        public float TotalSeconds => rawTimeSeconds + penaltySeconds;
    }

    /// Aggregate for a rally day or full weekend.
    [System.Serializable]
    public class EventResult
    {
        public string eventId;
        public float totalSeconds;
        public int placement;
        public int fieldSize;
        public int payout;
    }
}
