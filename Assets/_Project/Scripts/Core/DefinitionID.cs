using System;

namespace RallyGame.Core
{
    /// Shared rules for definition asset IDs.
    ///
    /// Every definition used to declare its own inline default — id = "Part_New" and
    /// friends. That default is not EMPTY, so a fill-if-empty guard in OnValidate never
    /// replaced it, and the placeholder rode into save files as though it were a real
    /// ID: dealer stock stores definitionId, OwnedPart stores definitionId. Later the
    /// asset gets a proper ID, or is deleted, and the save still says "Part_New" — a
    /// string that by then exists nowhere in the project, which is why searching for it
    /// finds nothing.
    ///
    /// The rule now: a definition either has an ID you authored, or it has none. There
    /// is no third state that looks authored but is not.
    public static class DefinitionId
    {
        /// True for the values that mean "nobody has set this yet": empty, the bare
        /// CreateAssetMenu filename prefix ("Part_"), and the legacy inline defaults
        /// ("Part_New", "Car_New", "surface_new"). Case-insensitive, so the lowercase
        /// surface convention is covered by the same check.
        public static bool IsPlaceholder(string id, string prefix)
        {
            if (string.IsNullOrWhiteSpace(id)) return true;

            string trimmed = id.Trim();
            return string.Equals(trimmed, prefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, prefix + "New", StringComparison.OrdinalIgnoreCase);
        }

        /// What OnValidate should store.
        ///
        /// An authored ID comes back untouched — renaming an asset must NEVER change its
        /// ID, because the ID is the thing saves point at. Only a placeholder is
        /// replaced, and only when the asset name is itself usable; if the asset is
        /// still called "Part_" the placeholder survives on purpose, so the audit can
        /// see it rather than a quietly invented ID standing in its place.
        public static string Resolve(string current, string assetName, string prefix)
        {
            if (!IsPlaceholder(current, prefix)) return current;
            return IsPlaceholder(assetName, prefix) ? current : assetName.Trim();
        }
    }
}