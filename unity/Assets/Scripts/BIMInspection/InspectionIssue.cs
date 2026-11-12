using System;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Serializable representation of a single inspection issue. An issue is
    /// always attached to an <see cref="InspectionRecord"/>, but it also stores
    /// a world position so a 3D pin can be rendered in the MR scene.
    /// </summary>
    [Serializable]
    public class InspectionIssue
    {
        public string issueId;
        public string elementStableId;
        public string issueType;   // e.g. "Geometry", "Clash", "Damage", "Missing"
        public string severity;    // e.g. "Low", "Medium", "High", "Critical"
        public string comment;
        public Vector3 worldPosition;
        public string createdAt;   // ISO-8601 UTC.
        public string screenshotPath;
        public bool resolved;

        public InspectionIssue()
        {
            issueId = Guid.NewGuid().ToString();
            createdAt = DateTime.UtcNow.ToString("o");
        }
    }
}
