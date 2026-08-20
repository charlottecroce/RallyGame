using TMPro;
using UnityEngine;
using UnityEngine.UI;
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

        [Header("Gearbox widgets")]
        [Tooltip("Optional. Filled Image; set Image Type to Filled in the inspector.")]
        [SerializeField] private Image rpmBar;
        [Tooltip("Optional. Shows AT / MT so the player knows which mode G left them in.")]
        [SerializeField] private TMP_Text transmissionLabel;
        [SerializeField] private Color rpmNormal = new Color(0.85f, 0.85f, 0.85f);
        [SerializeField] private Color rpmRedline = new Color(0.9f, 0.2f, 0.15f);
        [Range(0.6f, 1f)] [SerializeField] private float shiftLightAt = 0.88f;

        private void Update()
        {
            if (clockLabel) clockLabel.text = $"{clock.Weekday} {Format.Clock24(clock.TimeOfDay)}  {weather.Value}";
            if (moneyLabel) moneyLabel.text = Format.Money(money.Value);
            if (promptLabel) promptLabel.text = interactPrompt ? interactPrompt.Value : string.Empty;

            var car = spawner ? spawner.Current : null;
            if (car != null)
            {
                var v = car.Vehicle;
                if (speedLabel) speedLabel.text = $"{Mathf.RoundToInt(v.SpeedKph)} km/h";

                // Gear indices: -1 = R, 0 = N, 1..N forward. One shared formatter.
                if (gearLabel) gearLabel.text = Gearbox.Label(v.Gear);

                if (rpmBar)
                {
                    float n = Mathf.Clamp01(v.NormalisedRpm);
                    rpmBar.fillAmount = n;
                    rpmBar.color = n >= shiftLightAt ? rpmRedline : rpmNormal;
                }

                if (transmissionLabel)
                {
                    var cc = car.GetComponent<CarController>();
                    if (cc) transmissionLabel.text = cc.Transmission == TransmissionMode.Manual ? "MT" : "AT";
                }

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