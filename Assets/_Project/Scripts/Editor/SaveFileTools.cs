using System.IO;
using UnityEditor;
using UnityEngine;

namespace RallyGame.Core.EditorTools
{
    /// Save file plumbing, reachable without entering play mode or hunting through
    /// AppData. persistentDataPath depends on the Company and Product names in Player
    /// Settings, so the folder is never quite where you expect it — better to ask
    /// Unity than to go looking.
    ///
    /// Keep this in an Editor folder, or wrap it in #if UNITY_EDITOR: UnityEditor
    /// does not exist in a build.
    public static class SaveFileTools
    {
        /// Must match SaveManager's fileName field.
        private const string FileName = "rally_save.json";

        private static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        [MenuItem("Rally/Save/Print Save Path")]
        private static void PrintPath()
        {
            Debug.Log($"Save path: {Path}\nExists: {File.Exists(Path)}");
        }

        [MenuItem("Rally/Save/Reveal Save Folder")]
        private static void Reveal()
        {
            // Trailing separator: RevealInFinder opens the PARENT of whatever you hand
            // it, so passing the bare folder would land you one level too high.
            EditorUtility.RevealInFinder(Application.persistentDataPath + System.IO.Path.DirectorySeparatorChar);
        }

        [MenuItem("Rally/Save/Delete Save File")]
        private static void Delete()
        {
            if (!File.Exists(Path))
            {
                Debug.Log($"No save file to delete at {Path}");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Delete save file?",
                    $"This permanently deletes:\n\n{Path}\n\nThe next run starts a new game.",
                    "Delete", "Cancel"))
                return;

            File.Delete(Path);
            Debug.Log($"Save file deleted: {Path}");
        }

        /// Dumps the raw JSON to the console. Useful for answering "is the bad ID
        /// actually in here, and where" before deleting anything.
        [MenuItem("Rally/Save/Print Save Contents")]
        private static void PrintContents()
        {
            if (!File.Exists(Path)) { Debug.Log($"No save file at {Path}"); return; }
            Debug.Log(File.ReadAllText(Path));
        }

        [MenuItem("Rally/Save/Delete Save File", true)]
        [MenuItem("Rally/Save/Print Save Contents", true)]
        private static bool HasSave() => File.Exists(Path);
    }
}