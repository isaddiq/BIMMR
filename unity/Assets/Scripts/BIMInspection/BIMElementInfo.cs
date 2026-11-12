using System;
using System.Collections.Generic;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Serializable metadata snapshot of a BIM element.
    /// Returned by <see cref="BIMElementMetadataReader"/> and consumed by the
    /// inspection manager, UI, and exporter.
    /// </summary>
    [Serializable]
    public class BIMElementInfo
    {
        // Stable identifier used to link inspection records across sessions
        // (IFC GlobalId, Revit UniqueId, etc.). Falls back to a runtime ID
        // when no persistent key is available.
        public string stableId;
        public string displayName;
        public string category;
        public string typeName;
        public string level;
        public string room;

        // All discovered properties keyed by the source name. Useful for
        // displaying full metadata in the UI and for export.
        [NonSerialized] public Dictionary<string, string> properties = new Dictionary<string, string>();

        // True when stableId was generated at runtime (instance ID) instead of
        // being read from a BIM property. Such IDs are NOT reliable across
        // sessions; the caller should warn the user.
        public bool stableIdIsFallback;

        public BIMElementInfo()
        {
        }
    }
}
