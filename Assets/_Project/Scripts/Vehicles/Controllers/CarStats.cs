using UnityEngine;
using RallyGame.Core;
using RallyGame.Parts.Data;
using RallyGame.Parts.Runtime;
using RallyGame.Vehicles.Data;

namespace RallyGame.Vehicles.Controllers
{
    /// Flattened, controller-ready numbers. Recomputed whenever fitment, condition,
    /// tires or weather change - the controller itself never touches part data.
    public struct ResolvedCarStats
    {
        public float peakTorqueNm;
        public float massKg;
        public float maxSteerAngle;
        public float steerResponse;
        public float brakeTorque;
        public float handbrakeTorque;
        public float forwardGrip;
        public float sidewaysGrip;
        public float shiftTime;
        public float suspensionStiffness;
        public float lightRange;
        public bool canStart;
        public bool overheating;
    }

    /// Pure function: definitions + owned state + world state -> stats. No MonoBehaviour,
    /// so it is unit-testable and callable from UI previews ("what if I fit this part?").
    public static class CarStatsResolver
    {
        public static ResolvedCarStats Resolve(
            OwnedCar car,
            IPartResolver parts,
            DefinitionDatabase db,
            TireCompoundTable tireTable,
            WeatherType weather)
        {
            var def = car.Definition(db);
            var s = new ResolvedCarStats
            {
                peakTorqueNm = def.peakTorqueNm,
                massKg = def.massKg,
                maxSteerAngle = def.maxSteerAngle,
                steerResponse = 1f,
                brakeTorque = def.brakeTorque,
                handbrakeTorque = def.handbrakeTorque,
                forwardGrip = def.baseForwardGrip,
                sidewaysGrip = def.baseSidewaysGrip,
                shiftTime = def.shiftTime,
                suspensionStiffness = 1f,
                lightRange = 0f,
                canStart = true,
                overheating = false
            };

            float gripFromParts = 1f;
            float turbo = 1f;
            float cooling = 1f;
            float clutch = 1f;

            foreach (var slot in (PartSlot[])System.Enum.GetValues(typeof(PartSlot)))
            {
                var part = car.PartInSlot(slot, parts, db);
                if (part == null)
                {
                    if (slot == PartSlot.Electronics || slot == PartSlot.Engine) s.canStart = false;
                    continue;
                }

                var pd = part.Definition(db);
                if (pd == null) continue;

                if (pd.requiredToStart && part.condition <= 0.01f) s.canStart = false;

                // Effectiveness blends a modifier toward neutral (1.0) as condition drops.
                float e = part.Effectiveness(db);
                float M(float value) => Mathf.Lerp(1f, value, e);

                s.peakTorqueNm *= M(pd.enginePower);
                turbo *= M(pd.turboBoost);
                cooling *= M(pd.coolingRate);
                clutch *= M(pd.clutchEfficiency);
                s.shiftTime /= Mathf.Max(0.2f, M(pd.gearShiftSpeed));
                s.suspensionStiffness *= M(pd.suspensionStiffness);
                s.steerResponse *= M(pd.steeringResponse);
                s.brakeTorque *= M(pd.brakeForce);
                gripFromParts *= M(pd.gripBonus);
                s.massKg += pd.massDelta;
                s.lightRange += pd.lightRange * e;
            }

            s.peakTorqueNm *= turbo * Mathf.Lerp(0.6f, 1f, clutch);
            s.overheating = cooling < 0.6f;
            if (s.overheating) s.peakTorqueNm *= 0.7f;   // limp mode instead of hard failure

            float tireGrip = tireTable ? tireTable.GripMultiplier(car.tires, weather) : 1f;
            s.forwardGrip *= tireGrip * gripFromParts;
            s.sidewaysGrip *= tireGrip * gripFromParts;
            s.handbrakeTorque *= Mathf.Max(0.5f, s.brakeTorque / Mathf.Max(1f, def.brakeTorque));

            return s;
        }
    }
}
