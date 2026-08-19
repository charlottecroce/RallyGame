using UnityEngine;

namespace RallyGame.Core
{
    [CreateAssetMenu(menuName = "Rally/Variables/Weather", fileName = "Var_Weather")]
    public class WeatherVariable : ScriptableVariable<WeatherType> { }
}