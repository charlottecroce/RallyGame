using UnityEngine;

namespace RallyGame.World.Streaming
{
    /// "Leave me where I am." Put this on anything in the mega-scene that must stay in
    /// the always-loaded scene rather than being filed into a terrain tile: the player,
    /// managers, the road networks, weather, the sun.
    ///
    /// The splitter also auto-keeps cameras, lights, audio listeners and road splines,
    /// so this is only needed for things it cannot recognise.
    public class WorldPersistent : MonoBehaviour
    {
    }
}