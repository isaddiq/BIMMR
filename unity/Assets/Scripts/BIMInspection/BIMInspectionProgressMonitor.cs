using System.Collections.Generic;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Computes aggregate progress statistics from inspection records and an
    /// optional set of "tracked elements" (the BIM elements that COULD be
    /// inspected, used to compute total / not-inspected counts).
    /// </summary>
    public class BIMInspectionProgressMonitor : MonoBehaviour
    {
        /// <summary>Build a snapshot from the current state.</summary>
        /// <param name="records">All inspection records, keyed by stable id.</param>
        /// <param name="totalTrackedElements">Total inspectable elements in the
        ///   scene. If &lt;= 0, the record count is used as the total.</param>
        /// <param name="trackedCategoryCounts">Optional per-category totals
        ///   (category -> count). Helps display "X / Y inspected in Walls".</param>
        /// <param name="trackedLevelCounts">Optional per-level totals.</param>
        public InspectionProgressSummary BuildSummary(
            Dictionary<string, InspectionRecord> records,
            int totalTrackedElements,
            Dictionary<string, int> trackedCategoryCounts = null,
            Dictionary<string, int> trackedLevelCounts = null)
        {
            var s = new InspectionProgressSummary();
            if (records == null) records = new Dictionary<string, InspectionRecord>();

            // Per-status counters.
            int inspected = 0, passed = 0, failed = 0, review = 0;
            foreach (var r in records.Values)
            {
                if (r == null) continue;
                switch (r.status)
                {
                    case InspectionStatus.Passed:      inspected++; passed++; break;
                    case InspectionStatus.Failed:      inspected++; failed++; break;
                    case InspectionStatus.NeedsReview: inspected++; review++; break;
                    case InspectionStatus.InProgress:  inspected++;           break;
                }

                AccumulateCategory(s.progressByCategory, r);
                AccumulateLevel   (s.progressByLevel,    r);
            }

            int total = totalTrackedElements > 0
                ? totalTrackedElements
                : Mathf.Max(records.Count, 0);

            s.totalElements         = total;
            s.inspectedElements     = inspected;
            s.passedElements        = passed;
            s.failedElements        = failed;
            s.needsReviewElements   = review;
            s.notInspectedElements  = Mathf.Max(0, total - inspected);
            s.inspectedPercentage   = total > 0 ? (inspected * 100f) / total : 0f;
            s.passedPercentage      = total > 0 ? (passed    * 100f) / total : 0f;

            // Apply optional totals to category / level buckets.
            if (trackedCategoryCounts != null)
            {
                foreach (var kv in trackedCategoryCounts)
                {
                    var bucket = GetOrCreateCategory(s.progressByCategory, kv.Key);
                    bucket.total = kv.Value;
                }
            }
            if (trackedLevelCounts != null)
            {
                foreach (var kv in trackedLevelCounts)
                {
                    var bucket = GetOrCreateLevel(s.progressByLevel, kv.Key);
                    bucket.total = kv.Value;
                }
            }

            return s;
        }

        // --- helpers --------------------------------------------------------

        private static CategoryProgress GetOrCreateCategory(
            Dictionary<string, CategoryProgress> map, string key)
        {
            if (string.IsNullOrEmpty(key)) key = "Uncategorized";
            if (!map.TryGetValue(key, out var p))
            {
                p = new CategoryProgress { category = key };
                map[key] = p;
            }
            return p;
        }

        private static LevelProgress GetOrCreateLevel(
            Dictionary<string, LevelProgress> map, string key)
        {
            if (string.IsNullOrEmpty(key)) key = "Unknown";
            if (!map.TryGetValue(key, out var p))
            {
                p = new LevelProgress { level = key };
                map[key] = p;
            }
            return p;
        }

        private static void AccumulateCategory(Dictionary<string, CategoryProgress> map,
                                               InspectionRecord r)
        {
            var p = GetOrCreateCategory(map, r.category);
            // total is derived from tracked counts (set after the loop) or
            // inferred from records when no tracked counts were provided.
            if (p.total < p.inspected + 1) p.total++;
            switch (r.status)
            {
                case InspectionStatus.Passed:      p.inspected++; p.passed++; break;
                case InspectionStatus.Failed:      p.inspected++; p.failed++; break;
                case InspectionStatus.NeedsReview: p.inspected++; p.needsReview++; break;
                case InspectionStatus.InProgress:  p.inspected++;              break;
            }
        }

        private static void AccumulateLevel(Dictionary<string, LevelProgress> map,
                                            InspectionRecord r)
        {
            var p = GetOrCreateLevel(map, r.level);
            if (p.total < p.inspected + 1) p.total++;
            switch (r.status)
            {
                case InspectionStatus.Passed:      p.inspected++; p.passed++; break;
                case InspectionStatus.Failed:      p.inspected++; p.failed++; break;
                case InspectionStatus.NeedsReview: p.inspected++; p.needsReview++; break;
                case InspectionStatus.InProgress:  p.inspected++;              break;
            }
        }
    }
}
