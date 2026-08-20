using UnityEngine;
using RallyGame.Core;
#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
#else
using PhysMat = UnityEngine.PhysicMaterial;
#endif

namespace RallyGame.World.Roads
{
    /// One asset per kind of road. Adding a third surface (gravel, snow, cobbles)
    /// means creating an asset and dropping it in the table — no code, no enum,
    /// nothing to recompile. That is the whole point of this file.
    ///
    /// Grip here is a MULTIPLIER on the already-resolved car grip, not a replacement.
    /// Tarmac is the 1.0 reference; dirt is under it. The tire model still decides
    /// how good your tires are, this only decides what they are gripping.
    [CreateAssetMenu(menuName = "Rally/Definitions/Road Surface", fileName = "Surface_")]
    public class RoadSurface : ScriptableObject
    {
        [Header("Identity")]
        public string id = "surface_new";
        public string displayName = "New Surface";

        [Header("Look")]
        public Material material;
        [Tooltip("Texture repeats per metre along the road. 0.1 = one tile every 10 m.")]
        public float uvTilesPerMetre = 0.12f;

        [Header("Skirt")]
        [Tooltip("Material for the embankment skirt that fills the gap under the road on a camber " +
                 "or a side slope — the wall and ramp that run down to the actual ground. Leave " +
                 "empty to reuse the road material above; the skirt is still built either way, so " +
                 "there is never a hole under the road even without a dedicated texture.")]
        public Material skirtMaterial;

        [Header("Grip")]
        [Tooltip("Multiplier on resolved car grip in the dry. Tarmac is the 1.0 reference.")]
        [Range(0.2f, 1.5f)] public float dryGrip = 1f;
        [Tooltip("Multiplier in the rain. Dirt loses far more than tarmac does.")]
        [Range(0.2f, 1.5f)] public float wetGrip = 0.85f;

        [Header("Build defaults")]
        [Tooltip("Road width in metres. A RoadSpline can override this per road.")]
        public float defaultWidth = 7f;
        [Tooltip("Skirt on each side that drops to the terrain and hides the floating edge.")]
        public float shoulderWidth = 0.6f;
        [Tooltip("How far the skirt drops below the road surface. This is also the floor for the " +
                 "adaptive ground-following drop — the skirt never goes shallower than this, only deeper.")]
        public float shoulderDrop = 0.12f;
        [Tooltip("Height above the terrain the road sits. Just enough to beat z-fighting.")]
        public float heightOffset = 0.06f;

        [Header("Physics")]
        [Tooltip("Optional. Physics material for the road collider. Leave empty to inherit the default.")]
        public PhysMat physicsMaterial;

        /// Weather-aware grip multiplier. Same shape as TireCompoundTable.GripMultiplier
        /// so the two read alike at the call site.
        public float GripMultiplier(WeatherType weather)
            => weather == WeatherType.Rainy ? wetGrip : dryGrip;

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
            defaultWidth = Mathf.Max(1f, defaultWidth);
            uvTilesPerMetre = Mathf.Max(0.001f, uvTilesPerMetre);
        }
    }
}