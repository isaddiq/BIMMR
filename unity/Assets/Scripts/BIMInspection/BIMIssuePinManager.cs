using System.Collections.Generic;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Spawns and manages 3D "issue pins" placed in the BIM scene. A pin is a
    /// world-space marker that points at the location of an
    /// <see cref="InspectionIssue"/>. Clicking a pin (via the same selector
    /// path or a dedicated mouse handler) opens a details panel.
    /// </summary>
    public class BIMIssuePinManager : MonoBehaviour
    {
        [Header("Prefabs (optional)")]
        [Tooltip("Prefab spawned for each issue. If null, a simple sphere is generated at runtime.")]
        public GameObject pinPrefab;

        [Header("Defaults for runtime-generated pins")]
        public float runtimePinRadius = 0.075f;
        public Color lowSeverityColor      = new Color(1f, 0.85f, 0.2f);
        public Color mediumSeverityColor   = new Color(1f, 0.6f, 0.1f);
        public Color highSeverityColor     = new Color(1f, 0.3f, 0.1f);
        public Color criticalSeverityColor = new Color(0.8f, 0f, 0f);

        [Header("Behavior")]
        [Tooltip("If true, pins continually rotate to face the main camera (billboard).")]
        public bool billboardToCamera = true;
        [Tooltip("Camera the pins should face. Defaults to Camera.main.")]
        public Camera pinCamera;

        // All active pins keyed by issueId.
        private readonly Dictionary<string, GameObject> _pins = new Dictionary<string, GameObject>();
        // Reverse lookup so a click on a pin can resolve back to the issue.
        private readonly Dictionary<GameObject, InspectionIssue> _pinToIssue =
            new Dictionary<GameObject, InspectionIssue>();

        // Lazily created billboard root used for runtime-spawned pins.
        private Transform _pinsRoot;

        private void Awake()
        {
            if (pinCamera == null) pinCamera = Camera.main;
            _pinsRoot = new GameObject("BIMInspection_PinsRoot").transform;
            _pinsRoot.SetParent(transform, worldPositionStays: false);
        }

        private void LateUpdate()
        {
            if (!billboardToCamera) return;
            var cam = pinCamera != null ? pinCamera : Camera.main;
            if (cam == null) return;
            foreach (var kv in _pins)
            {
                var pin = kv.Value;
                if (pin == null) continue;
                var dir = pin.transform.position - cam.transform.position;
                if (dir.sqrMagnitude < 0.0001f) continue;
                pin.transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        /// <summary>Create a pin for the given issue, located at
        /// <see cref="InspectionIssue.worldPosition"/>. If the world position is
        /// zero, the centre of the related element's bounds is used instead.</summary>
        public GameObject CreateIssuePin(InspectionIssue issue, GameObject relatedElement)
        {
            if (issue == null) return null;

            Vector3 pos = issue.worldPosition;
            if (pos == Vector3.zero && relatedElement != null)
                pos = ComputeElementCentre(relatedElement);

            GameObject pin;
            if (pinPrefab != null)
            {
                pin = Instantiate(pinPrefab, pos, Quaternion.identity, _pinsRoot);
            }
            else
            {
                pin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pin.transform.SetParent(_pinsRoot, worldPositionStays: false);
                pin.transform.position = pos;
                pin.transform.localScale = Vector3.one * (runtimePinRadius * 2f);
                var rend = pin.GetComponent<Renderer>();
                if (rend != null)
                {
                    var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                    rend.material = new Material(sh) { color = SeverityColor(issue.severity) };
                }
            }

            pin.name = $"IssuePin_{issue.issueId}";
            _pins[issue.issueId] = pin;
            _pinToIssue[pin] = issue;
            // Tag pins so the selector / pickers can recognise them.
            pin.tag = "Untagged"; // do not assume custom tags exist.
            return pin;
        }

        /// <summary>Reposition pins from their issues (e.g. after data reload).</summary>
        public void RefreshPins(IEnumerable<InspectionIssue> issues = null)
        {
            if (issues == null) return;
            foreach (var issue in issues)
            {
                if (_pins.TryGetValue(issue.issueId, out var go) && go != null)
                    go.transform.position = issue.worldPosition;
            }
        }

        /// <summary>Resolve an <see cref="InspectionIssue"/> from a clicked pin GameObject.</summary>
        public bool TryGetIssueFromPin(GameObject pin, out InspectionIssue issue)
        {
            issue = null;
            if (pin == null) return false;
            // Pins may have child colliders; walk up to the registered root.
            var t = pin.transform;
            while (t != null)
            {
                if (_pinToIssue.TryGetValue(t.gameObject, out issue)) return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>Show issue details (currently a Debug.Log; the world UI can
        /// subscribe via the manager to display a full panel).</summary>
        public void ShowIssueDetails(InspectionIssue issue)
        {
            if (issue == null) return;
            BIMLog.Verbose($"[BIM Inspection] Issue {issue.issueId}: " +
                      $"type={issue.issueType}, severity={issue.severity}, " +
                      $"resolved={issue.resolved}, comment={issue.comment}");
        }

        public void HideIssueDetails()
        {
            // Reserved for UI integration.
        }

        // --- helpers --------------------------------------------------------

        private static Vector3 ComputeElementCentre(GameObject element)
        {
            var renderers = element.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return element.transform.position;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.center;
        }

        private Color SeverityColor(string severity)
        {
            if (string.IsNullOrEmpty(severity)) return mediumSeverityColor;
            switch (severity.Trim().ToLowerInvariant())
            {
                case "low":      return lowSeverityColor;
                case "medium":   return mediumSeverityColor;
                case "high":     return highSeverityColor;
                case "critical": return criticalSeverityColor;
                default:         return mediumSeverityColor;
            }
        }
    }
}
