using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BIMMR.Inspection.Study
{
    /// <summary>
    /// Three-point manual registration of the virtual module to the physical module
    /// (Section 3.2.4, stage 2), with re-registration on the same reference points.
    ///
    /// Each module prefab carries three datum transforms (named "Datum_1".."Datum_3", or
    /// assigned explicitly). The operator touches the matching physical datum with the
    /// controller tip and captures it; after the third point the rigid transform that maps
    /// the model datums onto the measured points is applied to the module root.
    ///
    /// Logged: registration_events.csv (type, fit residual per point) and the
    /// module_pose_confirmed / re_registration rows of session_events.csv, from which
    /// re-registrations per session (Table 10) are counted.
    /// </summary>
    public class ModuleRegistration : MonoBehaviour
    {
        public ModuleManager moduleManager;
        [Tooltip("Transform at the physical tip of the right controller.")]
        public Transform controllerTip;
        [Tooltip("Optional explicit datums; otherwise children named Datum_1..Datum_3 are used.")]
        public Transform[] modelDatums = new Transform[3];
        [Tooltip("Optional input that captures the current point (e.g. right primary button).")]
        public InputActionReference captureAction;

        public int ReregistrationCount { get; private set; }
        public bool LastActionWasReregistration => ReregistrationCount > 0;
        public bool IsRegistering => _capturing;
        public int PointsCaptured => _measured.Count;

        private readonly List<Vector3> _measured = new List<Vector3>();
        private bool _capturing;
        private bool _registeredOnce;

        private static readonly CsvLog Log = new CsvLog("registration_events.csv", true,
            "ts", "type", "residual_p1_mm", "residual_p2_mm", "residual_p3_mm", "mean_residual_mm");

        private void OnEnable() { if (captureAction != null) captureAction.action.Enable(); }

        private void Update()
        {
            if (_capturing && captureAction != null && captureAction.action.WasPressedThisFrame())
                CapturePoint();
        }

        /// <summary>UI: start (or restart) three-point registration.</summary>
        public void BeginRegistration()
        {
            if (StudySession.BimOnlyViewerMode)
            {
                Debug.Log("[Registration] BIM-only viewer mode: registration is disabled.");
                return;
            }
            _measured.Clear();
            _capturing = true;
            SessionEvents.Log(_registeredOnce ? "re_registration_started" : "registration_started");
        }

        /// <summary>UI or input: capture the controller tip as the next datum.</summary>
        public void CapturePoint()
        {
            if (!_capturing || controllerTip == null) return;
            _measured.Add(controllerTip.position);
            if (_measured.Count == 3) Apply();
        }

        private void Apply()
        {
            _capturing = false;
            var module = moduleManager != null ? moduleManager.GetCurrentModule() : null;
            var datums = ResolveDatums(module);
            if (module == null || datums == null)
            {
                Debug.LogWarning("[Registration] No module or datums; registration aborted.");
                return;
            }

            Vector3[] m = datums.Select(d => d.position).ToArray();
            Vector3[] p = _measured.ToArray();
            Quaternion r = Frame(p) * Quaternion.Inverse(Frame(m));
            Vector3 cm = (m[0] + m[1] + m[2]) / 3f, cp = (p[0] + p[1] + p[2]) / 3f;

            var t = module.transform;
            t.SetPositionAndRotation(r * (t.position - cm) + cp, r * t.rotation);

            var res = new float[3];
            for (int i = 0; i < 3; i++) res[i] = Vector3.Distance(datums[i].position, p[i]) * 1000f;

            string type = _registeredOnce ? "re_registration" : "initial";
            Log.Write(StudySession.NowIso(), type, res[0], res[1], res[2], res.Average());
            if (_registeredOnce)
            {
                ReregistrationCount++;
                SessionEvents.Log("re_registration", $"mean fit residual {res.Average():F1} mm");
            }
            else
            {
                SessionEvents.Log("module_pose_confirmed", $"mean fit residual {res.Average():F1} mm");
            }
            _registeredOnce = true;
        }

        /// <summary>Orthonormal frame spanned by three points (x along p0->p1, z normal).</summary>
        private static Quaternion Frame(Vector3[] q)
        {
            Vector3 x = (q[1] - q[0]).normalized;
            Vector3 n = Vector3.Cross(q[1] - q[0], q[2] - q[0]).normalized;
            if (n.sqrMagnitude < 1e-8f) n = Vector3.up;
            return Quaternion.LookRotation(n, Vector3.Cross(n, x));
        }

        private Transform[] ResolveDatums(GameObject module)
        {
            if (modelDatums != null && modelDatums.Length == 3 && modelDatums.All(d => d != null)) return modelDatums;
            if (module == null) return null;
            var found = new Transform[3];
            foreach (var tr in module.GetComponentsInChildren<Transform>(true))
                for (int i = 0; i < 3; i++)
                    if (tr.name == $"Datum_{i + 1}") found[i] = tr;
            return found.All(d => d != null) ? found : null;
        }
    }
}
