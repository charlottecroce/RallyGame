using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Input
{
    /// Temporary. Reports what the vehicle is actually being told to do, and whether
    /// the reader is still being sampled at all. Put this on a scene object that is
    /// NOT a child of the on-foot root, or it will be disabled along with everything
    /// else the moment the player gets into the car.
    public class InputDiagnostics : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private float intervalSeconds = 1f;

        private float next;

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + Mathf.Max(0.1f, intervalSeconds);

            int stale = Time.frameCount - input.LastSampleFrame;
            var v = input.Vehicle;

            string line = $"Input: throttle={v.throttle:0.00} brake={v.brake:0.00} " +
                          $"steer={v.steer:0.00} handbrake={v.handbrake}  " +
                          $"(last sampled {stale} frame(s) ago)";

            if (stale > 2)
                GameLog.Error(LogCat.Input,
                    line + "\n  InputReader.Sample() has stopped running. InputPump is probably " +
                    "parented under the on-foot root, which VehicleEntry.Enter() deactivates.", this);
            else
                GameLog.Action(LogCat.Input, "Input", line, this);
        }
    }
}