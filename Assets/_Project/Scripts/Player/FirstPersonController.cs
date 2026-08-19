using UnityEngine;
using RallyGame.Core;
using RallyGame.Input;

namespace RallyGame.Player
{
    /// On-foot movement. Disabled wholesale while driving or in menus.
    ///
    /// Walking and looking are per-frame and are never logged — that was the one
    /// thing explicitly excluded. What is logged: the controller being enabled or
    /// disabled, input being locked or unlocked, jumps (Verbose), and teleports.
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private BoolVariable inputLocked;   // raised by menus/race UI

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float jumpSpeed = 4.5f;
        [SerializeField] private float pitchLimit = 85f;

        [Header("Debug")]
        [Tooltip("Log jumps at Verbose. Movement is never logged either way.")]
        [SerializeField] private bool logJumps = true;

        private CharacterController cc;
        private float pitch;
        private float verticalSpeed;
        private bool lastLocked;

        private void Awake() => cc = GetComponent<CharacterController>();

        // Cursor state is owned by the pause menu, not by enabling/disabling movement.
        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            GameLog.Action(LogCat.Player, "On-foot controller ENABLED", $"at {transform.position:0.0}", this);
        }

        private void OnDisable()
        {
            GameLog.Action(LogCat.Player, "On-foot controller DISABLED", null, this);
        }

        private void Update()
        {
            bool locked = inputLocked && inputLocked.Value;
            if (locked != lastLocked)
            {
                lastLocked = locked;
                GameLog.Action(LogCat.Player,
                               locked ? "On-foot input LOCKED" : "On-foot input UNLOCKED",
                               locked ? "menu or race UI has focus" : "movement restored", this);
            }
            if (locked) return;

            // Look — per-frame, never logged.
            transform.Rotate(Vector3.up, input.Look.x, Space.Self);
            pitch = Mathf.Clamp(pitch - input.Look.y, -pitchLimit, pitchLimit);
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // Move — per-frame, never logged.
            Vector3 planar = (transform.right * input.Move.x + transform.forward * input.Move.y);
            if (planar.sqrMagnitude > 1f) planar.Normalize();

            if (cc.isGrounded)
            {
                verticalSpeed = -1f;
                if (input.JumpPressed)
                {
                    verticalSpeed = jumpSpeed;
                    if (logJumps) GameLog.Verbose(LogCat.Player, $"Jumped from {transform.position:0.0}", this);
                }
            }
            verticalSpeed += gravity * Time.deltaTime;

            cc.Move((planar * walkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        /// Used when leaving the car so the player does not spawn inside geometry.
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            GameLog.Action(LogCat.Player, "Player teleported",
                           $"{transform.position:0.0} -> {position:0.0}", this);

            cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            pitch = 0f;
            cc.enabled = true;
        }
    }
}
