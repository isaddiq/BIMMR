using System;
using System.Collections.Generic;
using System.Linq;
using BIMMR.Inspection.Study;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Routes user "select" input to the <see cref="BIMInspectionManager"/>.
    ///
    /// Selection sources, in priority order:
    ///   1. The right-hand XR ray (see <see cref="RightHandRay"/>), selected
    ///      with the interactor's select input.
    ///   2. Mouse click (Editor and standalone). Used for testing.
    ///
    /// On select, the selector gathers every candidate element under or right next to the
    /// ray (stage 1 of Table 3), drops objects without a bound BIM identifier and, when
    /// module scoping is on, objects outside the active module. When n-best
    /// disambiguation is on (U3) and more than one candidate remains, the candidates are
    /// shown in a popup ranked by ray distance (U1); otherwise the nearest candidate is
    /// confirmed. Hover onset, popup timing, candidate count and chosen rank are passed to
    /// the manager for the selection event log (U2).
    /// </summary>
    public class BIMInspectionSelector : MonoBehaviour
    {
        [Header("References")]
        public BIMInspectionManager manager;
        [Tooltip("Camera used for mouse raycasts. Falls back to Camera.main.")]
        public Camera selectionCamera;
        public DisambiguationPopup popup;

        [Header("Configuration")]
        [Tooltip("Layers considered selectable. Set to 'Everything' to select any collider.")]
        public LayerMask selectableLayers = ~0;
        [Tooltip("Maximum mouse-ray distance in metres.")]
        public float mouseRayMaxDistance = 200f;
        [Tooltip("If true, the selector will scan bimModelRoots once at start " +
                 "and auto-add MeshColliders to objects that have a MeshFilter " +
                 "but no Collider, so they can be selected.")]
        public bool autoAddCollidersOnStart = false;
        [Tooltip("Optional roots used when autoAddCollidersOnStart is enabled.")]
        public Transform[] bimModelRoots;

        [Header("Candidate gathering (n-best)")]
        [Tooltip("Elements hit further than this behind the first hit are not candidates (m).")]
        public float candidateDepth = 0.30f;
        [Tooltip("Elements whose collider lies within this radius of the hit point are candidates (m).")]
        public float candidateRadius = 0.05f;
        [Tooltip("Objects whose identifier did not bind to a BIM record cannot be confirmed " +
                 "(Table 3, F2). They are listed in the binding report instead.")]
        public bool rejectUnboundObjects = true;

        // Hover state (U2).
        private GameObject _hovered;
        private DateTime _hoverStart;

        private void Start()
        {
            if (selectionCamera == null) selectionCamera = Camera.main;
            if (popup == null) popup = GetComponent<DisambiguationPopup>() ?? gameObject.AddComponent<DisambiguationPopup>();
            if (autoAddCollidersOnStart)
            {
                var util = GetComponent<BIMInspectionColliderUtility>();
                if (util != null)
                {
                    util.bimModelRoots = bimModelRoots;
                    util.AddMissingColliders();
                }
            }
        }

        private void Update()
        {
            // While the popup is open, its buttons own the input.
            if (popup != null && popup.IsOpen) return;

            // Block selection while the user is interacting with a UI element.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            // 1. XR ray (if available). Triggered on a configurable input.
            if (RightHandRay.TryGetRay(out var xrRay) &&
                RightHandRay.TryGet3DHit(out var xrHit, mouseRayMaxDistance, selectableLayers))
            {
                TrackHover(xrHit.collider.gameObject);
                if (RightHandRay.WasSelectPressedThisFrame() && IsLayerSelectable(xrHit.collider.gameObject))
                {
                    BeginSelection(xrRay, xrHit);
                    return;
                }
            }
            else if (selectionCamera != null)
            {
                // 2. Mouse fallback (Editor / Standalone).
                var ray = selectionCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out var hit, mouseRayMaxDistance, selectableLayers, QueryTriggerInteraction.Ignore))
                {
                    TrackHover(hit.collider.gameObject);
                    if (Input.GetMouseButtonDown(0)) BeginSelection(ray, hit);
                }
                else TrackHover(null);
            }
            else TrackHover(null);
        }

        private void TrackHover(GameObject go)
        {
            if (go == _hovered) return;
            _hovered = go;
            _hoverStart = StudySession.Now;
        }

        private bool IsLayerSelectable(GameObject go)
        {
            return (selectableLayers.value & (1 << go.layer)) != 0;
        }

        private void BeginSelection(Ray ray, RaycastHit firstHit)
        {
            if (manager == null) manager = FindObjectOfType<BIMInspectionManager>();
            if (manager == null)
            {
                Debug.LogWarning("[BIM Inspection] No BIMInspectionManager in scene; selection ignored.");
                return;
            }
            if (!manager.InspectionModeActive) return;

            DateTime pressTime = StudySession.Now;
            DateTime hoverStart = _hovered != null ? _hoverStart : pressTime;

            var ranked = GatherCandidates(ray, firstHit);
            if (ranked.Count == 0)
            {
                SessionEvents.Log("selection_rejected", "no bound candidate under the ray");
                return;
            }

            var ctx = new SelectionContext
            {
                hoverStart = hoverStart,
                pressTime = pressTime,
                top1Id = ranked[0].info.stableId,
                candidatesShown = 0,
                chosenRank = 1
            };

            if (StudySession.EnableDisambiguation && ranked.Count > 1 && popup != null)
            {
                ctx.popupOpen = StudySession.Now;
                ctx.candidatesShown = Mathf.Min(ranked.Count, popup.maxCandidates);
                SessionEvents.Log("popup_open", $"{ctx.candidatesShown} candidates");
                popup.Show(ranked, rank =>
                {
                    if (rank <= 0)
                    {
                        SessionEvents.Log("popup_cancelled");
                        return;
                    }
                    ctx.chosenRank = rank;
                    var chosen = ranked[rank - 1];
                    manager.ConfirmSelection(chosen.element, chosen.info, chosen.hitPoint, ctx);
                });
                return;
            }

            var top = ranked[0];
            manager.ConfirmSelection(top.element, top.info, top.hitPoint, ctx);
        }

        /// <summary>
        /// Candidates are (a) every element hit along the ray within <see cref="candidateDepth"/>
        /// behind the first hit and (b) every element whose collider touches a sphere of
        /// <see cref="candidateRadius"/> at the first hit point. Elements split into several
        /// meshes share one identifier and appear once. The list is ranked by distance along
        /// the ray.
        /// </summary>
        public List<SelectionCandidate> GatherCandidates(Ray ray, RaycastHit firstHit)
        {
            var byId = new Dictionary<string, SelectionCandidate>();
            Transform scope = StudySession.ModuleScoping ? manager.ActiveModuleRoot : null;

            void Consider(Collider col, Vector3 point, float dist)
            {
                if (col == null) return;
                var go = col.gameObject;
                if (!IsLayerSelectable(go)) return;
                if (scope != null && !go.transform.IsChildOf(scope)) return;
                var info = manager.ReadInfo(go);
                if (info == null) return;
                if (info.stableIdIsFallback && rejectUnboundObjects)
                {
                    BIMLog.Verbose($"[BIM Inspection] Unbound object '{go.name}' is not selectable.");
                    return;
                }
                if (byId.TryGetValue(info.stableId, out var existing) && existing.rayDistance <= dist) return;
                byId[info.stableId] = new SelectionCandidate { element = go, info = info, rayDistance = dist, hitPoint = point };
            }

            float maxDist = firstHit.distance + candidateDepth;
            foreach (var h in Physics.RaycastAll(ray, maxDist, selectableLayers, QueryTriggerInteraction.Ignore))
                Consider(h.collider, h.point, h.distance);

            if (candidateRadius > 0f)
            {
                foreach (var col in Physics.OverlapSphere(firstHit.point, candidateRadius, selectableLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 p = col.ClosestPoint(firstHit.point);
                    float along = Vector3.Dot(p - ray.origin, ray.direction);
                    Consider(col, p, Mathf.Max(along, 0f));
                }
            }

            return byId.Values.OrderBy(c => c.rayDistance).ToList();
        }
    }

    /// <summary>Timing and ranking of one selection attempt, passed to the manager.</summary>
    public class SelectionContext
    {
        public DateTime hoverStart;
        public DateTime pressTime;
        public DateTime? popupOpen;
        public int candidatesShown;   // 0 = no popup
        public int chosenRank = 1;
        public string top1Id;
    }
}
