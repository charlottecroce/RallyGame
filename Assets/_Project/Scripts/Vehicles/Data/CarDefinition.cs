using System.Collections.Generic;
using UnityEngine;
using RallyGame.Parts.Data;

namespace RallyGame.Vehicles.Data
{
    public enum Drivetrain { FWD, RWD, AWD }

    /// Static template: base handling before any part is fitted.
    [CreateAssetMenu(menuName = "Rally/Definitions/Car", fileName = "Car_")]
    public class CarDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id = "Car_New";
        public string displayName = "New Car";
        public Sprite thumbnail;
        [Tooltip("Prefab with Rigidbody + CarController + CarAssembly on the root.")]
        public GameObject prefab;

        [Header("Economy")]
        public int basePrice = 4000;

        [Header("Chassis")]
        public float massKg = 1000f;
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.35f, 0f);
        public Drivetrain drivetrain = Drivetrain.FWD;

        [Header("Engine")]
        [Tooltip("Peak crank torque in Nm before part modifiers.")]
        public float peakTorqueNm = 180f;
        [Tooltip("Normalised RPM (x) -> torque multiplier (y).")]
        public AnimationCurve torqueCurve = AnimationCurve.EaseInOut(0f, 0.55f, 1f, 0.85f);
        public float idleRpm = 900f;
        public float redlineRpm = 6800f;
        [Tooltip("How fast revs chase their target. Low = heavy flywheel, lazy. High = snappy.")]
        public float engineResponse = 7f;
        [Tooltip("Retarding torque at the crank when you lift off, in Nm.")]
        public float engineBrakingNm = 60f;

        [Header("Transmission")]
        public float[] gearRatios = { 3.4f, 2.1f, 1.5f, 1.1f, 0.86f };
        public float reverseRatio = 3.2f;
        public float finalDrive = 4.1f;
        [Range(0.05f, 1.5f)] public float shiftTime = 0.35f;
        [Tooltip("AWD only: share of drive torque sent to the front axle. 0.5 = even.")]
        [Range(0f, 1f)] public float awdFrontTorqueSplit = 0.4f;

        [Header("Handling")]
        public float maxSteerAngle = 32f;
        [Tooltip("Steer angle multiplier at top speed - lower means more stable.")]
        [Range(0.1f, 1f)] public float highSpeedSteerScale = 0.35f;
        public float brakeTorque = 2200f;
        public float handbrakeTorque = 3000f;
        [Tooltip("Share of braking done by the front axle. 0.65 is a typical road bias.")]
        [Range(0.3f, 0.9f)] public float frontBrakeBias = 0.64f;
        public float baseForwardGrip = 1.4f;
        public float baseSidewaysGrip = 1.5f;
        public float topSpeedKph = 150f;

        // ---- weight transfer -------------------------------------------------

        [Header("Weight transfer")]
        [Tooltip("Height of the centre of mass above the contact patches, metres. " +
                 "Higher = more transfer, more dive/squat/roll, easier to unsettle.")]
        public float cgHeightM = 0.52f;
        [Tooltip("Share of static weight on the front axle. FWD hatch ~0.62, mid-engine ~0.42.")]
        [Range(0.3f, 0.75f)] public float frontWeightBias = 0.58f;
        [Tooltip("Share of LATERAL transfer taken by the front axle. Raise for understeer, " +
                 "lower for a car that rotates on turn-in. This is your main balance knob.")]
        [Range(0.25f, 0.75f)] public float frontRollShare = 0.52f;
        [Tooltip("Grip coefficient vs normalised wheel load (1 = static load). " +
                 "Sub-linear: a wheel at 2x load does NOT give 2x grip, which is why " +
                 "transferring weight costs total grip and rewards being smooth.")]
        public AnimationCurve loadSensitivity = new AnimationCurve(
            new Keyframe(0f, 1.30f), new Keyframe(1f, 1f), new Keyframe(3f, 0.72f));

        [Header("Aero")]
        [Tooltip("Derive drag from topSpeedKph and current power, so upgrades actually " +
                 "raise top speed. Turn off to hand-tune aeroDrag.")]
        public bool deriveDragFromTopSpeed = true;
        [Tooltip("Newtons per (m/s)^2. Only used when deriveDragFromTopSpeed is off.")]
        public float aeroDrag = 0.45f;
        [Tooltip("Newtons of downforce per (m/s)^2. Small for a rally car - this is mostly " +
                 "here to stop the car floating over crests at speed.")]
        public float downforceCoefficient = 0.55f;
        [Range(0f, 1f)] public float aeroBalanceFront = 0.45f;

        // ---- default build ---------------------------------------------------
        // Restored: GarageState and OwnedCar both read these on first creation.

        [Header("Default build")]
        [Tooltip("Parts fitted when this car is first created, one per slot.")]
        public List<PartDefinition> defaultParts = new List<PartDefinition>();
        public TireCompound defaultTireCompound = TireCompound.Hard;

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
            cgHeightM = Mathf.Clamp(cgHeightM, 0.15f, 1.2f);
        }
    }
}