using System.Collections.Generic;
using UnityEngine;

namespace RallyGame.World.Streaming
{
    /// One streamable tile: a grid coordinate and the scene that holds it.
    [System.Serializable]
    public struct WorldCell
    {
        public int x, z;
        public string sceneName;
        public string scenePath;    // editor-only convenience; the runtime loads by name
        public Vector3 center;      // world centre of the tile, for distance sorting
    }

    /// The map of the world: where every tile is and which scene it lives in.
    ///
    /// An asset rather than a scene object, for the same reason RoadNetwork is one —
    /// the streamer, the respawn code and the editor tools all need this and none of
    /// them should have to find a GameObject to get it.
    ///
    /// Filled in by the World Splitter window. Hand-editing it is possible but
    /// pointless; re-run the splitter instead.
    [CreateAssetMenu(menuName = "Rally/State/World Cells", fileName = "WorldCells")]
    public class WorldCells : ScriptableObject
    {
        [Tooltip("Width of one terrain tile in metres. Every tile must be the same size.")]
        public float cellSize = 1000f;
        [Tooltip("World position of the low corner of cell (0,0).")]
        public Vector3 origin = Vector3.zero;
        public List<WorldCell> cells = new List<WorldCell>();

        private Dictionary<long, int> index;

        public int Count => cells.Count;

        private void OnEnable() { index = null; }   // rebuild after domain reload

        /// Grid coordinate containing a world position. Valid whether or not a cell
        /// actually exists there.
        public Vector2Int CoordAt(Vector3 world)
        {
            float size = Mathf.Max(1f, cellSize);
            return new Vector2Int(
                Mathf.FloorToInt((world.x - origin.x) / size),
                Mathf.FloorToInt((world.z - origin.z) / size));
        }

        public bool TryGet(int x, int z, out WorldCell cell)
        {
            if (index == null) Build();
            if (index.TryGetValue(Key(x, z), out int i)) { cell = cells[i]; return true; }
            cell = default;
            return false;
        }

        /// Every existing cell within `radius` tiles of a position, square ring
        /// (Chebyshev) rather than circular — it matches how terrain tiles look on
        /// screen and keeps the corner tiles that a circle would drop.
        public void Around(Vector3 world, int radius, List<WorldCell> into)
        {
            into.Clear();
            var c = CoordAt(world);

            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
                if (TryGet(c.x + dx, c.y + dz, out var cell)) into.Add(cell);
        }

        /// Chebyshev distance in tiles from a world position to a cell.
        public int RingDistance(Vector3 world, in WorldCell cell)
        {
            var c = CoordAt(world);
            return Mathf.Max(Mathf.Abs(c.x - cell.x), Mathf.Abs(c.y - cell.z));
        }

        private void Build()
        {
            index = new Dictionary<long, int>(cells.Count);
            for (int i = 0; i < cells.Count; i++) index[Key(cells[i].x, cells[i].z)] = i;
        }

        public void Invalidate() => index = null;

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}