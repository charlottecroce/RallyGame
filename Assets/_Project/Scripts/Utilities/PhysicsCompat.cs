using UnityEngine;

namespace RallyGame.Utilities
{
    /// Rigidbody.velocity was renamed to linearVelocity in Unity 6. One shim so no
    /// gameplay file needs a version ifdef.
    public static class PhysicsCompat
    {
        public static Vector3 Velocity(this Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(this Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static void SetAngularVelocity(this Rigidbody rb, Vector3 v) => rb.angularVelocity = v;
    }
}
