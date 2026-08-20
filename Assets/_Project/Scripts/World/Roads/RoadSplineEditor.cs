using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace RallyGame.World.Roads.EditorTools
{
    /// Buttons instead of a right-click menu. The whole road workflow is "move a knot,
    /// press Rebuild", so that button should be the biggest thing in the inspector.
    ///
    /// Props and clearing get their own buttons: tuning a bollard interval, or sweeping
    /// junk off the tarmac, should not cost a full mesh bake and a round trip through
    /// the asset database.
    ///
    /// Auto-rebuild is off by default: on a long road each rebuild raycasts every
    /// sample against the terrain, and doing that while dragging a knot is miserable.
    [CustomEditor(typeof(RoadSpline))]
    [CanEditMultipleObjects]
    public class RoadSplineEditor : Editor
    {
        private const string AutoKey = "RallyGame.Roads.AutoRebuild";

        private static bool Auto
        {
            get => EditorPrefs.GetBool(AutoKey, false);
            set => EditorPrefs.SetBool(AutoKey, value);
        }

        private void OnEnable() => Spline.Changed += OnSplineChanged;
        private void OnDisable() => Spline.Changed -= OnSplineChanged;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Road", GUILayout.Height(32))) RebuildAll();
                if (GUILayout.Button("Props Only", GUILayout.Height(32), GUILayout.Width(100))) RebuildPropsOnly();
                if (GUILayout.Button("Clear", GUILayout.Height(32), GUILayout.Width(70))) ClearAll();
            }

            if (GUILayout.Button("Remove Items From Road", GUILayout.Height(26))) RemoveItems();

            Auto = EditorGUILayout.ToggleLeft(
                "Auto-rebuild when the spline changes (slow on long roads)", Auto);

            var road = (RoadSpline)target;
            if (!road.HasBake)
            {
                EditorGUILayout.HelpBox("No mesh yet. Press Rebuild Road.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                $"{road.Centreline.Count} cross-sections, {road.Width:0.0} m wide.\n" +
                $"{road.Junctions.Count} junction(s) found — cones go inside them, bollards stay out.",
                MessageType.None);

            if (road.Junctions.Count == 0)
                EditorGUILayout.HelpBox(
                    "No junctions. If two strands do meet, raise Junction Join Distance — " +
                    "it has to be wider than the gap between their centrelines where they touch.",
                    MessageType.Info);
        }

        private void RebuildAll()
        {
            foreach (var t in targets)
            {
                var road = (RoadSpline)t;
                Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Rebuild Road");
                road.Rebuild();
                EditorUtility.SetDirty(road);
            }
        }

        private void RebuildPropsOnly()
        {
            foreach (var t in targets)
            {
                var road = (RoadSpline)t;
                Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Rebuild Road Props");
                road.RebuildProps();
                EditorUtility.SetDirty(road);
            }
        }

        /// Grouped under one undo entry: the pass can delete objects belonging to other
        /// hierarchies (trees, rocks), and undoing that one deletion at a time would be
        /// unusable.
        private void RemoveItems()
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove Items From Road");

            foreach (var t in targets)
            {
                var road = (RoadSpline)t;
                Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Remove Items From Road");
                road.RemoveItemsFromRoad();
                EditorUtility.SetDirty(road);
            }

            Undo.CollapseUndoOperations(group);
        }

        private void ClearAll()
        {
            foreach (var t in targets)
            {
                var road = (RoadSpline)t;
                Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Clear Road");
                road.Clear();
                EditorUtility.SetDirty(road);
            }
        }

        private void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
        {
            if (!Auto || Application.isPlaying) return;
            foreach (var t in targets) ((RoadSpline)t).Rebuild();
        }
    }
}