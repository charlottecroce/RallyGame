using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using RallyGame.Core;

namespace RallyGame.World.Streaming
{
    /// Keeps a square of terrain cells loaded around the player and throws the rest
    /// away. Lives in the always-loaded scene.
    ///
    /// Two rules make this behave rather than thrash:
    ///   - Load radius is smaller than unload radius. A cell you just loaded is not
    ///     immediately eligible for unloading, so driving back and forth across a tile
    ///     border does not load-unload-load the same 40 MB of heightmap.
    ///   - One operation at a time, in a queue. Unity cannot cancel an async load or
    ///     unload a scene that has not finished loading, so overlapping requests are a
    ///     reliable way to produce a hang.
    ///
    /// Cells already open in the editor when you press Play are adopted, not
    /// re-loaded — multi-scene editing and this component do not fight.
    public class WorldStreamer : MonoBehaviour
    {
        [Header("World")]
        [SerializeField] private WorldCells cells;
        [Tooltip("Usually the car. Left empty, the tagged object is found at startup.")]
        [SerializeField] private Transform target;
        [SerializeField] private string targetTag = "Player";

        [Header("Radius (in tiles)")]
        [Tooltip("Cells this close are kept loaded. 1 = a 3x3 block, 2 = 5x5.")]
        [SerializeField] private int loadRadius = 2;
        [Tooltip("Cells further than this are unloaded. Must be greater than Load Radius.")]
        [SerializeField] private int unloadRadius = 3;

        [Header("Pacing")]
        [Tooltip("How often the desired set is recomputed when nothing is in flight.")]
        [SerializeField] private float checkInterval = 0.25f;
        [Tooltip("Hold a finished load until the next frame before activating it. Costs a little " +
                 "latency, spreads the activation spike.")]
        [SerializeField] private bool deferActivation = true;
        [Tooltip("Release unreferenced assets after this many unloads. 0 = never. This is a " +
                 "stall, so keep it high.")]
        [SerializeField] private int unloadsPerAssetSweep = 4;

        [Header("Debug")]
        [SerializeField] private bool logging = true;

        private readonly HashSet<string> loaded = new HashSet<string>();
        private readonly List<WorldCell> scratch = new List<WorldCell>();
        private readonly HashSet<string> desired = new HashSet<string>();

        private int unloadsSinceSweep;
        private bool busy;

        /// True when the ring around the target is fully loaded and nothing is queued.
        public bool Settled => !busy && desired.Count > 0 && desired.Count == CountLoadedOfDesired();
        public int LoadedCount => loaded.Count;

        private void Start()
        {
            if (cells == null)
            {
                GameLog.Error(LogCat.World, "World streamer has no WorldCells asset — nothing will stream.", this);
                enabled = false;
                return;
            }

            if (unloadRadius <= loadRadius)
            {
                unloadRadius = loadRadius + 1;
                GameLog.Warn(LogCat.World, "Unload radius must exceed load radius; bumped it by one.", this);
            }

            AdoptOpenScenes();
            StartCoroutine(Pump());
        }

        /// Scenes already open (editor multi-scene play, or the boot scene setup) are
        /// counted as loaded so they are never loaded twice.
        private void AdoptOpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var cell in cells.cells)
                    if (cell.sceneName == scene.name) { loaded.Add(scene.name); break; }
            }

            if (logging && loaded.Count > 0)
                GameLog.Verbose(LogCat.World, $"World streamer adopted {loaded.Count} open cell(s)", this);
        }

        private IEnumerator Pump()
        {
            var wait = new WaitForSeconds(Mathf.Max(0.05f, checkInterval));

            while (true)
            {
                if (!Resolve()) { yield return wait; continue; }

                Refresh();

                string next = NextToLoad();
                if (next != null) { yield return Load(next); continue; }

                string stale = NextToUnload();
                if (stale != null) { yield return Unload(stale); continue; }

                yield return wait;
            }
        }

        private bool Resolve()
        {
            if (target) return true;
            if (string.IsNullOrEmpty(targetTag)) return false;

            var found = GameObject.FindGameObjectWithTag(targetTag);
            if (found) target = found.transform;
            return target != null;
        }

        private void Refresh()
        {
            desired.Clear();
            cells.Around(target.position, loadRadius, scratch);
            foreach (var cell in scratch) desired.Add(cell.sceneName);
        }

        /// Nearest missing cell first, so the ground under the player arrives before
        /// the horizon does.
        private string NextToLoad()
        {
            string best = null;
            int bestRing = int.MaxValue;

            foreach (var cell in scratch)
            {
                if (loaded.Contains(cell.sceneName)) continue;

                int ring = cells.RingDistance(target.position, cell);
                if (ring >= bestRing) continue;

                bestRing = ring;
                best = cell.sceneName;
            }

            return best;
        }

        private string NextToUnload()
        {
            foreach (string name in loaded)
            {
                if (desired.Contains(name)) continue;

                foreach (var cell in cells.cells)
                {
                    if (cell.sceneName != name) continue;
                    if (cells.RingDistance(target.position, cell) > unloadRadius) return name;
                    break;
                }
            }

            return null;
        }

        private IEnumerator Load(string sceneName)
        {
            busy = true;

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                GameLog.Error(LogCat.World,
                    $"Cell scene '{sceneName}' could not be loaded. Is it in Build Settings?", this);
                loaded.Add(sceneName);          // do not retry it every frame
                busy = false;
                yield break;
            }

            if (deferActivation)
            {
                op.allowSceneActivation = false;
                while (op.progress < 0.9f) yield return null;
                yield return null;              // let the frame that finished loading breathe
                op.allowSceneActivation = true;
            }

            while (!op.isDone) yield return null;

            loaded.Add(sceneName);
            Terrain.SetConnectivityDirty();     // re-stitch tile LOD across the new neighbour

            if (logging) GameLog.Verbose(LogCat.World, $"Cell loaded: {sceneName} ({loaded.Count} open)", this);
            busy = false;
        }

        private IEnumerator Unload(string sceneName)
        {
            busy = true;

            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(scene);
                while (op != null && !op.isDone) yield return null;
            }

            loaded.Remove(sceneName);
            Terrain.SetConnectivityDirty();

            if (unloadsPerAssetSweep > 0 && ++unloadsSinceSweep >= unloadsPerAssetSweep)
            {
                unloadsSinceSweep = 0;
                var sweep = Resources.UnloadUnusedAssets();   // this is where heightmaps actually free
                while (!sweep.isDone) yield return null;
            }

            if (logging) GameLog.Verbose(LogCat.World, $"Cell unloaded: {sceneName} ({loaded.Count} open)", this);
            busy = false;
        }

        private int CountLoadedOfDesired()
        {
            int n = 0;
            foreach (string name in desired) if (loaded.Contains(name)) n++;
            return n;
        }

        // ---- teleports -----------------------------------------------------

        /// Yield on this before dropping the car somewhere far away. Without it the car
        /// spawns above an unloaded tile, finds no collider, and falls through the
        /// world — the classic streaming bug.
        public IEnumerator EnsureLoaded(Vector3 position)
        {
            if (cells == null) yield break;

            cells.Around(position, loadRadius, scratch);
            var required = new List<WorldCell>(scratch);

            foreach (var cell in required)
            {
                if (loaded.Contains(cell.sceneName)) continue;
                while (busy) yield return null;
                yield return Load(cell.sceneName);
            }
        }

        /// Cheap pre-flight check: is there ground at this position right now?
        public bool IsLoadedAt(Vector3 position)
        {
            if (cells == null) return false;
            var c = cells.CoordAt(position);
            return cells.TryGet(c.x, c.y, out var cell) && loaded.Contains(cell.sceneName);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (cells == null || !target) return;

            float s = cells.cellSize;
            foreach (var cell in cells.cells)
            {
                int ring = cells.RingDistance(target.position, cell);
                if (ring > unloadRadius + 1) continue;

                Gizmos.color = loaded.Contains(cell.sceneName)
                    ? new Color(0.3f, 1f, 0.4f, 0.5f)
                    : new Color(1f, 1f, 1f, 0.12f);

                Gizmos.DrawWireCube(cell.center, new Vector3(s, 1f, s));
            }
        }
#endif
    }
}