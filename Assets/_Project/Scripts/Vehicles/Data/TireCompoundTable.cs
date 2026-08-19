using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Vehicles.Data
{
    /// Balance data for all four compounds in one asset, so tuning is a single file.
    [CreateAssetMenu(menuName = "Rally/Definitions/Tire Compound Table", fileName = "TireCompounds")]
    public class TireCompoundTable : ScriptableObject
    {
        [System.Serializable]
        public class CompoundProfile
        {
            public TireCompound compound;
            [Tooltip("Grip multiplier when fresh, on dry tarmac.")]
            public float dryGrip = 1f;
            [Tooltip("Grip multiplier when fresh, in rain.")]
            public float wetGrip = 0.75f;
            [Tooltip("Wear added per km driven.")]
            public float wearPerKm = 0.004f;
            [Tooltip("Grip multiplier at 100% wear.")]
            [Range(0.1f, 1f)] public float wornGripFactor = 0.55f;
        }

        [SerializeField] private CompoundProfile[] profiles;
        [Tooltip("Flat cost of a tire change at service or mechanic.")]
        public int changeCost = 250;

        public CompoundProfile Profile(TireCompound c)
        {
            foreach (var p in profiles) if (p.compound == c) return p;
            return profiles != null && profiles.Length > 0 ? profiles[0] : new CompoundProfile();
        }

        /// Final grip multiplier fed to the wheel friction curves.
        public float GripMultiplier(TireState state, WeatherType weather)
        {
            var p = Profile(state.compound);
            float baseGrip = weather == WeatherType.Rainy ? p.wetGrip : p.dryGrip;
            return baseGrip * Mathf.Lerp(1f, p.wornGripFactor, state.wear);
        }
    }
}