using UnityEngine;
using RallyGame.Races.Runtime;

namespace RallyGame.Core
{
    /// Deterministic startup: reset volatile SO state, then load or start fresh.
    /// Runs before everything else so no system observes half-initialised data.
    [DefaultExecutionOrder(-200)]
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private SaveManager saves;
        [SerializeField] private RaceState raceState;
        [SerializeField] private bool loadSaveOnStart = true;
        [SerializeField] private int targetFrameRate = 60;

        private void Awake()
        {
            Application.targetFrameRate = targetFrameRate;
            raceState.Reset();
        }

        private void Start()
        {
            if (loadSaveOnStart && saves.HasSave && saves.Load()) return;
            saves.NewGame();
        }
    }
}
