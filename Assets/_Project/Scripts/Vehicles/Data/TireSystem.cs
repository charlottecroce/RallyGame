using UnityEngine;
using RallyGame.Core;

namespace RallyGame.Vehicles.Data
{
    public enum TireCompound { Hard, Medium, Soft, Wet }

    /// Mounted set state. Deliberately simpler than the part/condition system:
    /// mileage-driven wear, no quality tiers, flat cost to change.
    [System.Serializable]
    public class TireState
    {
        public TireCompound compound = TireCompound.Hard;
        [Range(0f, 1f)] public float wear;   // 0 = fresh, 1 = fully worn
        public float kmDriven;

        public void Fit(TireCompound c) { compound = c; wear = 0f; kmDriven = 0f; }

        public void AccumulateWear(float km, TireCompoundTable table)
        {
            kmDriven += km;
            wear = Mathf.Clamp01(wear + km * table.Profile(compound).wearPerKm);
        }
    }
}