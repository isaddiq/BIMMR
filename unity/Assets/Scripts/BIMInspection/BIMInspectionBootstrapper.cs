using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Drop-in helper that wires up the entire inspection module on a single
    /// empty GameObject (suggested name: <c>InspectionSystem</c>).
    ///
    /// Assign the BIM model root transforms and (optionally) the MR camera in
    /// the inspector. On <c>Awake</c> the bootstrapper auto-creates any of the
    /// inspection sub-components that aren't already present, then forwards
    /// the configured roots and inspector name to them.
    ///
    /// You do NOT need this script if you'd rather wire each component
    /// manually — it is purely a convenience.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class BIMInspectionBootstrapper : MonoBehaviour
    {
        [Header("Scene")]
        [Tooltip("Roots of the BIM model(s) in the scene. These are used for " +
                 "progress totals, optional auto-collider setup, and stable-id " +
                 "scans.")]
        public Transform[] bimModelRoots;

        [Tooltip("The MR / XR camera. Defaults to Camera.main when unset.")]
        public Camera xrCamera;

        [Tooltip("Display name attached to inspection records (e.g. 'A. Engineer').")]
        public string inspectorName = "Inspector";

        [Tooltip("Optional preferred metadata key for the stable id.")]
        public string preferredStableIdKey;

        [Tooltip("Layers eligible for selection raycasts.")]
        public LayerMask selectableLayers = ~0;

        [Header("Auto-setup")]
        [Tooltip("If true, generated MeshColliders are added to BIM elements that " +
                 "have a renderable mesh but no collider on Awake.")]
        public bool addMissingCollidersOnAwake = true;

        // Created / discovered sub-components.
        public BIMInspectionManager     Manager        { get; private set; }
        public BIMElementMetadataReader Reader         { get; private set; }
        public BIMInspectionHighlighter Highlighter    { get; private set; }
        public BIMInspectionSelector    Selector       { get; private set; }
        public BIMIssuePinManager       IssuePins      { get; private set; }
        public BIMInspectionWorldUI     WorldUI        { get; private set; }
        public BIMInspectionProgressMonitor Progress   { get; private set; }
        public BIMInspectionExporter    Exporter       { get; private set; }
        public BIMInspectionColliderUtility ColliderUtil { get; private set; }

        private void Awake()
        {
            Manager      = GetOrAdd<BIMInspectionManager>();
            Reader       = GetOrAdd<BIMElementMetadataReader>();
            Highlighter  = GetOrAdd<BIMInspectionHighlighter>();
            Selector     = GetOrAdd<BIMInspectionSelector>();
            IssuePins    = GetOrAdd<BIMIssuePinManager>();
            WorldUI      = GetOrAdd<BIMInspectionWorldUI>();
            Progress     = GetOrAdd<BIMInspectionProgressMonitor>();
            Exporter     = GetOrAdd<BIMInspectionExporter>();
            ColliderUtil = GetOrAdd<BIMInspectionColliderUtility>();

            // Forward inspector-set scene values.
            Manager.bimModelRoots   = bimModelRoots;
            Manager.inspectorName   = inspectorName;
            Manager.metadataReader  = Reader;
            Manager.highlighter     = Highlighter;
            Manager.selector        = Selector;
            Manager.issuePinManager = IssuePins;
            Manager.worldUI         = WorldUI;
            Manager.progressMonitor = Progress;
            Manager.exporter        = Exporter;

            Reader.preferredStableIdKey = preferredStableIdKey;
            Highlighter.manager = Manager;

            Selector.manager           = Manager;
            Selector.selectionCamera   = xrCamera != null ? xrCamera : Camera.main;
            Selector.selectableLayers  = selectableLayers;
            Selector.bimModelRoots     = bimModelRoots;

            WorldUI.manager       = Manager;
            WorldUI.followCamera  = xrCamera != null ? xrCamera : Camera.main;

            IssuePins.pinCamera   = xrCamera != null ? xrCamera : Camera.main;

            ColliderUtil.bimModelRoots = bimModelRoots;

            if (addMissingCollidersOnAwake) ColliderUtil.AddMissingColliders();

            Debug.Log("[BIM Inspection] Bootstrapped on '" + name + "'. " +
                      "Roots=" + (bimModelRoots != null ? bimModelRoots.Length : 0) +
                      ", inspector=" + inspectorName);
        }

        private T GetOrAdd<T>() where T : Component
        {
            var c = GetComponent<T>();
            return c != null ? c : gameObject.AddComponent<T>();
        }
    }
}
