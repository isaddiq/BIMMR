using System;
using System.Collections.Generic;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Per-category breakdown of inspection progress.
    /// </summary>
    [Serializable]
    public class CategoryProgress
    {
        public string category;
        public int total;
        public int inspected;
        public int passed;
        public int failed;
        public int needsReview;
    }

    /// <summary>
    /// Per-level / per-floor breakdown of inspection progress.
    /// </summary>
    [Serializable]
    public class LevelProgress
    {
        public string level;
        public int total;
        public int inspected;
        public int passed;
        public int failed;
        public int needsReview;
    }

    /// <summary>
    /// Aggregated snapshot produced by <see cref="BIMInspectionProgressMonitor"/>.
    /// Intended for display in the UI and inclusion in exported reports.
    /// </summary>
    [Serializable]
    public class InspectionProgressSummary
    {
        public int totalElements;
        public int inspectedElements;
        public int passedElements;
        public int failedElements;
        public int needsReviewElements;
        public int notInspectedElements;
        public float inspectedPercentage;
        public float passedPercentage;

        [NonSerialized] public Dictionary<string, CategoryProgress> progressByCategory = new Dictionary<string, CategoryProgress>();
        [NonSerialized] public Dictionary<string, LevelProgress> progressByLevel = new Dictionary<string, LevelProgress>();
    }
}
