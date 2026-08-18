using UnityEngine;

namespace RallyGame.Input
{
    /// One component in the scene ticks the reader. Execution order is set low so
    /// every consumer reads fresh values in the same frame.
    [DefaultExecutionOrder(-100)]
    public class InputPump : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        private void Update() => input.Sample();
    }
}
