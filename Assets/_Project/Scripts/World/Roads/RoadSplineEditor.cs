using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace RallyGame.World.Roads.EditorTools
{
    /// Buttons instead of a right-click menu. The whole road workflow is "move a knot,
    /// press Rebuild", so that button should be the biggest thing in the inspector.
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
                if (GUILayout.Button("Clear", GUILayout.Height(32), GUILayout.Width(70))) ClearAll();
            }

            Auto = EditorGUILayout.ToggleLeft(
                "Auto-rebuild when the spline changes (slow on long roads)", Auto);

            var road = (RoadSpline)target;
            if (!road.HasBake)
                EditorGUILayout.HelpBox("No mesh yet. Press Rebuild Road.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox(
                    $"{road.Centreline.Count} cross-sections, {road.Width:0.0} m wide.", MessageType.None);
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