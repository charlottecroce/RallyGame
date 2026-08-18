using UnityEngine;

namespace RallyGame.Core
{
    // Concrete variable assets. Add new types here rather than subclassing per-system.
    [CreateAssetMenu(menuName = "Rally/Variables/Float", fileName = "Var_Float")]
    public class FloatVariable : ScriptableVariable<float> { }

    [CreateAssetMenu(menuName = "Rally/Variables/Int", fileName = "Var_Int")]
    public class IntVariable : ScriptableVariable<int> { }

    [CreateAssetMenu(menuName = "Rally/Variables/Bool", fileName = "Var_Bool")]
    public class BoolVariable : ScriptableVariable<bool> { }

    [CreateAssetMenu(menuName = "Rally/Variables/String", fileName = "Var_String")]
    public class StringVariable : ScriptableVariable<string> { }

    [CreateAssetMenu(menuName = "Rally/Variables/Weather", fileName = "Var_Weather")]
    public class WeatherVariable : ScriptableVariable<WeatherType> { }
}
