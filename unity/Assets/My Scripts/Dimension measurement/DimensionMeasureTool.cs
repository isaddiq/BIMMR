// Mixed Reality dimension measurement for BIM modules with prefab support and module tracking
// -------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

[DefaultExecutionOrder(50)]
public class DimensionMeasureTool : MonoBehaviour
{
    public enum UnitSystem { Meters, Millimeters, Centimeters, FeetInches }

    [Header("Activation")]
    [Tooltip("Optional UI toggle to turn measurement mode on/off.")]
    [SerializeField] private UnityEngine.UI.Toggle modeToggle;

    [Tooltip("Right-hand controller transform used as the ray origin/forward.")]
    [SerializeField] private Transform rightController;

    [Tooltip("Input action used to select points (e.g. Right Trigger / XR Select).")]
    [SerializeField] private InputActionReference selectAction;

    [Tooltip("Input action to cancel the in-progress point or remove the last completed dimension.")]
    [SerializeField] private InputActionReference cancelAction;

    [Header("Module Integration")]
    [Tooltip("Optional. When assigned, dimensions follow the spawned module and are cleared on module unload.")]
    [SerializeField] private ModuleManager moduleManager;

    [Header("Prefabs")]
    [Tooltip("TextMeshPro prefab for dimension labels.")]
    [SerializeField] private TextMeshPro labelPrefab;

    [Header("Raycast")]
    [SerializeField] private LayerMask raycastMask = ~0;
    [SerializeField, Min(0.1f)] private float maxRayDistance = 50f;
    [SerializeField] private bool drawAimRay = true;

    [Header("Display & Units")]
    [SerializeField] private UnitSystem unit = UnitSystem.Meters;
    [SerializeField, Range(0, 4)] private int decimals = 3;
    [SerializeField] private bool showUnitSuffix = true;
    [SerializeField, Min(0.001f)] private float lineWidth = 0.01f;
    [SerializeField, Min(0.001f)] private float previewLineWidth = 0.005f;
    [SerializeField, Min(0.001f)] private float markerRadius = 0.02f;

    [Header("Colors")]
    [SerializeField] private Color lineColor = new Color(0.13f, 0.67f, 1f, 1f);
    [SerializeField] private Color previewColor = new Color(1f, 0.74f, 0.2f, 1f);
    [SerializeField] private Color textColor = Color.white;

    [Header("Multi-measurement")]
    [Tooltip("Keep completed dimensions in the scene so multiple measurements can be made.")]
    [SerializeField] private bool keepCompletedDimensions = true;

    [Tooltip("Maximum number of completed dimensions kept in the scene. Oldest are removed beyond this. 0 = unlimited.")]
    [Min(0)] [SerializeField] private int maxStoredDimensions = 20;

    // ---- Preview helpers (the in-progress measurement) ----
    private LineRenderer previewLine;
    private GameObject previewMarkerA;
    private GameObject previewMarkerB;
    private TextMeshPro previewLabel;
    private Material previewLineMat;
    private Material previewMarkerMat;

    // Parent container for everything we spawn (world-space, decoupled from this transform)
    private Transform spawnRoot;

    // ---- Completed dimensions ----
    private readonly List<Dimension> completedDimensions = new List<Dimension>();

    // ---- State ----
    private bool isActive;
    private bool hasFirstPoint;
    private Vector3 p1, p2;
    private Transform moduleA;
    private Vector3 localP1;

    private Camera mainCam;

    public event Action<float, Vector3, Vector3> OnMeasurementCompleted;

    /// <summary>
    /// One persistent dimension: line + two markers + label, optionally parented to module(s) so it follows them.
    /// </summary>
    private class Dimension
    {
        public GameObject root;
        public LineRenderer line;
        public GameObject markerA;
        public GameObject markerB;
        public TextMeshPro label;
        public Transform moduleA;
        public Transform moduleB;
        public Vector3 localA;
        public Vector3 localB;
        public Material lineMat;
        public Material markerMatA;
        public Material markerMatB;

        public Vector3 WorldA => moduleA != null ? moduleA.TransformPoint(localA) : localA;
        public Vector3 WorldB => moduleB != null ? moduleB.TransformPoint(localB) : localB;
        public bool ModulesValid => (moduleA == null || moduleA) && (moduleB == null || moduleB);
    }

    #region Unity Lifecycle
    private void Awake()
    {
        RefreshCameraCache();
        EnsureSpawnRoot();
        EnsurePreviewHelpers();
        SetPreviewVisible(false);

        if (moduleManager == null)
            moduleManager = FindAnyObjectByType<ModuleManager>();
    }

    private void OnEnable()
    {
        if (modeToggle != null)
        {
            modeToggle.onValueChanged.AddListener(SetActive);
            if (modeToggle.isOn) SetActive(true);
        }

        if (selectAction != null && selectAction.action != null)
        {
            selectAction.action.performed += OnSelectPerformed;
            selectAction.action.Enable();
        }

        if (cancelAction != null && cancelAction.action != null)
        {
            cancelAction.action.performed += OnCancelPerformed;
            cancelAction.action.Enable();
        }

        if (moduleManager != null)
        {
            ((IBimModelProvider)moduleManager).OnModelUnloaded -= OnModelUnloaded;
            ((IBimModelProvider)moduleManager).OnModelUnloaded += OnModelUnloaded;
        }
    }

    private void OnDisable()
    {
        if (modeToggle != null)
            modeToggle.onValueChanged.RemoveListener(SetActive);

        if (selectAction != null && selectAction.action != null)
        {
            selectAction.action.performed -= OnSelectPerformed;
            selectAction.action.Disable();
        }

        if (cancelAction != null && cancelAction.action != null)
        {
            cancelAction.action.performed -= OnCancelPerformed;
            cancelAction.action.Disable();
        }

        if (moduleManager != null)
            ((IBimModelProvider)moduleManager).OnModelUnloaded -= OnModelUnloaded;
    }

    private void OnDestroy()
    {
        ClearAllDimensions();
        DestroyMaterial(ref previewLineMat);
        DestroyMaterial(ref previewMarkerMat);
        if (spawnRoot != null) Destroy(spawnRoot.gameObject);
    }

    private void Update()
    {
        // Always update existing dimensions so they follow their modules
        UpdateCompletedDimensions();

        if (!isActive || rightController == null) return;

        var hasHit = RaycastFromController(out RaycastHit hit);

        if (hasFirstPoint)
        {
            var end = hasHit ? hit.point : rightController.position + rightController.forward * maxRayDistance;
            p2 = end;
            UpdatePreviewLine(p1, p2, previewColor, previewLineWidth);
            UpdatePreviewLabel(p1, p2, FormatDistance(Vector3.Distance(p1, p2)));
            PositionMarker(previewMarkerB, p2, markerRadius);
            previewMarkerB.SetActive(true);
        }
        else if (drawAimRay)
        {
            // Show aim ray + hover marker at the hit point
            var end = hasHit ? hit.point : rightController.position + rightController.forward * Mathf.Min(2f, maxRayDistance);
            UpdatePreviewLine(rightController.position, end, previewColor, previewLineWidth * 0.5f);

            if (hasHit)
            {
                PositionMarker(previewMarkerA, hit.point, markerRadius * 0.8f);
                previewMarkerA.SetActive(true);
            }
            else
            {
                previewMarkerA.SetActive(false);
            }

            previewMarkerB.SetActive(false);
            previewLabel.gameObject.SetActive(false);
        }
        else
        {
            previewLine.enabled = false;
            previewMarkerA.SetActive(false);
            previewMarkerB.SetActive(false);
            previewLabel.gameObject.SetActive(false);
        }
    }
    #endregion

    #region Public API
    public void SetActive(bool active)
    {
        isActive = active;
        if (!isActive)
        {
            CancelInProgress();
            SetPreviewVisible(false);
        }
        else
        {
            SetPreviewVisible(true);
        }
    }

    public void SetUnit(UnitSystem unitSystem, int fractionalDigits = 3)
    {
        unit = unitSystem;
        decimals = Mathf.Clamp(fractionalDigits, 0, 4);

        // Refresh all existing dimension labels with the new format
        foreach (var dim in completedDimensions)
        {
            if (dim.label != null)
                dim.label.text = FormatDistance(Vector3.Distance(dim.WorldA, dim.WorldB));
        }
        if (hasFirstPoint && previewLabel != null)
            previewLabel.text = FormatDistance(Vector3.Distance(p1, p2));
    }

    public void SetRightController(Transform controller) => rightController = controller;

    public void ClearAllDimensions()
    {
        foreach (var dim in completedDimensions)
            DestroyDimension(dim);
        completedDimensions.Clear();
        CancelInProgress();
    }

    public void RemoveLastDimension()
    {
        if (completedDimensions.Count == 0) return;
        int last = completedDimensions.Count - 1;
        DestroyDimension(completedDimensions[last]);
        completedDimensions.RemoveAt(last);
    }

    public int CompletedDimensionCount => completedDimensions.Count;
    #endregion

    #region Input
    private void OnSelectPerformed(InputAction.CallbackContext ctx)
    {
        if (!isActive || rightController == null) return;
        if (!RaycastFromController(out RaycastHit hit)) return;

        if (!hasFirstPoint)
        {
            p1 = hit.point;
            hasFirstPoint = true;
            moduleA = ResolveModuleTransform(hit.collider);
            localP1 = moduleA != null ? moduleA.InverseTransformPoint(p1) : p1;

            PositionMarker(previewMarkerA, p1, markerRadius);
            previewMarkerA.SetActive(true);
        }
        else
        {
            p2 = hit.point;
            var moduleB = ResolveModuleTransform(hit.collider);
            float distance = Vector3.Distance(p1, p2);

            if (keepCompletedDimensions)
            {
                var dim = CreateDimension(p1, p2, moduleA, moduleB);
                completedDimensions.Add(dim);
                EnforceMaxDimensionLimit();
            }
            else
            {
                // Replace existing single dimension
                ClearAllDimensions();
                var dim = CreateDimension(p1, p2, moduleA, moduleB);
                completedDimensions.Add(dim);
            }

            // Reset preview state
            hasFirstPoint = false;
            previewMarkerA.SetActive(false);
            previewMarkerB.SetActive(false);
            previewLabel.gameObject.SetActive(false);
            previewLine.enabled = false;

            OnMeasurementCompleted?.Invoke(distance, p1, p2);
        }
    }

    private void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        if (!isActive) return;
        if (hasFirstPoint)
            CancelInProgress();
        else
            RemoveLastDimension();
    }

    private void CancelInProgress()
    {
        hasFirstPoint = false;
        p1 = p2 = Vector3.zero;
        moduleA = null;
        localP1 = Vector3.zero;
        if (previewMarkerA != null) previewMarkerA.SetActive(false);
        if (previewMarkerB != null) previewMarkerB.SetActive(false);
        if (previewLabel != null) previewLabel.gameObject.SetActive(false);
        if (previewLine != null) previewLine.enabled = false;
    }
    #endregion

    #region Module Tracking
    /// <summary>
    /// Find the module root for a collider. Prefers the parent that owns
    /// ModuleCenterCalculator / Metadata (BIM-aware), falls back to ModuleManager's
    /// current module, then to a name-based match.
    /// </summary>
    private Transform ResolveModuleTransform(Collider col)
    {
        if (col == null) return null;

        // 1) BIM marker components — most reliable
        var calc = col.GetComponentInParent<ModuleCenterCalculator>();
        if (calc != null) return calc.transform;

        // 2) The ModuleManager's current spawned module
        if (moduleManager != null)
        {
            var currentModule = moduleManager.GetCurrentModule();
            if (currentModule != null && col.transform.IsChildOf(currentModule.transform))
                return currentModule.transform;
        }

        // 3) Name-based fallback (legacy)
        var t = col.transform;
        while (t != null && !t.name.ToLower().Contains("module"))
            t = t.parent;
        return t;
    }

    private void OnModelUnloaded(string modelId)
    {
        // Drop any dimensions whose anchor module is about to be destroyed
        for (int i = completedDimensions.Count - 1; i >= 0; i--)
        {
            var dim = completedDimensions[i];
            // We rely on the actual GameObject being destroyed shortly after — so check
            // for "soon-to-be-null" by checking the moduleManager's current module match.
            if (moduleManager != null)
            {
                var currentModule = moduleManager.GetCurrentModule();
                if ((dim.moduleA != null && dim.moduleA.gameObject == currentModule) ||
                    (dim.moduleB != null && dim.moduleB.gameObject == currentModule))
                {
                    DestroyDimension(dim);
                    completedDimensions.RemoveAt(i);
                }
            }
        }

        if (hasFirstPoint && moduleA != null && moduleManager != null &&
            moduleA.gameObject == moduleManager.GetCurrentModule())
        {
            CancelInProgress();
        }
    }

    private void UpdateCompletedDimensions()
    {
        if (completedDimensions.Count == 0) return;

        // Remove any whose modules have been destroyed
        for (int i = completedDimensions.Count - 1; i >= 0; i--)
        {
            var dim = completedDimensions[i];
            if (!dim.ModulesValid)
            {
                DestroyDimension(dim);
                completedDimensions.RemoveAt(i);
                continue;
            }

            Vector3 a = dim.WorldA;
            Vector3 b = dim.WorldB;

            if (dim.line != null)
            {
                dim.line.SetPosition(0, a);
                dim.line.SetPosition(1, b);
            }
            if (dim.markerA != null) dim.markerA.transform.position = a;
            if (dim.markerB != null) dim.markerB.transform.position = b;
            if (dim.label != null)
            {
                dim.label.transform.position = (a + b) * 0.5f;
                dim.label.text = FormatDistance(Vector3.Distance(a, b));
                BillboardAndScaleLabel(dim.label.transform);
            }
        }
    }
    #endregion

    #region Dimension Factory
    private Dimension CreateDimension(Vector3 worldA, Vector3 worldB, Transform modA, Transform modB)
    {
        var root = new GameObject($"Dimension_{completedDimensions.Count + 1}");
        root.transform.SetParent(spawnRoot, true);

        var dim = new Dimension
        {
            root = root,
            moduleA = modA,
            moduleB = modB,
            localA = modA != null ? modA.InverseTransformPoint(worldA) : worldA,
            localB = modB != null ? modB.InverseTransformPoint(worldB) : worldB
        };

        // Line
        var lineGo = new GameObject("Line");
        lineGo.transform.SetParent(root.transform, false);
        dim.line = lineGo.AddComponent<LineRenderer>();
        dim.lineMat = CreateUnlitMaterial(lineColor);
        dim.line.material = dim.lineMat;
        dim.line.textureMode = LineTextureMode.Stretch;
        dim.line.alignment = LineAlignment.View;
        dim.line.widthMultiplier = lineWidth;
        dim.line.numCapVertices = 12;
        dim.line.positionCount = 2;
        dim.line.useWorldSpace = true;
        dim.line.startColor = dim.line.endColor = lineColor;
        dim.line.SetPosition(0, worldA);
        dim.line.SetPosition(1, worldB);

        // Markers
        dim.markerA = CreateMarker("Marker_A", out dim.markerMatA);
        dim.markerA.transform.SetParent(root.transform, true);
        PositionMarker(dim.markerA, worldA, markerRadius);
        dim.markerA.SetActive(true);

        dim.markerB = CreateMarker("Marker_B", out dim.markerMatB);
        dim.markerB.transform.SetParent(root.transform, true);
        PositionMarker(dim.markerB, worldB, markerRadius);
        dim.markerB.SetActive(true);

        // Label
        if (labelPrefab != null)
        {
            dim.label = Instantiate(labelPrefab, root.transform);
        }
        else
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            dim.label = labelGo.AddComponent<TextMeshPro>();
            dim.label.fontSize = 20.5f;
            dim.label.alignment = TextAlignmentOptions.CenterGeoAligned;
            dim.label.enableAutoSizing = false;
        }
        dim.label.name = "Label";
        dim.label.color = textColor;
        dim.label.transform.position = (worldA + worldB) * 0.5f;
        dim.label.text = FormatDistance(Vector3.Distance(worldA, worldB));
        BillboardAndScaleLabel(dim.label.transform);

        return dim;
    }

    private void DestroyDimension(Dimension dim)
    {
        if (dim == null) return;
        DestroyMaterial(ref dim.lineMat);
        DestroyMaterial(ref dim.markerMatA);
        DestroyMaterial(ref dim.markerMatB);
        if (dim.root != null) Destroy(dim.root);
    }

    private void EnforceMaxDimensionLimit()
    {
        if (maxStoredDimensions <= 0) return;
        while (completedDimensions.Count > maxStoredDimensions)
        {
            DestroyDimension(completedDimensions[0]);
            completedDimensions.RemoveAt(0);
        }
    }
    #endregion

    #region Core Logic
    private bool RaycastFromController(out RaycastHit hit)
    {
        // Prefer the visible right-hand ray: it stops at UI in front of the model.
        if (RightHandRay.Interactor != null)
            return RightHandRay.TryGet3DHit(out hit, maxRayDistance, raycastMask);

        if (rightController == null) { hit = default; return false; }
        return Physics.Raycast(rightController.position, rightController.forward, out hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Ignore);
    }

    private void UpdatePreviewLine(Vector3 a, Vector3 b, Color color, float width)
    {
        if (previewLine == null) return;
        previewLine.enabled = true;
        previewLine.startColor = color;
        previewLine.endColor = color;
        previewLine.widthMultiplier = Mathf.Max(0.0005f, width);
        previewLine.positionCount = 2;
        previewLine.SetPosition(0, a);
        previewLine.SetPosition(1, b);
    }

    private void UpdatePreviewLabel(Vector3 a, Vector3 b, string text)
    {
        if (previewLabel == null) return;
        previewLabel.gameObject.SetActive(true);
        previewLabel.text = text;
        previewLabel.transform.position = (a + b) * 0.5f;
        BillboardAndScaleLabel(previewLabel.transform);
    }

    private void BillboardAndScaleLabel(Transform labelTransform)
    {
        if (labelTransform == null) return;
        RefreshCameraCache();
        if (mainCam == null) return;

        labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - mainCam.transform.position);
        float dist = Vector3.Distance(labelTransform.position, mainCam.transform.position);
        float s = Mathf.Clamp(dist * 0.02f, 0.02f, 0.25f);
        labelTransform.localScale = Vector3.one * s;
    }

    private void RefreshCameraCache()
    {
        if (mainCam != null && mainCam.isActiveAndEnabled) return;
        mainCam = Camera.main;
        if (mainCam == null) mainCam = FindAnyObjectByType<Camera>();
    }
    #endregion

    #region Preview Helpers
    private void EnsureSpawnRoot()
    {
        if (spawnRoot != null) return;
        var go = new GameObject("DimensionMeasureTool_Root");
        spawnRoot = go.transform;
    }

    private void EnsurePreviewHelpers()
    {
        if (previewLineMat == null)
            previewLineMat = CreateUnlitMaterial(previewColor);
        if (previewMarkerMat == null)
            previewMarkerMat = CreateUnlitMaterial(previewColor);

        if (previewLine == null)
        {
            var go = new GameObject("Dimension_PreviewLine");
            go.transform.SetParent(spawnRoot, false);
            previewLine = go.AddComponent<LineRenderer>();
            previewLine.material = previewLineMat;
            previewLine.textureMode = LineTextureMode.Stretch;
            previewLine.alignment = LineAlignment.View;
            previewLine.widthMultiplier = previewLineWidth;
            previewLine.numCapVertices = 12;
            previewLine.useWorldSpace = true;
            previewLine.enabled = false;
        }

        if (previewMarkerA == null) previewMarkerA = CreatePreviewMarker("Dimension_PreviewMarker_A");
        if (previewMarkerB == null) previewMarkerB = CreatePreviewMarker("Dimension_PreviewMarker_B");

        if (previewLabel == null)
        {
            if (labelPrefab != null)
            {
                previewLabel = Instantiate(labelPrefab, spawnRoot);
                previewLabel.name = "Dimension_PreviewLabel";
                previewLabel.color = textColor;
            }
            else
            {
                var go = new GameObject("Dimension_PreviewLabel");
                go.transform.SetParent(spawnRoot, false);
                previewLabel = go.AddComponent<TextMeshPro>();
                previewLabel.fontSize = 20.5f;
                previewLabel.color = textColor;
                previewLabel.alignment = TextAlignmentOptions.CenterGeoAligned;
                previewLabel.enableAutoSizing = false;
            }
            previewLabel.gameObject.SetActive(false);
        }
    }

    private GameObject CreatePreviewMarker(string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(spawnRoot, false);
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = previewMarkerMat;
        go.SetActive(false);
        return go;
    }

    private GameObject CreateMarker(string name, out Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        mat = CreateUnlitMaterial(lineColor);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = mat;
        return go;
    }

    private void PositionMarker(GameObject m, Vector3 pos, float radius)
    {
        if (m == null) return;
        m.transform.position = pos;
        m.transform.localScale = Vector3.one * (radius * 2f);
    }

    private void SetPreviewVisible(bool visible)
    {
        if (!visible)
        {
            if (previewLine != null) previewLine.enabled = false;
            if (previewMarkerA != null) previewMarkerA.SetActive(false);
            if (previewMarkerB != null) previewMarkerB.SetActive(false);
            if (previewLabel != null) previewLabel.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Create a material that works in both Built-in and URP without polluting log noise.
    /// </summary>
    private static Material CreateUnlitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }

    private static void DestroyMaterial(ref Material mat)
    {
        if (mat == null) return;
        if (Application.isPlaying) Destroy(mat);
        else DestroyImmediate(mat);
        mat = null;
    }
    #endregion

    #region Formatting
    private string FormatDistance(float meters)
    {
        switch (unit)
        {
            case UnitSystem.Millimeters:
            {
                float mm = meters * 1000f;
                return showUnitSuffix ? $"{Math.Round(mm, decimals)} mm" : Math.Round(mm, decimals).ToString();
            }
            case UnitSystem.Centimeters:
            {
                float cm = meters * 100f;
                return showUnitSuffix ? $"{Math.Round(cm, decimals)} cm" : Math.Round(cm, decimals).ToString();
            }
            case UnitSystem.FeetInches:
            {
                const float mToFt = 3.2808399f;
                float totalFeet = meters * mToFt;
                int feet = Mathf.FloorToInt(totalFeet);
                float inches = (totalFeet - feet) * 12f;
                string inchStr = decimals > 0 ? inches.ToString($"F{decimals}") : Mathf.RoundToInt(inches).ToString();
                return showUnitSuffix ? $"{feet}' {inchStr}\"" : $"{feet} {inchStr}";
            }
            case UnitSystem.Meters:
            default:
                return showUnitSuffix ? $"{meters.ToString($"F{decimals}")} m" : meters.ToString($"F{decimals}");
        }
    }
    #endregion

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (previewLine != null) previewLine.widthMultiplier = Mathf.Max(previewLineWidth, 0.001f);
        if (previewLabel != null) previewLabel.color = textColor;
        foreach (var dim in completedDimensions)
        {
            if (dim?.line != null) dim.line.widthMultiplier = Mathf.Max(lineWidth, 0.001f);
            if (dim?.label != null) dim.label.color = textColor;
        }
    }
#endif
}
