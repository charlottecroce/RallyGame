using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RallyGame.World.Roads;

namespace RallyGame.World.Streaming.EditorTools
{
    /// One-shot surgery plus the day-to-day cell tools. Window > Rally > World Splitter.
    ///
    /// The split is destructive and rearranges your whole scene, so it refuses to run
    /// on an unsaved scene and every step is separate: prepare, scan, split. Read the
    /// scan output before pressing Split.
    public class WorldSplitterWindow : EditorWindow
    {
        private WorldCells cells;
        private string outputFolder = "Assets/_Project/Scenes/World";
        private string cellPrefix = "Cell";
        private bool moveLooseObjects = true;
        private bool addToBuildSettings = true;
        private int groupingId = 0;

        private readonly List<Terrain> found = new List<Terrain>();
        private float detectedSize;
        private Vector3 detectedOrigin;
        private string status = "Press Scan.";
        private Vector2 scroll;

        [MenuItem("Window/Rally/World Splitter")]
        private static void Open() => GetWindow<WorldSplitterWindow>("World Splitter");

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            cells = (WorldCells)EditorGUILayout.ObjectField("World Cells", cells, typeof(WorldCells), false);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            cellPrefix = EditorGUILayout.TextField("Scene Prefix", cellPrefix);
            groupingId = EditorGUILayout.IntField("Terrain Grouping ID", groupingId);
            moveLooseObjects = EditorGUILayout.ToggleLeft(
                "Move loose root objects into the tile they stand on", moveLooseObjects);
            addToBuildSettings = EditorGUILayout.ToggleLeft("Add new scenes to Build Settings", addToBuildSettings);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Step 1 — prepare terrains", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Turns on Auto Connect and sets one grouping ID on every terrain, so tiles stitch " +
                "their edges together automatically as they stream in and out.", MessageType.None);
            if (GUILayout.Button("Prepare Terrains In Open Scenes")) PrepareTerrains();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Step 2 — scan", EditorStyles.boldLabel);
            if (GUILayout.Button("Scan Open Scenes")) Scan();
            EditorGUILayout.HelpBox(status, MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Step 3 — split", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates one scene per terrain tile and moves that terrain into it. The scene you " +
                "start from keeps everything else and becomes your always-loaded scene. " +
                "COMMIT TO SOURCE CONTROL FIRST.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(found.Count == 0 || cells == null))
                if (GUILayout.Button("Split Into Cell Scenes", GUILayout.Height(30))) Split();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Daily tools", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(cells == null))
            {
                if (GUILayout.Button("Open All Cells (slow)")) OpenCells(-1);
                if (GUILayout.Button("Open Cells Around Selection (3x3)")) OpenCells(1);
                if (GUILayout.Button("Open Cells Around Selection (5x5)")) OpenCells(2);
                if (GUILayout.Button("Close All Cells")) CloseCells();
            }

            EditorGUILayout.EndScrollView();
        }

        // ---- steps ---------------------------------------------------------

        private void PrepareTerrains()
        {
            var terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            foreach (var terrain in terrains)
            {
                Undo.RecordObject(terrain, "Prepare terrain");
                terrain.allowAutoConnect = true;
                terrain.groupingID = groupingId;
                EditorUtility.SetDirty(terrain);
            }
            Terrain.SetConnectivityDirty();
            status = $"Prepared {terrains.Length} terrain(s).";
        }

        private void Scan()
        {
            found.Clear();
            found.AddRange(FindObjectsByType<Terrain>(FindObjectsSortMode.None));

            if (found.Count == 0) { status = "No terrains in the open scenes."; return; }

            // Tile size comes from the terrain data, not from a field you can mistype.
            detectedSize = found[0].terrainData.size.x;
            float minX = float.MaxValue, minZ = float.MaxValue;
            int odd = 0;

            foreach (var t in found)
            {
                var size = t.terrainData.size;
                if (Mathf.Abs(size.x - detectedSize) > 0.01f || Mathf.Abs(size.z - detectedSize) > 0.01f) odd++;
                minX = Mathf.Min(minX, t.transform.position.x);
                minZ = Mathf.Min(minZ, t.transform.position.z);
            }

            detectedOrigin = new Vector3(minX, 0f, minZ);

            status = $"{found.Count} terrain(s), tile size {detectedSize:0.#} m, " +
                     $"origin ({minX:0.#}, {minZ:0.#}).";
            if (odd > 0) status += $"\n{odd} tile(s) are a different size — fix those first, the grid assumes one size.";

            var occupied = new HashSet<long>();
            foreach (var t in found)
            {
                var c = Coord(t.transform.position);
                if (!occupied.Add(((long)c.x << 32) ^ (uint)c.y))
                    status += $"\nTwo terrains share cell {c.x},{c.y} — they overlap.";
            }
        }

        private void Split()
        {
            var source = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(source.path))
            {
                EditorUtility.DisplayDialog("Save first", "Save the scene before splitting it.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveOpenScenes()) return;

            Directory.CreateDirectory(outputFolder);
            AssetDatabase.Refresh();

            cells.cellSize = detectedSize;
            cells.origin = detectedOrigin;
            cells.cells.Clear();

            var made = new List<Scene>();
            var byCoord = new Dictionary<long, Scene>();

            for (int i = 0; i < found.Count; i++)
            {
                var terrain = found[i];
                var coord = Coord(terrain.transform.position);
                string sceneName = $"{cellPrefix}_{coord.x:00}_{coord.y:00}";
                string path = $"{outputFolder}/{sceneName}.unity";

                EditorUtility.DisplayProgressBar("Splitting world", sceneName, (float)i / found.Count);

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.MoveGameObjectToScene(terrain.transform.root.gameObject, scene);
                EditorSceneManager.SaveScene(scene, path);

                made.Add(scene);
                byCoord[((long)coord.x << 32) ^ (uint)coord.y] = scene;

                cells.cells.Add(new WorldCell
                {
                    x = coord.x,
                    z = coord.y,
                    sceneName = sceneName,
                    scenePath = path,
                    center = terrain.transform.position +
                             new Vector3(detectedSize * 0.5f, 0f, detectedSize * 0.5f)
                });
            }

            int moved = moveLooseObjects ? FileLooseObjects(source, byCoord) : 0;

            EditorUtility.ClearProgressBar();

            cells.Invalidate();
            EditorUtility.SetDirty(cells);
            AssetDatabase.SaveAssets();

            EditorSceneManager.SaveOpenScenes();
            if (addToBuildSettings) RegisterScenes(source);

            status = $"Split into {made.Count} cell scene(s); moved {moved} loose object(s). " +
                     $"'{source.name}' is now your always-loaded scene.";
            Debug.Log($"[WorldSplitter] {status}");
        }

        /// Root objects that are plainly part of the scenery get filed into the tile
        /// they stand over. Anything that has to survive streaming — the player, the
        /// sun, managers, road networks — is left behind.
        private int FileLooseObjects(Scene source, Dictionary<long, Scene> byCoord)
        {
            int moved = 0;

            foreach (var go in source.GetRootGameObjects())
            {
                if (go.GetComponent<WorldPersistent>()) continue;
                if (go.GetComponentInChildren<Camera>()) continue;
                if (go.GetComponentInChildren<Light>()) continue;
                if (go.GetComponentInChildren<AudioListener>()) continue;
                if (go.GetComponentInChildren<RoadSpline>()) continue;    // roads cross tiles
                if (go.GetComponentInChildren<WorldStreamer>()) continue;

                var coord = Coord(go.transform.position);
                if (!byCoord.TryGetValue(((long)coord.x << 32) ^ (uint)coord.y, out var scene)) continue;

                SceneManager.MoveGameObjectToScene(go, scene);
                moved++;
            }

            return moved;
        }

        private void RegisterScenes(Scene source)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var known = new HashSet<string>();
            foreach (var s in list) known.Add(s.path);

            if (!known.Contains(source.path))
                list.Insert(0, new EditorBuildSettingsScene(source.path, true));

            foreach (var cell in cells.cells)
                if (!known.Contains(cell.scenePath))
                    list.Add(new EditorBuildSettingsScene(cell.scenePath, true));

            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---- daily ---------------------------------------------------------

        /// radius < 0 opens everything; otherwise a square around the selected object,
        /// or the scene view camera when nothing is selected.
        private void OpenCells(int radius)
        {
            if (radius >= 0 && cells.Count > 0)
            {
                Vector3 at = Selection.activeTransform
                    ? Selection.activeTransform.position
                    : (SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.pivot : Vector3.zero);

                var wanted = new List<WorldCell>();
                cells.Around(at, radius, wanted);

                foreach (var cell in wanted) OpenOne(cell);
                status = $"Opened {wanted.Count} cell(s) around ({at.x:0}, {at.z:0}).";
                return;
            }

            foreach (var cell in cells.cells) OpenOne(cell);
            status = $"Opened all {cells.Count} cell(s).";
        }

        private static void OpenOne(in WorldCell cell)
        {
            if (SceneManager.GetSceneByName(cell.sceneName).isLoaded) return;
            EditorSceneManager.OpenScene(cell.scenePath, OpenSceneMode.Additive);
        }

        private void CloseCells()
        {
            if (!EditorSceneManager.SaveOpenScenes()) return;

            int closed = 0;
            foreach (var cell in cells.cells)
            {
                var scene = SceneManager.GetSceneByName(cell.sceneName);
                if (!scene.isLoaded) continue;
                EditorSceneManager.CloseScene(scene, true);
                closed++;
            }

            status = $"Closed {closed} cell(s).";
        }

        private Vector2Int Coord(Vector3 world)
        {
            float size = Mathf.Max(1f, detectedSize);
            return new Vector2Int(
                Mathf.RoundToInt((world.x - detectedOrigin.x) / size),
                Mathf.RoundToInt((world.z - detectedOrigin.z) / size));
        }
    }
}