using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BIMMR.Inspection.Study
{
    /// <summary>
    /// U12: registration check points. The operator touches N known physical points (not
    /// the registration datums) with the controller tip; the distance between the measured
    /// tip position and the corresponding model point is logged in millimetres. Gives the
    /// median and P95 residual before and after re-registration (A12).
    ///
    /// Writes registration_checks.csv:
    ///   check_ts, check_point_id, measured (x,y,z), model (x,y,z), residual_mm, after_reregistration
    /// </summary>
    public class RegistrationCheck : MonoBehaviour
    {
        public ModuleManager moduleManager;
        public ModuleRegistration registration;
        public Transform controllerTip;
        [Tooltip("Optional explicit check points; otherwise children named Check_* are used.")]
        public List<Transform> checkPoints = new List<Transform>();
        public InputActionReference captureAction;

        public int NextIndex { get; private set; }

        private static readonly CsvLog Log = new CsvLog("registration_checks.csv", true,
            "check_ts", "check_point_id",
            "measured_x", "measured_y", "measured_z", "model_x", "model_y", "model_z",
            "residual_mm", "after_reregistration");

        private void OnEnable() { if (captureAction != null) captureAction.action.Enable(); }

        private void Update()
        {
            if (captureAction != null && captureAction.action.WasPressedThisFrame()) RecordNext();
        }

        /// <summary>UI: restart the check sequence at the first point.</summary>
        public void Restart() { NextIndex = 0; ResolvePoints(); }

        /// <summary>UI or input: record the controller tip against the next check point.</summary>
        public void RecordNext()
        {
            ResolvePoints();
            if (controllerTip == null || checkPoints.Count == 0) return;
            if (NextIndex >= checkPoints.Count) NextIndex = 0;

            var cp = checkPoints[NextIndex];
            Vector3 measured = controllerTip.position;
            Vector3 model = cp.position;
            float residualMm = Vector3.Distance(measured, model) * 1000f;
            bool after = registration != null && registration.LastActionWasReregistration;

            Log.Write(StudySession.NowIso(), cp.name,
                      measured.x, measured.y, measured.z, model.x, model.y, model.z,
                      residualMm, after);
            NextIndex++;
        }

        private void ResolvePoints()
        {
            if (checkPoints.Count > 0) return;
            var module = moduleManager != null ? moduleManager.GetCurrentModule() : null;
            if (module == null) return;
            foreach (var t in module.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Check_")) checkPoints.Add(t);
            checkPoints.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }
    }
}
