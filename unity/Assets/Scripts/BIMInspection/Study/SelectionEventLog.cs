using System;
using System.Collections.Generic;
using UnityEngine;

namespace BIMMR.Inspection.Study
{
    /// <summary>One target on the printed task sheet.</summary>
    [Serializable]
    public class TaskTarget
    {
        [Tooltip("Persistent BIM element id of the ground-truth target.")]
        public string targetId;
        [Tooltip("stud, header, window, duct, connection, ...")]
        public string targetType;
        [Tooltip("low / medium / high: elements within 0.5 m of the target, set in advance.")]
        public string densityBand = "medium";
        [Tooltip("Wording used on the task sheet, e.g. 'north exterior wall, ground level'.")]
        public string description;
    }

    /// <summary>
    /// The ground-truth target list. Two matched sets (A and B) of equal density support the
    /// within-subject disambiguation comparison (Table 11); the active set and the step
    /// pointer advance as selections are confirmed.
    /// </summary>
    public class StudyTaskSheet : MonoBehaviour
    {
        public List<TaskTarget> setA = new List<TaskTarget>();
        public List<TaskTarget> setB = new List<TaskTarget>();
        [Tooltip("A or B")]
        public string activeSet = "A";

        public int CurrentStep { get; private set; } = 1;   // 1-based
        public List<TaskTarget> Active => activeSet == "B" ? setB : setA;
        public bool HasTargets => Active != null && Active.Count > 0;
        public bool Finished => HasTargets && CurrentStep > Active.Count;

        public TaskTarget CurrentTarget =>
            HasTargets && CurrentStep >= 1 && CurrentStep <= Active.Count ? Active[CurrentStep - 1] : null;

        public void UseSet(string set)
        {
            activeSet = set == "B" ? "B" : "A";
            CurrentStep = 1;
            SessionEvents.Log("task_set_selected", activeSet);
        }

        public void Advance() { if (HasTargets) CurrentStep = Mathf.Min(CurrentStep + 1, Active.Count + 1); }
        public void StepBack() { CurrentStep = Mathf.Max(1, CurrentStep - 1); }
    }

    /// <summary>
    /// U2: selection_events.csv, one row per selection attempt including corrections.
    ///
    /// Definitions used by the analysis (Section 3.3.4):
    ///   hover_start_ts  onset of continuous hover on the element under the ray at select press
    ///   top1_id         element under the ray at select press (ray-cast alone)
    ///   popup_open_ts   empty when no popup was shown
    ///   confirmed_id    id written into the record
    ///   attempt         1 for the first confirmation of a task step, 2+ after an undo
    ///   correction      attempt &gt; 1
    /// </summary>
    public static class SelectionEventLog
    {
        private static readonly CsvLog Log = new CsvLog("selection_events.csv", true,
            "task_set", "task_step", "attempt", "target_id", "event_id",
            "hover_start_ts", "popup_open_ts", "confirm_ts",
            "candidates_shown", "chosen_rank", "confirmed_id", "top1_id",
            "correction", "target_type", "density_band", "ray_hit_point");

        private static readonly Dictionary<string, int> AttemptsPerStep = new Dictionary<string, int>();

        public static void ResetForSession() => AttemptsPerStep.Clear();

        public static void Write(StudyTaskSheet sheet, string eventId,
                                 DateTime hoverStart, DateTime? popupOpen, DateTime confirm,
                                 int candidatesShown, int chosenRank,
                                 string confirmedId, string top1Id, Vector3 hitPoint)
        {
            var target = sheet != null ? sheet.CurrentTarget : null;
            int step = sheet != null ? sheet.CurrentStep : 0;
            string set = sheet != null ? sheet.activeSet : "";
            string key = set + ":" + step;
            AttemptsPerStep.TryGetValue(key, out int attempt);
            attempt++;
            AttemptsPerStep[key] = attempt;

            Log.Write(set, step, attempt, target?.targetId ?? "", eventId,
                      StudySession.Iso(hoverStart),
                      popupOpen.HasValue ? StudySession.Iso(popupOpen.Value) : "",
                      StudySession.Iso(confirm),
                      candidatesShown, chosenRank, confirmedId, top1Id,
                      attempt > 1, target?.targetType ?? "", target?.densityBand ?? "",
                      hitPoint);
        }
    }
}
