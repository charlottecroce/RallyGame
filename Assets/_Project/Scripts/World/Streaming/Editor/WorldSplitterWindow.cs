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
    /// on an unsaved scene and every step is separate: scan, split, prepare. Read the
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
        private int parentedCount;
        private string status = "Press Scan.";
        private Vector2 scroll;

        [MenuItem("Window/Rally/World Splitter")]
        private static void Open() => GetWindow<WorldSplitterWindow>("World Splitter");

        /// A stuck progress bar means an exception ate the ClearProgressBar call. This
        /// gets the editor back without restarting it.
        [MenuItem("Window/Rally/Clear Stuck Progress Bar")]
        private static void ClearBar() => EditorUtility.ClearProgressBar();

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
            EditorGUILayout.LabelField("Step 1 — scan", EditorStyles.boldLabel);
            if (GUILayout.Button("Scan Open Scenes")) Scan();
            EditorGUILayout.HelpBox(status, MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Step 2 — split", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates one scene per terrain tile, moves that terrain into it, saves it and closes " +
                "it again — so only one cell is ever open and the cost stays flat across 81 tiles. " +
                "Terrains under a group object are unparented first; only a scene root can move " +
                "between scenes. COMMIT TO SOURCE CONTROL FIRST.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(found.Count == 0 || cells == null))
                if (GUILayout.Button("Split Into Cell Scenes", GUILayout.Height(30))) Split();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Step 3 — stitch the tiles", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Run AFTER splitting. Opens every cell in turn, turns on Auto Connect, sets one " +
                "grouping ID, and SAVES — a terrain whose scene was closed during an earlier pass " +
                "never got the flag, which is what leaves cracks between tiles at distance.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(cells == null || cells.Count == 0))
            {
                if (GUILayout.Button("Prepare All Cells (open, set, save, close)", GUILayout.Height(26)))
                    PrepareAllCells();
                if (GUILayout.Button("Verify Grid Alignment")) VerifyAlignment(false);
                if (GUILayout.Button("Snap Terrains To Grid")) VerifyAlignment(true);
            }

            if (GUILayout.Button("Prepare Terrains In Open Scenes Only")) PrepareOpen(true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Daily tools", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(cells == null))
            {
                if (GUILayout.Button("Open All Cells (slow)")) OpenCells(-1);
                if (GUILayout.Button("Open Cells Around Selection (3x3)")) OpenCells(1);
                if (GUILayout.Button("Open Cells Around Selection (5x5)")) OpenCells(2);
                if (GUILayout.Button("Close All Cells")) CloseCells();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recovery", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Undoes a bad split: open every cell scene, press Gather, and all terrains come back " +
                "to the active scene as roots. Then delete the cell scene files and split again.",
                MessageType.None);
            if (GUILayout.Button("Gather All Terrains Into Active Scene")) Gather();

            EditorGUILayout.EndScrollView();
        }

        // ---- stitching -----------------------------------------------------

        /// The reliable version: walks every cell scene whether or not it is open, so
        /// no tile is missed, and saves each one.
        private void PrepareAllCells()
        {
            int done = 0, touched = 0;

            try
            {
                foreach (var cell in cells.cells)
                {
                    EditorUtility.DisplayProgressBar("Stitching tiles", cell.sceneName,
                                                     (float)done++ / cells.Count);

                    bool wasOpen = SceneManager.GetSceneByName(cell.sceneName).isLoaded;
                    var scene = wasOpen
                        ? SceneManager.GetSceneByName(cell.sceneName)
                        : EditorSceneManager.OpenScene(cell.scenePath, OpenSceneMode.Additive);

                    foreach (var root in scene.GetRootGameObjects())
                    foreach (var terrain in root.GetComponentsInChildren<Terrain>())
                    {
                        terrain.allowAutoConnect = true;
                        terrain.groupingID = groupingId;
                        EditorUtility.SetDirty(terrain);
                        touched++;
                    }

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Terrain.SetConnectivityDirty();
            status = $"Stitched {touched} terrain(s) across {cells.Count} cell(s). " +
                     "Now run Verify Grid Alignment.";
            Debug.Log($"[WorldSplitter] {status}");
        }

        private void PrepareOpen(bool connect)
        {
            var terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            var scenes = new HashSet<Scene>();

            foreach (var terrain in terrains)
            {
                terrain.allowAutoConnect = connect;
                terrain.groupingID = groupingId;
                EditorUtility.SetDirty(terrain);
                scenes.Add(terrain.gameObject.scene);
            }

            // Marking the scene, not just the component: a component-only dirty flag can
            // be dropped when the scene closes, and the flag silently reverts.
            foreach (var scene in scenes)
                if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Terrain.SetConnectivityDirty();
            status = $"{(connect ? "Connected" : "Disconnected")} {terrains.Length} terrain(s) " +
                     $"in {scenes.Count} open scene(s). Save to keep it.";
        }

        /// Auto-connect finds neighbours by position. A tile a fraction of a metre off
        /// the grid connects to nothing, and cracks open along that whole edge.
        private void VerifyAlignment(bool snap)
        {
            float size = cells.cellSize;
            var origin = cells.origin;
            int checkedTiles = 0, offenders = 0, fixedTiles = 0;
            var report = new System.Text.StringBuilder();

            try
            {
                int done = 0;
                foreach (var cell in cells.cells)
                {
                    EditorUtility.DisplayProgressBar("Checking grid", cell.sceneName,
                                                     (float)done++ / cells.Count);

                    bool wasOpen = SceneManager.GetSceneByName(cell.sceneName).isLoaded;
                    var scene = wasOpen
                        ? SceneManager.GetSceneByName(cell.sceneName)
                        : EditorSceneManager.OpenScene(cell.scenePath, OpenSceneMode.Additive);

                    bool dirty = false;

                    foreach (var root in scene.GetRootGameObjects())
                    foreach (var terrain in root.GetComponentsInChildren<Terrain>())
                    {
                        checkedTiles++;
                        Vector3 p = terrain.transform.position;
                        Vector3 want = new Vector3(
                            origin.x + Mathf.Round((p.x - origin.x) / size) * size,
                            p.y,
                            origin.z + Mathf.Round((p.z - origin.z) / size) * size);

                        float off = Mathf.Max(Mathf.Abs(p.x - want.x), Mathf.Abs(p.z - want.z));
                        if (off <= 0.001f) continue;

                        offenders++;
                        report.AppendLine($"  {cell.sceneName}: off grid by {off:0.###} m");

                        if (!snap) continue;
                        terrain.transform.position = want;
                        dirty = true;
                        fixedTiles++;
                    }

                    if (dirty)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                    if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Terrain.SetConnectivityDirty();

            status = offenders == 0
                ? $"All {checkedTiles} tile(s) sit exactly on the {size:0.#} m grid."
                : snap
                    ? $"Snapped {fixedTiles} of {checkedTiles} tile(s) onto the grid."
                    : $"{offenders} of {checkedTiles} tile(s) are off the grid — press Snap.\n{report}";

            Debug.Log($"[WorldSplitter] {status}");
        }

        // ---- steps ---------------------------------------------------------

        private void Scan()
        {
            found.Clear();
            found.AddRange(FindObjectsByType<Terrain>(FindObjectsSortMode.None));

            if (found.Count == 0) { status = "No terrains in the open scenes."; return; }

            detectedSize = found[0].terrainData.size.x;
            float minX = float.MaxValue, minZ = float.MaxValue;
            int odd = 0;
            parentedCount = 0;

            foreach (var t in found)
            {
                var size = t.terrainData.size;
                if (Mathf.Abs(size.x - detectedSize) > 0.01f || Mathf.Abs(size.z - detectedSize) > 0.01f) odd++;
                if (t.transform.parent != null) parentedCount++;
                minX = Mathf.Min(minX, t.transform.position.x);
                minZ = Mathf.Min(minZ, t.transform.position.z);
            }

            detectedOrigin = new Vector3(minX, 0f, minZ);

            status = $"{found.Count} terrain(s), tile size {detectedSize:0.#} m, " +
                     $"origin ({minX:0.#}, {minZ:0.#}).";

            if (odd > 0)
                status += $"\n{odd} tile(s) are a different size — fix those first, the grid assumes one size.";
            if (parentedCount > 0)
                status += $"\n{parentedCount} terrain(s) sit under a group object and will be unparented " +
                          "by the split. World positions are preserved.";

            var occupied = new Dictionary<long, string>();
            foreach (var t in found)
            {
                var c = Coord(t.transform.position);
                long key = Key(c);
                if (occupied.TryGetValue(key, out string other))
                    status += $"\nCell {c.x},{c.y}: '{t.name}' overlaps '{other}' — they will share a scene.";
                else occupied[key] = t.name;
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

            // Auto-connect re-links every loaded terrain each time one moves scene.
            // With 81 tiles that is the difference between seconds and forever.
            PrepareOpen(false);

            int made = 0, moved = 0;
            bool cancelled = false;

            try
            {
                var paths = new Dictionary<long, string>();

                // Phase 1: one cell at a time — create, move, save, close. Never more
                // than one extra scene open, so the cost per tile stays flat.
                for (int i = 0; i < found.Count; i++)
                {
                    var terrain = found[i];
                    if (terrain == null) continue;

                    var coord = Coord(terrain.transform.position);
                    long key = Key(coord);
                    string sceneName = $"{cellPrefix}_{coord.x:00}_{coord.y:00}";
                    string path = $"{outputFolder}/{sceneName}.unity";

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Splitting world", $"{sceneName}  ({i + 1}/{found.Count})",
                            (float)i / found.Count))
                    { cancelled = true; break; }

                    if (terrain.transform.parent != null)
                        terrain.transform.SetParent(null, true);      // keep the world position

                    Scene scene;
                    if (paths.ContainsKey(key))
                        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    else
                    {
                        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                        paths[key] = path;
                        made++;

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

                    SceneManager.MoveGameObjectToScene(terrain.gameObject, scene);
                    EditorSceneManager.SaveScene(scene, path);
                    EditorSceneManager.CloseScene(scene, true);
                }

                if (!cancelled && moveLooseObjects)
                    moved = FileLooseObjects(source, paths);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            cells.Invalidate();
            EditorUtility.SetDirty(cells);
            AssetDatabase.SaveAssets();

            EditorSceneManager.SaveOpenScenes();
            if (addToBuildSettings) RegisterScenes(source);

            status = cancelled
                ? $"Cancelled after {made} cell(s). Cell scenes written so far are valid; " +
                  "gather and start again, or split the remainder."
                : $"Split {found.Count} terrain(s) into {made} cell scene(s); moved {moved} loose object(s). " +
                  $"'{source.name}' is now your always-loaded scene. Now run Step 3.";

            Debug.Log($"[WorldSplitter] {status}");
        }

        /// Root objects that are plainly part of the scenery get filed into the tile
        /// they stand over. Anything that has to survive streaming — the player, the
        /// sun, managers, road networks — is left behind.
        private int FileLooseObjects(Scene source, Dictionary<long, string> paths)
        {
            var buckets = new Dictionary<long, List<GameObject>>();

            foreach (var go in source.GetRootGameObjects())
            {
                if (go.GetComponent<WorldPersistent>()) continue;
                if (go.GetComponentInChildren<Camera>()) continue;
                if (go.GetComponentInChildren<Light>()) continue;
                if (go.GetComponentInChildren<AudioListener>()) continue;
                if (go.GetComponentInChildren<RoadSpline>()) continue;    // roads cross tiles
                if (go.GetComponentInChildren<WorldStreamer>()) continue;
                if (go.GetComponentInChildren<Terrain>()) continue;

                long key = Key(Coord(go.transform.position));
                if (!paths.ContainsKey(key)) continue;

                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<GameObject>();
                list.Add(go);
            }

            int moved = 0, done = 0;

            foreach (var bucket in buckets)
            {
                string path = paths[bucket.Key];
                EditorUtility.DisplayProgressBar("Filing scenery", path, (float)done++ / buckets.Count);

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                foreach (var go in bucket.Value)
                {
                    SceneManager.MoveGameObjectToScene(go, scene);
                    moved++;
                }
                EditorSceneManager.SaveScene(scene);
                EditorSceneManager.CloseScene(scene, true);
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

        // ---- recovery ------------------------------------------------------

        private void Gather()
        {
            var target = SceneManager.GetActiveScene();
            var terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            int moved = 0;

            foreach (var terrain in terrains)
            {
                if (terrain.transform.parent != null)
                    terrain.transform.SetParent(null, true);

                if (terrain.gameObject.scene == target) continue;

                SceneManager.MoveGameObjectToScene(terrain.gameObject, target);
                moved++;
            }

            EditorSceneManager.MarkSceneDirty(target);
            status = $"Gathered {moved} terrain(s) into '{target.name}'. Save, close the cell scenes, " +
                     "delete their files, then scan and split again.";
        }

        // ---- daily ---------------------------------------------------------

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

        // Rounding, not flooring: terrain corners land exactly on grid multiples, and
        // a -0.0001 float error would otherwise push a tile a whole cell negative.
        private Vector2Int Coord(Vector3 world)
        {
            float size = Mathf.Max(1f, detectedSize);
            return new Vector2Int(
                Mathf.RoundToInt((world.x - detectedOrigin.x) / size),
                Mathf.RoundToInt((world.z - detectedOrigin.z) / size));
        }

        private static long Key(Vector2Int c) => ((long)c.x << 32) ^ (uint)c.y;
    }
}