using UnityEngine;
using RallyGame.Vehicles.Controllers;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RallyGame.Input
{
    /// Single input surface for the whole game. Compiles against either input backend,
    /// so swapping to an InputActions asset later means editing only this file.
    [CreateAssetMenu(menuName = "Rally/Input Reader", fileName = "InputReader")]
    public class InputReader : ScriptableObject
    {
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool MenuPressed { get; private set; }
        public bool MapPressed { get; private set; }
        public bool RaceBookPressed { get; private set; }
        public bool LightsPressed { get; private set; }
        public VehicleInput Vehicle { get; private set; }

        [SerializeField] private float lookSensitivity = 0.12f;

        /// Called once per frame by InputPump. Nothing else polls devices.
        public void Sample()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) { Vehicle = default; return; }

            Move = new Vector2(
                (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            Look = mouse != null ? mouse.delta.ReadValue() * lookSensitivity : Vector2.zero;

            JumpPressed = kb.spaceKey.wasPressedThisFrame;
            InteractPressed = kb.eKey.wasPressedThisFrame;
            MenuPressed = kb.escapeKey.wasPressedThisFrame;
            MapPressed = kb.mKey.wasPressedThisFrame;
            RaceBookPressed = kb.tabKey.wasPressedThisFrame;
            LightsPressed = kb.lKey.wasPressedThisFrame;

            Vehicle = new VehicleInput
            {
                throttle = kb.wKey.isPressed ? 1f : 0f,
                brake = kb.sKey.isPressed ? 1f : 0f,
                steer = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                handbrake = kb.spaceKey.isPressed,
                shiftUp = kb.eKey.wasPressedThisFrame,
                shiftDown = kb.qKey.wasPressedThisFrame,
                lights = lightsOn
            };
#else
            Move = new Vector2(UnityEngine.Input.GetAxisRaw("Horizontal"), UnityEngine.Input.GetAxisRaw("Vertical"));
            Look = new Vector2(UnityEngine.Input.GetAxisRaw("Mouse X"), UnityEngine.Input.GetAxisRaw("Mouse Y")) * (lookSensitivity * 10f);

            JumpPressed = UnityEngine.Input.GetKeyDown(KeyCode.Space);
            InteractPressed = UnityEngine.Input.GetKeyDown(KeyCode.E);
            MenuPressed = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
            MapPressed = UnityEngine.Input.GetKeyDown(KeyCode.M);
            RaceBookPressed = UnityEngine.Input.GetKeyDown(KeyCode.Tab);
            LightsPressed = UnityEngine.Input.GetKeyDown(KeyCode.L);

            Vehicle = new VehicleInput
            {
                throttle = Mathf.Max(0f, UnityEngine.Input.GetAxis("Vertical")),
                brake = Mathf.Max(0f, -UnityEngine.Input.GetAxis("Vertical")),
                steer = UnityEngine.Input.GetAxis("Horizontal"),
                handbrake = UnityEngine.Input.GetKey(KeyCode.Space),
                shiftUp = UnityEngine.Input.GetKeyDown(KeyCode.E),
                shiftDown = UnityEngine.Input.GetKeyDown(KeyCode.Q),
                lights = lightsOn
            };
#endif
            if (LightsPressed) lightsOn = !lightsOn;
        }

        [System.NonSerialized] private bool lightsOn;

        /// Menus call this so held keys do not leak into gameplay.
        public void Clear() { Move = Vector2.zero; Look = Vector2.zero; Vehicle = default; }
    }
}
