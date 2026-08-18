using TMPro;
using UnityEngine;
using RallyGame.Core;
using RallyGame.Garage;
using RallyGame.Races.Runtime;
using RallyGame.Utilities;
using RallyGame.Vehicles.Controllers;

namespace RallyGame.UI
{
    /// Reads only SO state and the spawner. Never searches for the player or car.
    public class HudView : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private GameClock clock;
        [SerializeField] private FloatVariable money;
        [SerializeField] private WeatherVariable weather;
        [SerializeField] private StringVariable interactPrompt;
        [SerializeField] private RaceState raceState;
        [SerializeField] private GarageState garage;
        [SerializeField] private CarSpawner spawner;

        [Header("Widgets")]
        [SerializeField] private TMP_Text clockLabel;
        [SerializeField] private TMP_Text moneyLabel;
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private TMP_Text speedLabel;
        [SerializeField] private TMP_Text gearLabel;
        [SerializeField] private GameObject racePanel;
        [SerializeField] private TMP_Text stageTimerLabel;
        [SerializeField] private TMP_Text checkpointLabel;
        [SerializeField] private TMP_Text serviceLabel;
        [SerializeField] private TMP_Text tireLabel;

        private void Update()
        {
            if (clockLabel) clockLabel.text = $"{clock.Weekday} {Format.Clock24(clock.TimeOfDay)}  {weather.Value}";
            if (moneyLabel) moneyLabel.text = Format.Money(money.Value);
            if (promptLabel) promptLabel.text = interactPrompt ? interactPrompt.Value : string.Empty;

            var car = spawner ? spawner.Current : null;
            if (car != null)
            {
                if (speedLabel) speedLabel.text = $"{Mathf.RoundToInt(car.Vehicle.SpeedKph)} km/h";
                if (gearLabel) gearLabel.text = car.Vehicle.Gear == 0 ? "R" : car.Vehicle.Gear.ToString();
                if (tireLabel && car.Car != null)
                    tireLabel.text = $"{car.Car.tires.compound}  {Format.Percent(1f - car.Car.tires.wear)}";
            }

            UpdateRacePanel();
        }

        private void UpdateRacePanel()
        {
            bool show = raceState.inRace;
            if (racePanel && racePanel.activeSelf != show) racePanel.SetActive(show);
            if (!show) return;

            if (stageTimerLabel)
                stageTimerLabel.text = raceState.phase == RacePhase.Countdown ? "GET READY" : Format.LapTime(raceState.stageTime);

            if (checkpointLabel)
                checkpointLabel.text = $"CP {raceState.nextCheckpoint}/{raceState.totalCheckpoints}   " +
                                       $"SS{raceState.stageIndex + 1}/{raceState.stageCount}";

            if (serviceLabel)
            {
                bool servicing = raceState.phase == RacePhase.ServiceWindow;
                serviceLabel.gameObject.SetActive(servicing);
                if (servicing) serviceLabel.text = $"SERVICE {Format.LapTime(raceState.serviceSecondsRemaining)}";
            }
        }
    }
}
