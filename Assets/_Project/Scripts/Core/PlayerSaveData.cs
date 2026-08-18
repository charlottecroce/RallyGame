using System.Collections.Generic;
using UnityEngine;
using RallyGame.Dealers;
using RallyGame.Parts.Runtime;
using RallyGame.Races.Data;
using RallyGame.Vehicles.Data;

namespace RallyGame.Core
{
    /// Everything that persists. Definition assets appear only as string IDs, since
    /// JsonUtility cannot serialize an SO reference.
    [System.Serializable]
    public class PlayerSaveData
    {
        public int saveVersion = 1;

        public float money;
        public List<OwnedCar> ownedCars = new List<OwnedCar>();
        public List<OwnedPart> allParts = new List<OwnedPart>();   // installed + garage inventory
        public string activeCarInstanceId;

        public Vector3 playerPosition;
        public Quaternion playerRotation = Quaternion.identity;
        public Vector3 carPosition;
        public Quaternion carRotation = Quaternion.identity;

        public int currentDay;
        public float currentTimeOfDay = 8f;
        public WeatherType currentWeather;

        public WeeklySchedule schedule = new WeeklySchedule();
        public List<string> completedRaceIds = new List<string>();
        public DealerStock partDealerStock = new DealerStock();
        public DealerStock carDealerStock = new DealerStock();

        public List<StageRecord> stageRecords = new List<StageRecord>();
    }

    /// Personal best per stage, for progression and bragging rights.
    [System.Serializable]
    public class StageRecord
    {
        public string stageId;
        public float bestSeconds;
        public int bestPlacement;
    }
}
