using UnityEngine;

namespace RallyGame.Core
{
    [CreateAssetMenu(menuName = "Rally/Events/String Event", fileName = "Evt_Str_")]
    public class StringEvent : GameEvent<string> { }

    [CreateAssetMenu(menuName = "Rally/Events/Float Event", fileName = "Evt_Flt_")]
    public class FloatEvent : GameEvent<float> { }

    [CreateAssetMenu(menuName = "Rally/Events/Int Event", fileName = "Evt_Int_")]
    public class IntEvent : GameEvent<int> { }
}
