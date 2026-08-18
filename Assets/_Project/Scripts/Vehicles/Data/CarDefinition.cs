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

        [Header("Transmission")]
        public float[] gearRatios = { 3.4f, 2.1f, 1.5f, 1.1f, 0.86f };
        public float reverseRatio = 3.2f;
        public float finalDrive = 4.1f;
        [Range(0.05f, 1.5f)] public float shiftTime = 0.35f;

        [Header("Handling")]
        public float maxSteerAngle = 32f;
        [Tooltip("Steer angle multiplier at top speed - lower means more stable.")]
        [Range(0.1f, 1f)] public float highSpeedSteerScale = 0.35f;
        public float brakeTorque = 2200f;
        public float handbrakeTorque = 3000f;
        public float baseForwardGrip = 1.4f;
        public float baseSidewaysGrip = 1.5f;
        public float topSpeedKph = 150f;

        [Header("Default build")]
        [Tooltip("Parts fitted when this car is first created, one per slot.")]
        public List<PartDefinition> defaultParts = new List<PartDefinition>();
        public TireCompound defaultTireCompound = TireCompound.Hard;

        private void OnValidate() { if (string.IsNullOrEmpty(id)) id = name; }
    }
}
