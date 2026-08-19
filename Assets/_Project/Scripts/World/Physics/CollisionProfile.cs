using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
#else
using PhysMat = UnityEngine.PhysicMaterial;
#endif

namespace RallyGame.World.Physics
{
    /// One asset that owns every number involved in "car hits scenery". Both the car
    /// and the props read it, so the two halves of a collision can never drift apart.
    ///
    /// The stuck-on-a-tree bug is three problems wearing one coat:
    ///   1. Default physics materials have 0.6 friction. A car box wedged against a
    ///      tree capsule welds itself there - the tyres cannot out-torque the body.
    ///   2. Deep interpenetration on impact. PhysX pushes the bodies apart at up to
    ///      maxDepenetrationVelocity, which either launches the car or holds it
    ///      pinned while it fights the push.
    ///   3. The car climbs the prop collider, the wheels leave the ground, and drive
    ///      torque goes nowhere. That is why reverse does nothing.
    /// This asset fixes 1 and 2. CarUnstick handles 3.
    [CreateAssetMenu(menuName = "Rally/Definitions/Collision Profile", fileName = "CollisionProfile")]
    public class CollisionProfile : ScriptableObject
    {
        [Header("Materials")]
        [Tooltip("Applied to every non-wheel collider on the car. Low friction so the body " +
                 "slides off scenery instead of gripping it.")]
        public PhysMat carBodyMaterial;
        [Tooltip("Applied to crashable world props. Low friction, zero bounce.")]
        public PhysMat propMaterial;

        [Header("Car rigidbody")]
        [Tooltip("Cap on the speed PhysX may use to separate overlapping colliders. " +
                 "Low = no launches; too low = the car stays buried. 2-4 is sane.")]
        public float maxDepenetrationVelocity = 3f;
        [Tooltip("Higher = the solver works harder to resolve the wedge. 12 is cheap and enough.")]
        [Range(1, 32)] public int solverIterations = 12;
        [Range(1, 32)] public int solverVelocityIterations = 4;
        public CollisionDetectionMode collisionDetection = CollisionDetectionMode.ContinuousDynamic;

        [Header("Layers")]
        [Tooltip("Layer crashable props are moved onto. Leave blank to skip layer assignment.")]
        public string propLayerName = "Prop";

        public int PropLayer => string.IsNullOrEmpty(propLayerName) ? -1 : LayerMask.NameToLayer(propLayerName);
    }
}