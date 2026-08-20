using UnityEngine;
using RallyGame.Core;
using RallyGame.Vehicles.Controllers;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RallyGame.Input
{
    /// Single input surface for the whole game.
    ///
    /// Throttle, brake and steer are now RAMPED rather than binary. This is not
    /// cosmetic: weight transfer reacts to the rate of input, and a key that snaps
    /// 0 -> 1 in one frame will snap the car with it. Analogue devices bypass the
    /// ramp entirely.
    ///
    /// Binding change: shift up/down moved off E (which also exits the car — pressing
    /// E to upshift used to eject you) onto LeftShift / LeftCtrl.
    [CreateAssetMenu(menuName = "Rally/Input Reader", fileName = "InputReader")]
    public class InputReader : ScriptableObject
    {
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public int LastSampleFrame { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool MenuPressed { get; private set; }
        public bool MapPressed { get; private set; }
        public bool RaceBookPressed { get; private set; }
        public bool LightsPressed { get; private set; }
        public bool ResetPressed { get; private set; }
        public VehicleInput Vehicle { get; private set; }
        public TransmissionMode Transmission => mode;
        [SerializeField] private float lookSensitivity = 0.12f;

        [Header("Pedal ramps (keyboard only)")]
        [Tooltip("Units per second onto the throttle. Lower = you have to feed it in.")]
        [SerializeField] private float throttleRise = 3.2f;
        [SerializeField] private float throttleFall = 6f;
        [Tooltip("Brake rise is the one to tune for feel — this is what triggers the dive.")]
        [SerializeField] private float brakeRise = 4.5f;
        [SerializeField] private float brakeFall = 8f;
        [Tooltip("Steering rise. The controller smooths again on top of this.")]
        [SerializeField] private float steerRise = 2.8f;
        [SerializeField] private float steerFall = 5.5f;
        [Tooltip("Steer ramp slows down at speed so you cannot flick the car at 140.")]
        [SerializeField] private float steerSpeedDamping = 0.45f;

        [Header("Gearbox")]
        [Tooltip("Mode the game starts in. Players toggle with G at any time.")]
        [SerializeField] private TransmissionMode defaultTransmission = TransmissionMode.Manual;

        [Header("Debug")]
        [SerializeField] private bool logKeyPresses = true;

        private bool warnedNoKeyboard;
        [System.NonSerialized] private bool lightsOn;
        [System.NonSerialized] private TransmissionMode mode;
        [System.NonSerialized] private bool modeInitialised;

        // Ramped pedal state, persisted between frames.
        [System.NonSerialized] private float throttle, brake, steer;
        [System.NonSerialized] private float lastKnownSpeed01;

        /// Called once per frame by InputPump. Nothing else polls devices.
        public void Sample()
        {
            LastSampleFrame = Time.frameCount;
            if (!modeInitialised) { mode = defaultTransmission; modeInitialised = true; }

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var pad = Gamepad.current;

            if (kb == null && pad == null)
            {
                if (!warnedNoKeyboard)
                {
                    warnedNoKeyboard = true;
                    GameLog.Warn(LogCat.Input, "No keyboard or gamepad found — all input will read as zero.", this);
                }
                Vehicle = default;
                return;
            }
            warnedNoKeyboard = false;

            float kx = kb != null ? (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f) : 0f;
            float ky = kb != null ? (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f) : 0f;

            Move = new Vector2(kx, ky);
            Look = Mouse.current != null ? Mouse.current.delta.ReadValue() * lookSensitivity : Vector2.zero;

            JumpPressed = Pressed(kb?.spaceKey) || Pressed(pad?.buttonEast);
            InteractPressed = Pressed(kb?.eKey) || Pressed(pad?.buttonNorth);
            MenuPressed = Pressed(kb?.escapeKey) || Pressed(pad?.startButton);
            MapPressed = Pressed(kb?.mKey);
            RaceBookPressed = Pressed(kb?.tabKey) || Pressed(pad?.selectButton);
            LightsPressed = Pressed(kb?.lKey) || Pressed(pad?.dpad.up);
            ResetPressed = Pressed(kb?.rKey) || Pressed(pad?.dpad.down);

            bool modePressed = Pressed(kb?.gKey) || Pressed(pad?.dpad.left);

            // Analogue wins outright — no ramping, the player's thumb is the ramp.
            float padThrottle = pad != null ? pad.rightTrigger.ReadValue() : 0f;
            float padBrake = pad != null ? pad.leftTrigger.ReadValue() : 0f;
            float padSteer = pad != null ? pad.leftStick.x.ReadValue() : 0f;
            bool analogue = padThrottle > 0.02f || padBrake > 0.02f || Mathf.Abs(padSteer) > 0.08f;

            bool keyThrottle = kb != null && kb.wKey.isPressed;
            bool keyBrake = kb != null && kb.sKey.isPressed;
            float keySteer = kx;

            if (analogue)
            {
                throttle = padThrottle;
                brake = padBrake;
                steer = padSteer;
            }
            else
            {
                RampPedals(keyThrottle, keyBrake, keySteer, dt);
            }

            float clutch = 0f;
            if (kb != null && kb.cKey.isPressed) clutch = 1f;
            if (pad != null) clutch = Mathf.Max(clutch, pad.buttonWest.isPressed ? 1f : 0f);

            bool up = Pressed(kb?.leftShiftKey) || Pressed(pad?.rightShoulder);
            bool down = Pressed(kb?.leftCtrlKey) || Pressed(pad?.leftShoulder);
            bool handbrake = (kb != null && kb.spaceKey.isPressed) || (pad != null && pad.buttonSouth.isPressed);
#else
            Move = new Vector2(UnityEngine.Input.GetAxisRaw("Horizontal"), UnityEngine.Input.GetAxisRaw("Vertical"));
            Look = new Vector2(UnityEngine.Input.GetAxisRaw("Mouse X"), UnityEngine.Input.GetAxisRaw("Mouse Y")) * (lookSensitivity * 10f);

            JumpPressed = UnityEngine.Input.GetKeyDown(KeyCode.Space);
            InteractPressed = UnityEngine.Input.GetKeyDown(KeyCode.E);
            MenuPressed = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
            MapPressed = UnityEngine.Input.GetKeyDown(KeyCode.M);
            RaceBookPressed = UnityEngine.Input.GetKeyDown(KeyCode.Tab);
            LightsPressed = UnityEngine.Input.GetKeyDown(KeyCode.L);
            ResetPressed = UnityEngine.Input.GetKeyDown(KeyCode.R);
            bool modePressed = UnityEngine.Input.GetKeyDown(KeyCode.G);

            RampPedals(UnityEngine.Input.GetKey(KeyCode.W),
                       UnityEngine.Input.GetKey(KeyCode.S),
                       UnityEngine.Input.GetAxisRaw("Horizontal"), dt);

            float clutch = UnityEngine.Input.GetKey(KeyCode.C) ? 1f : 0f;
            bool up = UnityEngine.Input.GetKeyDown(KeyCode.LeftShift);
            bool down = UnityEngine.Input.GetKeyDown(KeyCode.LeftControl);
            bool handbrake = UnityEngine.Input.GetKey(KeyCode.Space);
#endif
            if (LightsPressed)
            {
                lightsOn = !lightsOn;
                GameLog.Action(LogCat.Input, "Lights toggled", lightsOn ? "ON" : "OFF", this);
            }
            if (modePressed)
            {
                mode = mode == TransmissionMode.Manual ? TransmissionMode.Automatic : TransmissionMode.Manual;
                GameLog.Action(LogCat.Input, "Transmission toggled",
                               mode == TransmissionMode.Manual
                                   ? "MANUAL (LeftShift up / LeftCtrl down / C clutch)"
                                   : "AUTOMATIC", this);
            }

            Vehicle = new VehicleInput
            {
                throttle = Mathf.Clamp01(throttle),
                brake = Mathf.Clamp01(brake),
                steer = Mathf.Clamp(steer, -1f, 1f),
                clutch = clutch,
                handbrake = handbrake,
                shiftUp = up,
                shiftDown = down,
                lights = lightsOn,
                transmission = Transmission
            };

            LogPresses();
        }

        /// Rate-limited pedals. Release is always faster than application, which is
        /// both how real pedals behave and what makes keyboard driving controllable.
        private void RampPedals(bool throttleHeld, bool brakeHeld, float steerTarget, float dt)
        {
            throttle = Mathf.MoveTowards(throttle, throttleHeld ? 1f : 0f,
                                         (throttleHeld ? throttleRise : throttleFall) * dt);
            brake = Mathf.MoveTowards(brake, brakeHeld ? 1f : 0f,
                                      (brakeHeld ? brakeRise : brakeFall) * dt);

            bool steering = Mathf.Abs(steerTarget) > 0.01f;
            float rate = steering ? steerRise : steerFall;
            rate *= Mathf.Lerp(1f, 1f - steerSpeedDamping, lastKnownSpeed01);
            steer = Mathf.MoveTowards(steer, steerTarget, rate * dt);
        }

        /// Let the HUD or vehicle entry feed speed back so the steer ramp can slow at
        /// pace. Optional — leave it unwired and you get the low-speed rate everywhere.
        public void ReportSpeed01(float speed01) => lastKnownSpeed01 = Mathf.Clamp01(speed01);

#if ENABLE_INPUT_SYSTEM
        private static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl c)
            => c != null && c.wasPressedThisFrame;
#endif

        public void Clear()
        {
            GameLog.Verbose(LogCat.Input, "Input cleared (menu took focus)", this);
            Move = Vector2.zero; Look = Vector2.zero; Vehicle = default;
            throttle = brake = steer = 0f;
            ResetPressed = false;
        }

        private void LogPresses()
        {
            if (!logKeyPresses) return;
            if (InteractPressed) GameLog.Verbose(LogCat.Input, "Key: E (interact / exit car)", this);
            if (MenuPressed) GameLog.Verbose(LogCat.Input, "Key: Escape (menu)", this);
            if (MapPressed) GameLog.Verbose(LogCat.Input, "Key: M (map)", this);
            if (RaceBookPressed) GameLog.Verbose(LogCat.Input, "Key: Tab (race book)", this);
            if (JumpPressed) GameLog.Verbose(LogCat.Input, "Key: Space (jump / handbrake)", this);
            if (ResetPressed) GameLog.Verbose(LogCat.Input, "Key: R (reset car)", this);
        }
    }
}