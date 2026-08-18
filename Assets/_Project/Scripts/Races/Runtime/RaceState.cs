using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Races.Runtime
{
    public enum RacePhase { None, Countdown, Running, ServiceWindow, Finished }

    /// Shared race-mode flags as an asset. The save gate, HUD and open-world systems
    /// all read this without referencing RaceManager.
    [CreateAssetMenu(menuName = "Rally/State/Race State", fileName = "RaceState")]
    public class RaceState : ScriptableObject
    {
        [System.NonSerialized] public bool inRace;
        [System.NonSerialized] public RacePhase phase = RacePhase.None;
        [System.NonSerialized] public string activeEventId;
        [System.NonSerialized] public string activeStageId;
        [System.NonSerialized] public int stageIndex;
        [System.NonSerialized] public int stageCount;
        [System.NonSerialized] public float stageTime;
        [System.NonSerialized] public int nextCheckpoint;
        [System.NonSerialized] public int totalCheckpoints;
        [System.NonSerialized] public float serviceSecondsRemaining;

        [SerializeField] private GameEvent onRaceStateChanged;

        /// Single gate used by the save system (GDD: save anywhere except during a race).
        public bool CanSave => !inRace;

        public void Notify() => onRaceStateChanged?.Raise();

        public void Reset()
        {
            inRace = false; phase = RacePhase.None;
            activeEventId = activeStageId = null;
            stageIndex = stageCount = nextCheckpoint = totalCheckpoints = 0;
            stageTime = serviceSecondsRemaining = 0f;
            Notify();
        }

        private void OnEnable() => Reset();
    }
}
