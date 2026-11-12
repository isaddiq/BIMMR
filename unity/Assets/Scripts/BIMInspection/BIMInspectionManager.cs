using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BIMMR.Inspection.Study;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Central controller for the BIM inspection module. The manager owns:
    ///   * the currently selected element
    ///   * the dictionary of inspection records keyed by stable id
    ///   * a stableId -> GameObject map used by the highlighter and exporter
    /// It coordinates the metadata reader, highlighter, issue pin manager,
    /// progress monitor, and exporter.
    ///
    /// All public methods are designed to be safe to call from UI buttons
    /// (no parameters, returns void or simple values).
    /// </summary>
    public class BIMInspectionManager : MonoBehaviour
    {
        // -------- Inspector wiring -----------------------------------------

        [Header("Module References")]
        public BIMElementMetadataReader     metadataReader;
        public BIMInspectionHighlighter     highlighter;
        public BIMInspectionSelector        selector;
        public BIMIssuePinManager           issuePinManager;
        public BIMInspectionWorldUI         worldUI;
        public BIMInspectionProgressMonitor progressMonitor;
        public BIMInspectionExporter        exporter;

        [Header("Scene")]
        [Tooltip("Roots that contain BIM model GameObjects. Used to count " +
                 "tracked elements for progress and to resolve stable ids back " +
                 "to scene objects.")]
        public Transform[] bimModelRoots;

        [Header("Defaults")]
        public string inspectorName = "Inspector";

        [Header("Study instrumentation (U1-U3, U8, U10, U11)")]
        public StudySessionConfig studyConfig;
        public StudyTaskSheet taskSheet;
        public ElementScreenshotCapture screenshotCapture;
        public ObservationCapture observationCapture;
        [Tooltip("Module whose descendants are selectable when module scoping is on. " +
                 "Leave empty to use ModuleManager's current module.")]
        public Transform activeModuleOverride;
        [Tooltip("Seconds to wait for each LLM report narrative before exporting without it.")]
        public float narrativeTimeoutSeconds = 20f;

        // -------- Events ---------------------------------------------------

        public event Action<bool>                   OnInspectionModeChanged;
        public event Action<GameObject, BIMElementInfo, InspectionRecord> OnElementSelected;
        public event Action<InspectionRecord>       OnRecordUpdated;
        public event Action<InspectionIssue>        OnIssueAdded;

        // -------- State ----------------------------------------------------

        public bool InspectionModeActive { get; private set; }
        public GameObject SelectedElement { get; private set; }
        public BIMElementInfo SelectedInfo { get; private set; }
        public InspectionRecord SelectedRecord { get; private set; }

        public IReadOnlyDictionary<string, InspectionRecord> Records => _records;

        /// <summary>Event-level evidence records in confirmation order (undone ones excluded).</summary>
        public IReadOnlyList<EvidenceRecord> Evidence => _evidence;

        /// <summary>event_id of the most recent confirmed selection; links LLM calls to events.</summary>
        public string CurrentEventId { get; private set; } = "";
        public EvidenceRecord CurrentEvidence => _evidence.Count > 0 ? _evidence[_evidence.Count - 1] : null;
        public bool IsExporting { get; private set; }

        private readonly Dictionary<string, InspectionRecord> _records =
            new Dictionary<string, InspectionRecord>();
        private readonly List<EvidenceRecord> _evidence = new List<EvidenceRecord>();
        private readonly Dictionary<string, GameObject> _eventToObject = new Dictionary<string, GameObject>();

        // Cached mapping of stableId -> GameObject so we can recolor / refresh
        // visuals without re-scanning the scene every frame. Populated when
        // an element is first selected.
        private readonly Dictionary<string, GameObject> _stableIdToObject =
            new Dictionary<string, GameObject>();

        // Last raycast hit point passed in by the selector; used as the
        // default issue position so pins land where the user pointed.
        private Vector3 _lastHitPoint;

        // -------- Unity lifecycle ------------------------------------------

        private void Awake()
        {
            // Pull in sibling components if not assigned in the inspector.
            if (metadataReader   == null) metadataReader   = GetComponentInChildren<BIMElementMetadataReader>();
            if (highlighter      == null) highlighter      = GetComponentInChildren<BIMInspectionHighlighter>();
            if (selector         == null) selector         = GetComponentInChildren<BIMInspectionSelector>();
            if (issuePinManager  == null) issuePinManager  = GetComponentInChildren<BIMIssuePinManager>();
            if (worldUI          == null) worldUI          = GetComponentInChildren<BIMInspectionWorldUI>();
            if (progressMonitor  == null) progressMonitor  = GetComponentInChildren<BIMInspectionProgressMonitor>();
            if (exporter         == null) exporter         = GetComponentInChildren<BIMInspectionExporter>();

            if (studyConfig        == null) studyConfig        = FindObjectOfType<StudySessionConfig>();
            if (taskSheet          == null) taskSheet          = FindObjectOfType<StudyTaskSheet>();
            if (screenshotCapture  == null) screenshotCapture  = GetComponentInChildren<ElementScreenshotCapture>();
            if (screenshotCapture  == null) screenshotCapture  = gameObject.AddComponent<ElementScreenshotCapture>();
            if (observationCapture == null) observationCapture = FindObjectOfType<ObservationCapture>();

            // Wire the selector back to us.
            if (selector != null) selector.manager = this;

            // Wire exporter to surface our progress in the JSON header.
            if (exporter != null)
            {
                exporter.progressProvider = () => GetInspectionProgress();
                exporter.headerProvider   = BuildExportHeader;
            }
        }

        // -------- Public API ----------------------------------------------

        /// <summary>Enable inspection mode and show the UI.</summary>
        public void StartInspectionMode()
        {
            if (InspectionModeActive) return;
            InspectionModeActive = true;
            if (!StudySession.IsActive)
            {
                if (studyConfig != null) studyConfig.BeginSession();
                else StudySession.Begin(null, null, null);
                SelectionEventLog.ResetForSession();
                _evidence.Clear();
                _eventToObject.Clear();
            }
            SessionEvents.Log("inspection_start", taskSheet != null ? "set " + taskSheet.activeSet : "");
            if (worldUI != null) worldUI.Show();
            Debug.Log("[BIM Inspection] Inspection mode STARTED.");
            OnInspectionModeChanged?.Invoke(true);
        }

        /// <summary>Disable inspection mode and clear selection.</summary>
        public void StopInspectionMode()
        {
            if (!InspectionModeActive) return;
            InspectionModeActive = false;
            ClearSelection();
            if (worldUI != null) worldUI.Hide();
            Debug.Log("[BIM Inspection] Inspection mode STOPPED.");
            OnInspectionModeChanged?.Invoke(false);
        }

        /// <summary>Overload used by UI buttons that do not pass a hit point.</summary>
        public void SelectElement(GameObject element)
        {
            SelectElement(element, element != null ? element.transform.position : Vector3.zero);
        }

        /// <summary>Make <paramref name="element"/> the active selection. If
        /// the element has no record yet, one is created and marked
        /// <see cref="InspectionStatus.InProgress"/>.</summary>
        public void SelectElement(GameObject element, Vector3 worldHitPoint)
        {
            if (!InspectionModeActive)
            {
                BIMLog.Verbose("[BIM Inspection] Selection ignored - inspection mode is OFF.");
                return;
            }
            if (element == null) { ClearSelection(); return; }

            _lastHitPoint = worldHitPoint;

            // Read metadata.
            var info = metadataReader != null ? metadataReader.Read(element) : new BIMElementInfo
            {
                stableId = $"runtime::{element.name}::{element.GetEntityId()}",
                displayName = element.name,
                stableIdIsFallback = true
            };

            SelectedElement = element;
            SelectedInfo    = info;
            _stableIdToObject[info.stableId] = element;

            // Create record on first touch.
            if (!_records.TryGetValue(info.stableId, out var record))
            {
                record = new InspectionRecord
                {
                    elementStableId = info.stableId,
                    elementName     = info.displayName,
                    category        = info.category,
                    level           = info.level,
                    status          = InspectionStatus.InProgress,
                    inspectorName   = inspectorName
                };
                _records[info.stableId] = record;
                BIMLog.Verbose($"[BIM Inspection] New record created for '{info.displayName}' " +
                          $"[{info.stableId}]");
            }
            SelectedRecord = record;

            // Visuals.
            if (highlighter != null) highlighter.HighlightSelection(element);

            // Notify UI / observers.
            BIMLog.Verbose($"[BIM Inspection] Selected: name='{info.displayName}', " +
                      $"stableId='{info.stableId}', status={record.status}");
            OnElementSelected?.Invoke(element, info, record);
            OnRecordUpdated?.Invoke(record);
        }

        // -------- Study: confirmed selections (U1, U2, U11) ------------------

        /// <summary>Root of the module that bounds selection when module scoping is on.</summary>
        public Transform ActiveModuleRoot
        {
            get
            {
                if (activeModuleOverride != null) return activeModuleOverride;
                var mm = FindObjectOfType<ModuleManager>();
                var mod = mm != null ? mm.GetCurrentModule() : null;
                return mod != null ? mod.transform : null;
            }
        }

        /// <summary>Metadata of a scene object, with the same fallback as selection.</summary>
        public BIMElementInfo ReadInfo(GameObject go)
        {
            if (go == null) return null;
            return metadataReader != null ? metadataReader.Read(go) : new BIMElementInfo
            {
                stableId = $"runtime::{go.name}::{go.GetEntityId()}",
                displayName = go.name,
                stableIdIsFallback = true
            };
        }

        /// <summary>
        /// Commit a selection as an inspection event: assign the event id, bind the
        /// persistent element id into a new evidence record, capture the element-framed
        /// screenshot (and the L1 observation frame when enabled), and write the selection
        /// event row. Called by the selector after the optional n-best popup.
        /// </summary>
        public void ConfirmSelection(GameObject element, BIMElementInfo info, Vector3 hitPoint, SelectionContext ctx)
        {
            if (!InspectionModeActive || element == null) return;
            DateTime confirmTime = StudySession.Now;
            string eventId = StudySession.NextEventId();

            SelectElement(element, hitPoint);
            info = SelectedInfo ?? info;

            var ev = new EvidenceRecord
            {
                eventId = eventId,
                sessionId = StudySession.SessionId,
                participantId = StudySession.ParticipantId,
                condition = StudySession.Condition,
                buildVersion = StudySession.BuildVersion,
                elementId = info.stableId,
                elementIdIsFallback = info.stableIdIsFallback,
                timestamp = StudySession.Iso(confirmTime),
                moduleId = ActiveModuleRoot != null ? ActiveModuleRoot.name.Replace("(Clone)", "").Trim() : "",
                category = info.category,
                elementName = info.displayName,
                elementType = info.typeName,
                level = info.level,
                geometricDescriptors = GeometricDescriptors(info),
                rayHitPoint = CsvLog.Format(hitPoint)
            };

            if (screenshotCapture != null)
            {
                // With the structured schema switched off (U15 ablation) the image is written
                // without the element id in its name, i.e. as an unbound screenshot.
                string stem = StudySession.StructuredSchemaEnabled ? eventId : "img_" + DateTime.UtcNow.Ticks;
                ev.screenshotPath = screenshotCapture.Capture(element, stem, out ev.cameraPose) ?? "";
            }

            _evidence.Add(ev);
            _eventToObject[eventId] = element;
            CurrentEventId = eventId;

            if (observationCapture != null) observationCapture.Capture(eventId, hitPoint, confirmTime);

            SelectionEventLog.Write(taskSheet, eventId, ctx.hoverStart, ctx.popupOpen, confirmTime,
                                    ctx.candidatesShown, ctx.chosenRank, info.stableId, ctx.top1Id, hitPoint);
            if (taskSheet != null) taskSheet.Advance();
            SessionEvents.Log("evidence_captured", info.stableId, eventId);
        }

        /// <summary>Undo the last confirmed selection; the redo for the same task step is
        /// logged as a correction.</summary>
        public void UndoLastSelection()
        {
            if (_evidence.Count == 0) return;
            var last = _evidence[_evidence.Count - 1];
            last.superseded = true;
            _evidence.RemoveAt(_evidence.Count - 1);
            if (taskSheet != null) taskSheet.StepBack();
            CurrentEventId = _evidence.Count > 0 ? _evidence[_evidence.Count - 1].eventId : "";
            SessionEvents.Log("selection_undone", last.elementId, last.eventId);
        }

        /// <summary>
        /// End the inspection pass and export the paired HTML/JSON report. Reporting time
        /// (Table 10) is the difference between the inspection_end and report_export_done
        /// rows of session_events.csv.
        /// </summary>
        public void EndInspectionAndExport()
        {
            if (IsExporting) return;
            StartCoroutine(EndAndExportRoutine());
        }

        private IEnumerator EndAndExportRoutine()
        {
            IsExporting = true;
            SessionEvents.Log("inspection_end", $"{_evidence.Count} events");

            // Copy the live per-element status and notes into each event snapshot.
            foreach (var ev in _evidence)
            {
                if (_records.TryGetValue(ev.elementId, out var rec))
                {
                    ev.status = rec.status.ToString();
                    var notes = new List<string>();
                    if (rec.comments != null) notes.AddRange(rec.comments.Where(c => !string.IsNullOrWhiteSpace(c)));
                    if (rec.issues != null)
                        notes.AddRange(rec.issues.Select(i => $"[{i.severity}] {i.issueType}: {i.comment}".Trim()));
                    ev.notes = string.Join(" | ", notes);
                }
            }

            // LLM narrative per record (U1, U7). Fallback reporting: export without it.
            var narrator = FindObjectsOfType<MonoBehaviour>().OfType<IReportNarrativeService>().FirstOrDefault();
            bool narrate = narrator != null && narrator.NarrativeAvailable &&
                           StudySession.LlmContextEnabled && StudySession.StructuredSchemaEnabled;
            foreach (var ev in _evidence)
            {
                bool got = false;
                if (narrate)
                {
                    bool done = false;
                    StartCoroutine(narrator.RequestNarrative(ev, (text, flagged) =>
                    {
                        ev.llmNarrative = text;
                        ev.llmNarrativeFlagged = flagged;
                        done = true;
                    }));
                    float t = 0f;
                    while (!done && t < narrativeTimeoutSeconds) { t += Time.unscaledDeltaTime; yield return null; }
                    got = done && !string.IsNullOrWhiteSpace(ev.llmNarrative);
                }
                ev.reportStatus = got ? "final" : "final_without_narrative";
            }

            if (exporter != null)
            {
                var result = exporter.ExportEvidenceReport(_evidence);
                SessionEvents.Log("report_export_done", result);
            }
            IsExporting = false;
        }

        private static readonly string[] GeometryKeys =
        {
            "Length", "Width", "Height", "Thickness", "Depth", "Area", "Volume",
            "Dimensions.Length", "Dimensions.Width", "Dimensions.Height", "Dimensions.Thickness",
            "Dimensions.Area", "Dimensions.Volume", "Unconnected Height", "Rough Width", "Rough Height"
        };

        /// <summary>Key geometric descriptors from the BIM record (not from the mesh), so the
        /// field is empty when the source metadata does not carry them.</summary>
        private static string GeometricDescriptors(BIMElementInfo info)
        {
            if (info?.properties == null) return "";
            var parts = new List<string>();
            foreach (var key in GeometryKeys)
            {
                var hit = info.properties.FirstOrDefault(p =>
                    p.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    p.Key.EndsWith("." + key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(hit.Value) &&
                    !hit.Value.Equals("<undefined>", StringComparison.OrdinalIgnoreCase))
                    parts.Add($"{key}={hit.Value}");
                if (parts.Count >= 4) break;
            }
            return string.Join("; ", parts);
        }

        public void MarkSelectedAsPassed()      => SetSelectedStatus(InspectionStatus.Passed);
        public void MarkSelectedAsFailed()      => SetSelectedStatus(InspectionStatus.Failed);
        public void MarkSelectedAsNeedsReview() => SetSelectedStatus(InspectionStatus.NeedsReview);

        /// <summary>Add an issue to the currently selected element.</summary>
        public void AddIssueToSelected(string issueType, string severity, string comment)
        {
            if (SelectedRecord == null)
            {
                Debug.LogWarning("[BIM Inspection] AddIssueToSelected called without selection.");
                return;
            }

            var issue = new InspectionIssue
            {
                elementStableId = SelectedRecord.elementStableId,
                issueType = issueType,
                severity = severity,
                comment = comment,
                worldPosition = _lastHitPoint != Vector3.zero
                    ? _lastHitPoint
                    : ComputeElementCentre(SelectedElement)
            };
            SelectedRecord.issues.Add(issue);
            SelectedRecord.Touch();

            // Spawn the 3D pin.
            if (issuePinManager != null)
                issuePinManager.CreateIssuePin(issue, SelectedElement);

            BIMLog.Verbose($"[BIM Inspection] Issue added to '{SelectedRecord.elementName}': " +
                      $"type={issueType}, severity={severity}");
            OnIssueAdded?.Invoke(issue);
            OnRecordUpdated?.Invoke(SelectedRecord);
        }

        /// <summary>Append a free-text comment to the selected element's record.</summary>
        public void AddCommentToSelected(string comment)
        {
            if (SelectedRecord == null) return;
            if (string.IsNullOrWhiteSpace(comment)) return;
            SelectedRecord.comments.Add(comment);
            SelectedRecord.Touch();
            BIMLog.Verbose($"[BIM Inspection] Comment added to '{SelectedRecord.elementName}'.");
            OnRecordUpdated?.Invoke(SelectedRecord);
        }

        /// <summary>Drop the current selection (and remove its selection highlight).</summary>
        public void ClearSelection()
        {
            if (highlighter != null) highlighter.ClearSelectionHighlight();
            // After removing selection highlight, re-apply the status color for
            // the element so the user can still see Pass/Fail at a glance.
            if (SelectedElement != null && SelectedRecord != null && highlighter != null)
                highlighter.ApplyStatusColor(SelectedElement, SelectedRecord.status);

            SelectedElement = null;
            SelectedInfo    = null;
            SelectedRecord  = null;
        }

        /// <summary>Compute the latest progress snapshot.</summary>
        public InspectionProgressSummary GetInspectionProgress()
        {
            int totalTracked = CountTrackedElements();
            return progressMonitor != null
                ? progressMonitor.BuildSummary(_records, totalTracked)
                : new InspectionProgressSummary { totalElements = totalTracked };
        }

        /// <summary>
        /// Export to BOTH JSON and CSV in one call (the UI exposes them as
        /// separate buttons too).
        /// </summary>
        public void ExportInspectionData()
        {
            if (exporter == null)
            {
                Debug.LogWarning("[BIM Inspection] No exporter configured.");
                return;
            }
            exporter.ExportToJson(_records);
            exporter.ExportToCsv(_records);
        }

        public void ExportInspectionDataJson() => exporter?.ExportToJson(_records);
        public void ExportInspectionDataCsv()  => exporter?.ExportToCsv(_records);

        // -------- Helpers --------------------------------------------------

        /// <summary>Resolve a stable id back to a scene GameObject, if known.</summary>
        public GameObject ResolveStableId(string stableId)
        {
            if (string.IsNullOrEmpty(stableId)) return null;
            _stableIdToObject.TryGetValue(stableId, out var go);
            return go;
        }

        private void SetSelectedStatus(InspectionStatus status)
        {
            if (SelectedRecord == null)
            {
                Debug.LogWarning("[BIM Inspection] No element is selected.");
                return;
            }
            SelectedRecord.status = status;
            SelectedRecord.Touch();

            if (highlighter != null && SelectedElement != null)
                highlighter.ApplyStatusColor(SelectedElement, status);

            BIMLog.Verbose($"[BIM Inspection] Status of '{SelectedRecord.elementName}' " +
                      $"-> {status}");
            OnRecordUpdated?.Invoke(SelectedRecord);
        }

        private int CountTrackedElements()
        {
            if (bimModelRoots == null) return 0;
            int total = 0;
            foreach (var root in bimModelRoots)
            {
                if (root == null) continue;
                total += root.GetComponentsInChildren<Renderer>(false).Length;
            }
            return total;
        }

        private static Vector3 ComputeElementCentre(GameObject element)
        {
            if (element == null) return Vector3.zero;
            var renderers = element.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return element.transform.position;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.center;
        }

        private Dictionary<string, string> BuildExportHeader()
        {
            return new Dictionary<string, string>
            {
                { "exportedBy", inspectorName ?? "Unknown" },
                { "unityVersion", Application.unityVersion },
                { "productName", Application.productName },
                { "modeActive", InspectionModeActive.ToString() }
            };
        }
    }
}
