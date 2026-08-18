using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RallyGame.Vehicles.Controllers
{
    /// Editor-time rig builder. Derives wheel centres and radii from the imported
    /// meshes, creates WheelColliders and pivot-corrected visual wrappers, and fills
    /// in CarController.wheels. Zero runtime cost - delete it once the prefab is built.
    [RequireComponent(typeof(CarController))]
    public class WheelSetupHelper : MonoBehaviour
    {
        [Header("Wheel meshes from the FBX")]
        [SerializeField] private Transform wheelFrontLeft;
        [SerializeField] private Transform wheelFrontRight;
        [SerializeField] private Transform wheelRearLeft;
        [SerializeField] private Transform wheelRearRight;

        [Header("Collider tuning")]
        [SerializeField] private float suspensionDistance = 0.2f;
        [SerializeField] private float spring = 22000f;
        [SerializeField] private float damper = 3000f;
        [SerializeField] private float targetPosition = 0.5f;
        [SerializeField] private float wheelMass = 20f;
        [Tooltip("Scales the radius measured from the mesh. Raise slightly if the car sits low.")]
        [SerializeField] private float radiusScale = 1f;

#if UNITY_EDITOR
        [ContextMenu("Build Wheel Rig")]
        private void Build()
        {
            if (!wheelFrontLeft || !wheelFrontRight || !wheelRearLeft || !wheelRearRight)
            { Debug.LogError("[WheelSetup] Assign all four wheel meshes first."); return; }

            var colliderRoot = EnsureChild("WheelColliders");
            var visualRoot = EnsureChild("WheelVisuals");

            // Order matters: axles must be paired for the anti-roll bar (0/1 front, 2/3 rear).
            var wheels = new CarController.Wheel[4];
            wheels[0] = BuildWheel("FL", wheelFrontLeft, colliderRoot, visualRoot, steers: true, handbraked: false);
            wheels[1] = BuildWheel("FR", wheelFrontRight, colliderRoot, visualRoot, steers: true, handbraked: false);
            wheels[2] = BuildWheel("RL", wheelRearLeft, colliderRoot, visualRoot, steers: false, handbraked: true);
            wheels[3] = BuildWheel("RR", wheelRearRight, colliderRoot, visualRoot, steers: false, handbraked: true);

            WriteToController(wheels);
            Debug.Log($"[WheelSetup] Built 4 wheels. Radius FL={wheels[0].collider.radius:0.000}m. " +
                      "Check the green gizmo circles match the tires, then remove this component.");
        }

        private CarController.Wheel BuildWheel(string tag, Transform mesh, Transform colliderRoot,
                                               Transform visualRoot, bool steers, bool handbraked)
        {
            var bounds = WorldBounds(mesh);
            float radius = Radius(bounds) * radiusScale;

            // Collider sits at the measured wheel centre, aligned to the car body.
            var wc = new GameObject($"WC_{tag}");
            Undo.RegisterCreatedObjectUndo(wc, "Build Wheel Rig");
            wc.transform.SetParent(colliderRoot);
            wc.transform.SetPositionAndRotation(bounds.center, transform.rotation);

            var col = wc.AddComponent<WheelCollider>();
            col.radius = radius;
            col.suspensionDistance = suspensionDistance;
            col.mass = wheelMass;
            col.forceAppPointDistance = 0f;
            var s = col.suspensionSpring;
            s.spring = spring; s.damper = damper; s.targetPosition = targetPosition;
            col.suspensionSpring = s;

            // Wrapper re-centres the pivot: the mesh keeps its world pose but now
            // rotates about the wheel centre instead of the FBX origin.
            var wv = new GameObject($"WV_{tag}");
            Undo.RegisterCreatedObjectUndo(wv, "Build Wheel Rig");
            wv.transform.SetParent(visualRoot);
            wv.transform.SetPositionAndRotation(bounds.center, transform.rotation);
            Undo.SetTransformParent(mesh, wv.transform, "Build Wheel Rig");

            return new CarController.Wheel { collider = col, visual = wv.transform, steers = steers, handbraked = handbraked };
        }

        /// Fills the private serialized array without needing a public setter.
        private void WriteToController(CarController.Wheel[] wheels)
        {
            var so = new SerializedObject(GetComponent<CarController>());
            var array = so.FindProperty("wheels");
            array.arraySize = wheels.Length;

            for (int i = 0; i < wheels.Length; i++)
            {
                var e = array.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("collider").objectReferenceValue = wheels[i].collider;
                e.FindPropertyRelative("visual").objectReferenceValue = wheels[i].visual;
                e.FindPropertyRelative("steers").boolValue = wheels[i].steers;
                e.FindPropertyRelative("handbraked").boolValue = wheels[i].handbraked;
            }
            so.ApplyModifiedProperties();
        }

        private Transform EnsureChild(string name)
        {
            var existing = transform.Find(name);
            if (existing) return existing;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Build Wheel Rig");
            go.transform.SetParent(transform, false);
            return go.transform;
        }
#endif

        /// Combined renderer bounds - works whether the wheel is one mesh or several.
        private static Bounds WorldBounds(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(t.position, Vector3.one * 0.3f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        /// Thinnest axis is the axle; radius is the mean of the other two extents.
        private static float Radius(Bounds b)
        {
            var e = b.extents;
            float axle = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
            return (e.x + e.y + e.z - axle) * 0.5f;
        }
    }
}