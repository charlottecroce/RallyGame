using UnityEngine;
using RallyGame.Core;
using RallyGame.Input;

namespace RallyGame.Player
{
    /// On-foot movement. Disabled wholesale while driving or in menus.
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

        private CharacterController cc;
        private float pitch;
        private float verticalSpeed;

        private void Awake() => cc = GetComponent<CharacterController>();

        // Cursor state is owned by the pause menu, not by enabling/disabling movement.
        private void OnEnable() { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        private void Update()
        {
            if (inputLocked && inputLocked.Value) return;

            // Look
            transform.Rotate(Vector3.up, input.Look.x, Space.Self);
            pitch = Mathf.Clamp(pitch - input.Look.y, -pitchLimit, pitchLimit);
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // Move
            Vector3 planar = (transform.right * input.Move.x + transform.forward * input.Move.y);
            if (planar.sqrMagnitude > 1f) planar.Normalize();

            if (cc.isGrounded)
            {
                verticalSpeed = -1f;
                if (input.JumpPressed) verticalSpeed = jumpSpeed;
            }
            verticalSpeed += gravity * Time.deltaTime;

            cc.Move((planar * walkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        /// Used when leaving the car so the player does not spawn inside geometry.
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            pitch = 0f;
            cc.enabled = true;
        }
    }
}
