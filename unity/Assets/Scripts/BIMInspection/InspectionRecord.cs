using System;
using System.Collections.Generic;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Inspection status applied to a single BIM element.
    /// </summary>
    public enum InspectionStatus
    {
        NotInspected = 0,
        InProgress = 1,
        Passed = 2,
        Failed = 3,
        NeedsReview = 4
    }

    /// <summary>
    /// Serializable per-element inspection record. Linked to a BIM element by
    /// its stable id (IFC GlobalId / Revit UniqueId / fallback runtime id).
    /// </summary>
    [Serializable]
    public class InspectionRecord
    {
        public string recordId;
        public string elementStableId;
        public string elementName;
        public string category;
        public string level;
        public InspectionStatus status;
        public List<InspectionIssue> issues = new List<InspectionIssue>();
        public List<string> comments = new List<string>();
        public string lastUpdated; // ISO-8601 UTC string for portability.
        public string inspectorName;

        public InspectionRecord()
        {
            recordId = Guid.NewGuid().ToString();
            status = InspectionStatus.NotInspected;
            lastUpdated = DateTime.UtcNow.ToString("o");
        }

        /// <summary>Touch the <c>lastUpdated</c> timestamp to now (UTC).</summary>
        public void Touch()
        {
            lastUpdated = DateTime.UtcNow.ToString("o");
        }
    }
}
