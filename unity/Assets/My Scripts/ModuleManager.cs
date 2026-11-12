// ModuleManager.cs - Enhanced XR Integration for Building Information Modeling
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Interface for ModuleManager to expose model loading state.
/// Used by FPSBenchmarker to observe model loading events.
/// </summary>
public interface IBimModelProvider
{
    /// <summary>
    /// Event fired when a new model is fully loaded and spawned.
    /// </summary>
    event Action<string, GameObject> OnModelLoaded;

    /// <summary>
    /// Event fired when the current model is about to be unloaded/destroyed.
    /// </summary>
    event Action<string> OnModelUnloaded;

    /// <summary>
    /// The currently loaded model's unique identifier (e.g., prefab name).
    /// </summary>
    string CurrentModelId { get; }

    /// <summary>
    /// The currently loaded model's root GameObject.
    /// </summary>
    GameObject CurrentModelRoot { get; }

    /// <summary>
    /// True if a model is currently loaded and active in the scene.
    /// </summary>
    bool IsModelLoaded { get; }
}

[System.Serializable]
public class ProjectData
{
    public string projectName;
    public List<GameObject> modulePrefabs = new List<GameObject>();
}

public class ModuleManager : MonoBehaviour, IBimModelProvider
{
    [Header("Projects and Spawning")]
    public List<ProjectData> projects = new List<ProjectData>();
    public TMP_Dropdown projectDropdown;
    public TMP_Dropdown moduleDropdown;
    public Transform spawnPoint;
    public float spawnDistance = 2f;

    [Header("Floor")]
    public LayerMask floorMask;
    public float floorRayStartHeight = 5f;
    public float floorRayDistance = 10f;

    [Header("Movement")]
    public InputActionReference moveAction;
    public InputActionReference upAction;
    public InputActionReference downAction;
    public float moveSpeed = 1f;
    public float verticalMoveSpeed = 1f;

    [Header("UI & Speed")]
    public Button increaseSpeedButton;
    public Button decreaseSpeedButton;
    public Button resetSpeedButton;
    public TMP_Text speedDisplayText;
    public float speedStep = 0.5f;

    [Header("Scale")]
    public Slider scaleSlider;
    public Button resetScaleButton;

    [Header("Component Hierarchy Levels")]
    public TMP_Dropdown levelDropdown;
    public List<GameObject> levelTogglePrefabs;
    public Transform toggleContainer;
    public float indentPerLevel = 20f;
    public float parentFontSize = 14f;
    public float childFontSize = 12f;

    [Header("XR Interaction Setup")]
    [SerializeField] private bool autoSetupXRInteraction = true;
    [SerializeField] private bool setupMetadataComponents = true;
    [SerializeField] private bool setupAllRenderers = true;
    [SerializeField] private LayerMask interactableLayer = -1;

    [Header("Voice Command Debug")]
    [SerializeField] private bool enableVoiceDebug = true;
    [SerializeField] private bool enableXRDebug = true;

    public event Action<GameObject> OnModuleSpawned;

    // IBimModelProvider interface implementation for FPS benchmarking
    public event Action<string, GameObject> OnModelLoaded;
    public event Action<string> OnModelUnloaded;

    public string CurrentModelId => currentModule != null ? currentModule.name : null;
    public GameObject CurrentModelRoot => currentModule;
    public bool IsModelLoaded => currentModule != null && currentModule.activeInHierarchy;

    private static readonly List<GameObject> EmptyPrefabList = new List<GameObject>();

    private GameObject currentModule;
    private Vector3 originalLocalCenter;
    private int currentProjectIndex = 0;
    private Transform _cachedCameraTransform;
    private List<GameObject> modulePrefabs => currentProjectIndex >= 0 && currentProjectIndex < projects.Count
        ? projects[currentProjectIndex].modulePrefabs
        : EmptyPrefabList;

    private Transform CachedCamera
    {
        get
        {
            if (_cachedCameraTransform == null || !_cachedCameraTransform.gameObject.activeInHierarchy)
            {
                var cam = Camera.main;
                if (cam == null) cam = FindAnyObjectByType<Camera>();
                _cachedCameraTransform = cam != null ? cam.transform : null;
            }
            return _cachedCameraTransform;
        }
    }

    // AEC Industry Hierarchy Structure - Inside Prefab:
    private int startToggleLevel = 1;
    private int maxToggleLevel = 2; // Default to show up to level 2
    private int maxSupportedLevel = 4; // Support up to level 4

    // Voice command integration - component mappings for every hierarchy level
    private readonly List<ComponentMapping> allComponentMappings = new List<ComponentMapping>();
    private readonly Dictionary<Transform, ComponentMapping> mappingByTransform = new Dictionary<Transform, ComponentMapping>();

    // XR Interaction tracking (list keeps order, set gives O(1) duplicate checks on large models)
    private readonly List<GameObject> xrInteractableObjects = new List<GameObject>();
    private readonly HashSet<GameObject> xrInteractableSet = new HashSet<GameObject>();

    // Pre-normalized AEC keywords, built once instead of per hierarchy node
    private struct AecKeyword
    {
        public string systemKey;
        public string lower;
        public string normalized;
    }
    private AecKeyword[] aecKeywordCache;
    private readonly HashSet<string> searchTermScratch = new HashSet<string>();

    private static readonly string[] ArchitecturalKeywords =
    {
        "wall", "door", "window", "ceiling", "floor", "roof", "stair", "partition", "facade",
        "curtain", "cladding", "architectural", "architecture", "arch", "envelope",
        // Korean architectural terms
        "벽", "문", "창", "천장", "바닥", "지붕", "계단", "파티션", "파사드", "커튼월", "외장", "건축"
    };

    // Enhanced component mapping structure
    [System.Serializable]
    public class ComponentMapping
    {
        public string originalName;
        public string normalizedName;
        public Transform transform;
        public Toggle toggle;
        public int hierarchyLevel;
        public List<string> searchTerms;
        public bool hasXRInteraction;
        public bool hasMetadata;

        public ComponentMapping()
        {
            searchTerms = new List<string>();
            hasXRInteraction = false;
            hasMetadata = false;
        }
    }

    // AEC Building System Keywords for better matching (English + Korean)
    private Dictionary<string, List<string>> aecSystemKeywords = new Dictionary<string, List<string>>()
    {
        ["architectural"] = new List<string> {
            "architectural", "architecture", "arch", "building envelope", "architectural system", "architectural elements",
            "건축", "건축물", "건축시스템", "건축구조", "아키텍처", "건축요소"
        },
        ["structural"] = new List<string> {
            "structural", "structure", "frame", "framing", "structural system", "structural elements", "load bearing",
            "구조", "구조물", "구조시스템", "골조", "프레임", "구조요소", "하중지지"
        },
        ["mep"] = new List<string> {
            "mep", "mechanical", "electrical", "plumbing", "building services", "mep systems", "utilities",
            "엠이피", "기계", "전기", "배관", "설비", "기계전기배관", "유틸리티"
        },
        ["exterior walls"] = new List<string> {
            "exterior walls", "exterior wall", "facade", "curtain wall", "cladding", "external walls", "building skin",
            "외벽", "외부벽", "커튼월", "파사드", "외장", "건물외피"
        },
        ["interior walls"] = new List<string> {
            "interior walls", "interior wall", "partitions", "internal walls", "room dividers", "partition walls",
            "내벽", "내부벽", "파티션", "칸막이벽", "내부칸막이", "실내벽"
        },
        ["columns"] = new List<string> {
            "columns", "column", "posts", "vertical supports", "structural columns", "pillars",
            "기둥", "컬럼", "수직지지체", "기둥부재", "기둥구조"
        },
        ["beams"] = new List<string> {
            "beams", "beam", "girders", "horizontal supports", "structural beams", "floor beams",
            "보", "빔", "거더", "수평지지체", "구조보"
        }
    };

    private void Awake()
    {
        BuildKeywordCache();

        if (projectDropdown != null)
        {
            projectDropdown.ClearOptions();
            projectDropdown.AddOptions(projects.ConvertAll(p => p != null ? p.projectName : "(missing project)"));
            if (projects.Count > 0)
            {
                projectDropdown.SetValueWithoutNotify(0);
                currentProjectIndex = 0;
            }
        }

        UpdateModuleDropdown();

        if (levelDropdown != null)
        {
            levelDropdown.ClearOptions();
            levelDropdown.AddOptions(new List<string> { "1", "2", "3", "4" });
            levelDropdown.SetValueWithoutNotify(1);
        }

        if (modulePrefabs.Count > 0)
            SpawnModule(0);
    }

    private void BuildKeywordCache()
    {
        var keywords = new List<AecKeyword>();
        foreach (var aecSystem in aecSystemKeywords)
        {
            foreach (var keyword in aecSystem.Value)
            {
                keywords.Add(new AecKeyword
                {
                    systemKey = aecSystem.Key,
                    lower = keyword.ToLower(),
                    normalized = NormalizeComponentName(keyword)
                });
            }
        }
        aecKeywordCache = keywords.ToArray();
    }

    private void UpdateModuleDropdown()
    {
        if (moduleDropdown == null) return;

        moduleDropdown.ClearOptions();
        moduleDropdown.AddOptions(modulePrefabs.ConvertAll(GetPrefabDisplayName));
        moduleDropdown.SetValueWithoutNotify(0);
    }

    private static string GetPrefabDisplayName(GameObject prefab) => prefab != null ? prefab.name : "(missing prefab)";

    private void OnEnable()
    {
        if (projectDropdown != null)
            projectDropdown.onValueChanged.AddListener(OnProjectChanged);
        if (moduleDropdown != null)
            moduleDropdown.onValueChanged.AddListener(OnModuleChanged);
        if (scaleSlider != null)
            scaleSlider.onValueChanged.AddListener(OnScaleChanged);
        if (resetScaleButton != null)
            resetScaleButton.onClick.AddListener(ResetScale);

        if (moveAction != null && moveAction.action != null) moveAction.action.Enable();
        if (upAction != null && upAction.action != null) upAction.action.Enable();
        if (downAction != null && downAction.action != null) downAction.action.Enable();

        if (increaseSpeedButton != null) increaseSpeedButton.onClick.AddListener(IncreaseSpeed);
        if (decreaseSpeedButton != null) decreaseSpeedButton.onClick.AddListener(DecreaseSpeed);
        if (resetSpeedButton != null) resetSpeedButton.onClick.AddListener(ResetSpeed);

        if (levelDropdown != null)
            levelDropdown.onValueChanged.AddListener(OnLevelDropdownChanged);

        UpdateSpeedDisplay();
    }

    private void OnDisable()
    {
        if (projectDropdown != null)
            projectDropdown.onValueChanged.RemoveListener(OnProjectChanged);
        if (moduleDropdown != null)
            moduleDropdown.onValueChanged.RemoveListener(OnModuleChanged);
        if (scaleSlider != null)
            scaleSlider.onValueChanged.RemoveListener(OnScaleChanged);
        if (resetScaleButton != null)
            resetScaleButton.onClick.RemoveListener(ResetScale);

        if (moveAction != null && moveAction.action != null) moveAction.action.Disable();
        if (upAction != null && upAction.action != null) upAction.action.Disable();
        if (downAction != null && downAction.action != null) downAction.action.Disable();

        if (increaseSpeedButton != null) increaseSpeedButton.onClick.RemoveListener(IncreaseSpeed);
        if (decreaseSpeedButton != null) decreaseSpeedButton.onClick.RemoveListener(DecreaseSpeed);
        if (resetSpeedButton != null) resetSpeedButton.onClick.RemoveListener(ResetSpeed);

        if (levelDropdown != null)
            levelDropdown.onValueChanged.RemoveListener(OnLevelDropdownChanged);
    }

    private void OnDestroy()
    {
        if (currentModule != null)
        {
            OnModelUnloaded?.Invoke(CurrentModelId);
            currentModule = null;
        }
    }

    private void Update()
    {
        if (currentModule == null) return;
        if (moveAction != null && moveAction.action != null)
            HandleHorizontalMovement();
        if (upAction != null && downAction != null && upAction.action != null && downAction.action != null)
            HandleVerticalMovement();
    }

    private void OnProjectChanged(int idx)
    {
        if (idx < 0 || idx >= projects.Count) return;

        currentProjectIndex = idx;
        UpdateModuleDropdown();

        // Clear current module when switching projects, then spawn the first module of the new project
        UnloadCurrentModule();
        if (!SpawnModule(0))
            RefreshComponentToggles();
    }

    private void OnModuleChanged(int idx)
    {
        UnloadCurrentModule();
        if (!SpawnModule(idx))
            RefreshComponentToggles();
    }

    /// <summary>
    /// Notifies listeners and removes the current module. The module is deactivated before the
    /// (deferred) Destroy so nothing else interacts with it for the rest of the frame.
    /// </summary>
    private void UnloadCurrentModule()
    {
        if (currentModule == null) return;

        OnModelUnloaded?.Invoke(CurrentModelId);
        currentModule.SetActive(false);
        Destroy(currentModule);
        currentModule = null;

        xrInteractableObjects.Clear();
        xrInteractableSet.Clear();
    }

    private bool SpawnModule(int idx)
    {
        var prefabs = modulePrefabs;
        if (idx < 0 || idx >= prefabs.Count) return false;

        var prefab = prefabs[idx];
        if (prefab == null)
        {
            Debug.LogWarning($"[Module Manager] Module prefab at index {idx} is null");
            return false;
        }

        Vector3 pos;
        if (spawnPoint != null)
        {
            pos = spawnPoint.position;
        }
        else
        {
            var cam = CachedCamera;
            pos = cam != null
                ? cam.position + cam.forward * spawnDistance
                : Vector3.zero;
        }

        currentModule = Instantiate(prefab, pos, Quaternion.identity);
        currentModule.transform.localScale = Vector3.one;

        CenterAndPlaceOnFloor(currentModule);

        var cc = currentModule.GetComponent<ModuleCenterCalculator>();
        if (cc != null)
            cc.CalculateCenter();

        originalLocalCenter = cc != null
            ? cc.GetLocalCenter()
            : Vector3.zero;

        if (scaleSlider != null) scaleSlider.value = 1f;

        // Setup XR interaction for all components in the spawned module
        if (autoSetupXRInteraction)
            SetupModuleForXRInteraction(currentModule);

        RefreshComponentToggles();
        // Study timing: module load is the first milestone after session start (Table 10).
        BIMMR.Inspection.Study.SessionEvents.Log("module_loaded", prefab.name);
        OnModuleSpawned?.Invoke(currentModule);
        OnModelLoaded?.Invoke(CurrentModelId, currentModule);

        // One summary line per spawn; detailed XR / voice logs stay behind the debug toggles
        Debug.Log($"[Module Manager] Spawned module: {prefab.name} ({xrInteractableObjects.Count} interactable objects)");

        return true;
    }

    #region XR Interaction Setup

    private void SetupModuleForXRInteraction(GameObject module)
    {
        xrInteractableObjects.Clear();
        xrInteractableSet.Clear();

        int layerIndex = ResolveInteractableLayerIndex();
        int metadataCount = 0;
        int rendererCount = 0;
        int removedGrabCount = 0;

        // Setup components with Metadata first (highest priority)
        if (setupMetadataComponents)
        {
            foreach (var metadata in module.GetComponentsInChildren<Metadata>(true))
            {
                if (SetupObjectForXRInteraction(metadata.gameObject, layerIndex)) removedGrabCount++;
                metadataCount++;
            }
        }

        // Setup all other renderers if enabled
        if (setupAllRenderers)
        {
            foreach (var renderer in module.GetComponentsInChildren<Renderer>(true))
            {
                // Skip if already has metadata (already processed) or is a UI / very small object
                if (renderer.TryGetComponent<Metadata>(out _)) continue;
                if (IsUIOrSmallObject(renderer)) continue;

                if (SetupObjectForXRInteraction(renderer.gameObject, layerIndex)) removedGrabCount++;
                rendererCount++;
            }
        }

        // Summary only: per-object logging stalls the spawn frame on large BIM models
        if (enableXRDebug)
        {
            Debug.Log($"[Module Manager] XR setup for '{module.name}': {xrInteractableObjects.Count} objects " +
                      $"({metadataCount} with Metadata, {rendererCount} renderers, {removedGrabCount} grab interactables removed)");
        }
    }

    /// <summary>
    /// Resolves the layer index from <see cref="interactableLayer"/>. Returns -1 when the mask is
    /// Nothing/Everything (or otherwise negative), meaning object layers are left untouched.
    /// For multi-layer masks the lowest selected layer is used.
    /// </summary>
    private int ResolveInteractableLayerIndex()
    {
        int mask = interactableLayer.value;
        if (mask <= 0) return -1;

        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) != 0)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Makes an object selectable (not grabbable). Returns true if an XRGrabInteractable was removed.
    /// </summary>
    private bool SetupObjectForXRInteraction(GameObject obj, int layerIndex)
    {
        bool removedGrab = false;

        // Ensure object has a collider for XR interaction
        if (!obj.TryGetComponent<Collider>(out _))
            AddAppropriateCollider(obj);

        // Add XRSimpleInteractable if not present (selectable but not grabbable).
        // The grab interactable is removed first so both never register the same collider.
        if (!obj.TryGetComponent<XRSimpleInteractable>(out _))
        {
            if (obj.TryGetComponent<XRGrabInteractable>(out var grabInteractable))
            {
                DestroyImmediate(grabInteractable);
                removedGrab = true;
            }

            obj.AddComponent<XRSimpleInteractable>();
        }

        if (layerIndex >= 0)
            obj.layer = layerIndex;

        // Track XR interactable objects
        if (xrInteractableSet.Add(obj))
            xrInteractableObjects.Add(obj);

        return removedGrab;
    }

    private void AddAppropriateCollider(GameObject obj)
    {
        if (obj.TryGetComponent<Renderer>(out var renderer))
        {
            if (obj.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
            {
                // Use MeshCollider for complex geometry
                var meshCollider = obj.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false; // More accurate for selection
            }
            else
            {
                // Use BoxCollider as fallback
                var boxCollider = obj.AddComponent<BoxCollider>();
                boxCollider.size = renderer.bounds.size;
            }
        }
        else
        {
            // Default BoxCollider
            obj.AddComponent<BoxCollider>();
        }
    }

    private static bool IsUIOrSmallObject(Renderer renderer)
    {
        // Skip UI objects
        if (renderer.transform is RectTransform || renderer.TryGetComponent<Canvas>(out _))
            return true;

        // Skip very small objects (likely details or hardware)
        var size = renderer.bounds.size;
        return size.x * size.y * size.z < 0.001f;
    }

    #endregion

    #region Module Management

    public GameObject GetCurrentModule() => currentModule;

    private void HandleHorizontalMovement()
    {
        var in2 = moveAction.action.ReadValue<Vector2>();
        if (in2.sqrMagnitude < 0.01f) return;
        var cam = CachedCamera;
        if (cam == null) return;
        var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        currentModule.transform.position += (right * in2.x + forward * in2.y) * moveSpeed * Time.deltaTime;
    }

    private void HandleVerticalMovement()
    {
        bool up = upAction.action.IsPressed();
        bool dn = downAction.action.IsPressed();
        if (up && !dn)
            currentModule.transform.position += Vector3.up * verticalMoveSpeed * Time.deltaTime;
        else if (dn && !up)
            currentModule.transform.position -= Vector3.up * verticalMoveSpeed * Time.deltaTime;
    }

    private void OnScaleChanged(float s)
    {
        if (currentModule == null) return;
        var before = currentModule.transform.TransformPoint(originalLocalCenter);
        currentModule.transform.localScale = Vector3.one * s;
        var after = currentModule.transform.TransformPoint(originalLocalCenter);
        currentModule.transform.position += (before - after);
    }

    private void ResetScale()
    {
        if (scaleSlider != null)
            scaleSlider.value = 1f;
    }

    private void CenterAndPlaceOnFloor(GameObject mod)
    {
        var t = mod.transform;
        if (mod.TryGetComponent<ModuleCenterCalculator>(out var cc))
        {
            Vector3 worldCenter = cc.GetModuleCenter();
            t.position -= (worldCenter - t.position);
        }

        var origin = t.position + Vector3.up * floorRayStartHeight;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, floorRayDistance, floorMask))
        {
            var pos = t.position;
            t.position = new Vector3(pos.x, hit.point.y, pos.z);
        }
    }

    private void IncreaseSpeed() { moveSpeed += speedStep; UpdateSpeedDisplay(); }
    private void DecreaseSpeed() { moveSpeed = Mathf.Max(0f, moveSpeed - speedStep); UpdateSpeedDisplay(); }
    private void ResetSpeed() { moveSpeed = 1f; UpdateSpeedDisplay(); }
    private void UpdateSpeedDisplay()
    {
        if (speedDisplayText != null)
            speedDisplayText.text = $"Speed: {moveSpeed:F2}";
    }

    private void OnLevelDropdownChanged(int idx)
    {
        maxToggleLevel = Mathf.Min(idx + 1, maxSupportedLevel);
        RefreshComponentToggles();
    }

    #endregion

    #region Component Toggle Management

    private void RefreshComponentToggles()
    {
        // Clear previous mappings
        allComponentMappings.Clear();
        mappingByTransform.Clear();

        if (toggleContainer != null)
        {
            for (int i = toggleContainer.childCount - 1; i >= 0; i--)
            {
                // Hide first so the layout rebuilds without the old entries this frame (Destroy is deferred)
                var child = toggleContainer.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        if (currentModule != null)
        {
            // First pass: Create all transform mappings for ALL hierarchy levels
            CreateAllTransformMappings(currentModule.transform, startToggleLevel);

            // Second pass: Create toggles for display levels
            CreateToggleRecursively(currentModule.transform, startToggleLevel);

            if (enableVoiceDebug)
            {
                Debug.Log($"[VOICE DEBUG] Created {allComponentMappings.Count} component mappings for voice commands " +
                          $"(metadata: {allComponentMappings.Count(m => m.hasMetadata)}, " +
                          $"XR interaction: {allComponentMappings.Count(m => m.hasXRInteraction)})");
            }
        }
    }

    private void CreateAllTransformMappings(Transform node, int level)
    {
        // Skip root node
        if (node == currentModule.transform)
        {
            foreach (Transform child in node)
                CreateAllTransformMappings(child, startToggleLevel);
            return;
        }

        // Create mapping for ALL components at ALL levels
        var mapping = new ComponentMapping
        {
            originalName = node.name,
            normalizedName = NormalizeComponentName(node.name),
            transform = node,
            hierarchyLevel = level,
            toggle = null,
            hasMetadata = node.TryGetComponent<Metadata>(out _),
            hasXRInteraction = node.TryGetComponent<XRSimpleInteractable>(out _)
        };

        // Generate multiple search terms for this component
        GenerateSearchTerms(mapping);

        allComponentMappings.Add(mapping);
        mappingByTransform[node] = mapping;

        // Continue recursion for all children regardless of display level
        foreach (Transform child in node)
            CreateAllTransformMappings(child, level + 1);
    }

    private void GenerateSearchTerms(ComponentMapping mapping)
    {
        string originalName = mapping.originalName.ToLower();
        var terms = mapping.searchTerms;
        terms.Clear();
        searchTermScratch.Clear();

        // Add the original name variations
        AddSearchTerm(terms, originalName);
        AddSearchTerm(terms, mapping.normalizedName);

        if (aecKeywordCache == null) BuildKeywordCache();

        // Check against AEC system keywords
        foreach (var keyword in aecKeywordCache)
        {
            // Exact (normalized) match, or partial match for complex names
            bool normalizedMatch = originalName.Contains(keyword.normalized) || keyword.normalized.Contains(originalName);
            bool partialMatch = originalName.Contains(keyword.lower) || keyword.lower.Contains(originalName);

            if (normalizedMatch || partialMatch)
            {
                AddSearchTerm(terms, keyword.systemKey);
                AddSearchTerm(terms, keyword.lower);
            }
            if (normalizedMatch)
                AddSearchTerm(terms, keyword.normalized);
        }
    }

    // Keeps first-occurrence order while skipping duplicates
    private void AddSearchTerm(List<string> terms, string term)
    {
        if (searchTermScratch.Add(term))
            terms.Add(term);
    }

    private void CreateToggleRecursively(Transform node, int level)
    {
        // Root node: Start from children inside the prefab
        if (node == currentModule.transform)
        {
            foreach (Transform child in node)
                CreateToggleRecursively(child, startToggleLevel);
            return;
        }

        // Stop recursion if we've reached the maximum display level
        if (level > maxToggleLevel) return;

        // Skip if no toggle prefabs configured or container missing
        if (levelTogglePrefabs == null || levelTogglePrefabs.Count == 0 || toggleContainer == null)
            return;

        // Get appropriate toggle prefab for this hierarchy level
        var prefab = level <= levelTogglePrefabs.Count
            ? levelTogglePrefabs[level - 1]
            : levelTogglePrefabs[levelTogglePrefabs.Count - 1];

        if (prefab == null) return;

        var entry = Instantiate(prefab, toggleContainer);
        var tog = entry.GetComponent<Toggle>();
        var txt = entry.GetComponentInChildren<TMP_Text>();

        if (tog == null)
        {
            Debug.LogWarning($"[Module Manager] Toggle prefab '{prefab.name}' missing Toggle component");
            Destroy(entry);
            return;
        }

        // Apply AEC-appropriate indentation and formatting
        if (txt != null)
        {
            int indentLevel = level - startToggleLevel;
            txt.text = new string(' ', indentLevel * 2) + node.name;
            txt.fontSize = (level == startToggleLevel) ? parentFontSize : childFontSize;
            txt.fontStyle = (level == startToggleLevel) ? FontStyles.Bold : FontStyles.Normal;
        }

        // Set toggle state based on current object visibility
        tog.isOn = node.gameObject.activeSelf;

        // Link toggle to the corresponding mapping (used by voice commands)
        if (mappingByTransform.TryGetValue(node, out var mapping))
            mapping.toggle = tog;

        // Handle toggle interactions
        tog.onValueChanged.AddListener(on =>
        {
            node.gameObject.SetActive(on);
            // If toggling a Level 1 building system, affect all child components
            if (level == startToggleLevel)
                SetChildrenActive(node, on);
        });

        // Continue recursion for child components up to maxToggleLevel
        if (level < maxToggleLevel)
        {
            foreach (Transform child in node)
                CreateToggleRecursively(child, level + 1);
        }
    }

    private void SetChildrenActive(Transform parent, bool active)
    {
        foreach (Transform c in parent)
            c.gameObject.SetActive(active);
    }

    #endregion

    #region Voice Command Integration

    public bool SpawnModuleByName(string moduleName)
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[VOICE DEBUG] === MODULE SPAWN REQUEST ===");
            Debug.Log($"[VOICE DEBUG] Input: '{moduleName}'");
        }

        string normalizedModuleName = NormalizeModuleName(moduleName);

        if (enableVoiceDebug)
        {
            Debug.Log($"[VOICE DEBUG] Normalized: '{normalizedModuleName}'");
            Debug.Log($"[VOICE DEBUG] Available modules: {string.Join(", ", GetAvailableModuleNames())}");
        }

        // Find module using various strategies
        int moduleIndex = string.IsNullOrWhiteSpace(moduleName) ? -1 : FindModuleIndex(moduleName, normalizedModuleName);

        if (moduleIndex >= 0)
        {
            if (enableVoiceDebug)
            {
                Debug.Log($"[VOICE DEBUG] ✅ Found module at index {moduleIndex}: '{modulePrefabs[moduleIndex].name}'");
            }

            // Update dropdown to reflect the change. If the dropdown already shows this module,
            // setting its value would not fire onValueChanged, so respawn directly instead.
            if (moduleDropdown != null && moduleDropdown.value != moduleIndex)
            {
                moduleDropdown.value = moduleIndex;
            }
            else
            {
                if (moduleDropdown != null) moduleDropdown.SetValueWithoutNotify(moduleIndex);
                OnModuleChanged(moduleIndex);
            }

            BIMLog.Verbose($"✅ Module spawned by voice: '{moduleName}' -> '{modulePrefabs[moduleIndex].name}'");
            return true;
        }

        if (enableVoiceDebug)
        {
            Debug.Log($"[VOICE DEBUG] ❌ Module '{moduleName}' not found");
        }

        Debug.LogWarning($"❌ Module '{moduleName}' not found in prefab list");
        return false;
    }

    private int FindModuleIndex(string originalName, string normalizedName)
    {
        var prefabs = modulePrefabs;

        // Strategy 1: Exact name match
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] != null && prefabs[i].name.Equals(originalName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // Strategy 2: Normalized name match
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] != null && NormalizeModuleName(prefabs[i].name).Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // Strategy 3: Contains match
        string searchName = originalName.ToLower();
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] == null) continue;
            string prefabName = prefabs[i].name.ToLower();

            if (prefabName.Contains(searchName) || searchName.Contains(prefabName))
                return i;
        }

        return -1;
    }

    public bool SetComponentVisibility(string componentName, bool visible)
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[VOICE DEBUG] === COMPONENT VISIBILITY REQUEST ===");
            Debug.Log($"[VOICE DEBUG] Input: '{componentName}' -> {visible}");
        }

        // Find and toggle component
        var mapping = FindComponentMapping(componentName);
        if (mapping != null)
        {
            if (mapping.toggle != null)
            {
                mapping.toggle.isOn = visible;
            }
            else
            {
                mapping.transform.gameObject.SetActive(visible);
            }

            if (enableVoiceDebug)
            {
                Debug.Log($"[VOICE DEBUG] ✅ SUCCESS: '{componentName}' -> '{mapping.originalName}' -> {visible}");
            }
            return true;
        }

        if (enableVoiceDebug)
        {
            Debug.Log($"[VOICE DEBUG] ❌ Component '{componentName}' not found");
        }
        return false;
    }

    private ComponentMapping FindComponentMapping(string componentName)
    {
        // An empty name would "contain"-match the first component
        if (string.IsNullOrWhiteSpace(componentName)) return null;

        string normalizedName = NormalizeComponentName(componentName);
        string lowerName = componentName.ToLower();

        // Direct mapping lookup
        foreach (var m in allComponentMappings)
        {
            if (m.transform == null) continue;
            if (m.normalizedName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase) ||
                m.searchTerms.Contains(normalizedName))
                return m;
        }

        // Fuzzy matching
        foreach (var m in allComponentMappings)
        {
            if (m.transform == null) continue;
            string original = m.originalName.ToLower();
            if (original.Contains(lowerName) || lowerName.Contains(original) ||
                m.searchTerms.Any(term => term.Contains(normalizedName) || normalizedName.Contains(term)))
                return m;
        }

        return null;
    }

    public List<string> GetAvailableModuleNames()
    {
        return modulePrefabs.Where(p => p != null).Select(p => p.name).ToList();
    }

    public List<string> GetAvailableBuildingComponents()
    {
        return allComponentMappings.Select(m => m.originalName).ToList();
    }

    public int GetXRInteractableObjectCount()
    {
        return xrInteractableObjects.Count;
    }

    public List<GameObject> GetXRInteractableObjects()
    {
        return new List<GameObject>(xrInteractableObjects);
    }

    public bool GetComponentVisibility(string componentName)
    {
        var mapping = FindComponentMapping(componentName);
        if (mapping != null)
        {
            if (mapping.toggle != null)
                return mapping.toggle.isOn;
            else
                return mapping.transform.gameObject.activeSelf;
        }

        if (enableVoiceDebug)
            Debug.Log($"[VOICE DEBUG] GetComponentVisibility: '{componentName}' not found");

        return false; // Default to hidden if not found
    }

    public void SetAllComponentsVisibility(bool visible)
    {
        // Set all toggles first (Level 1 toggles also drive their children)
        foreach (var mapping in allComponentMappings)
        {
            if (mapping.toggle != null)
                mapping.toggle.isOn = visible;
        }

        // Set all transforms without toggles
        foreach (var mapping in allComponentMappings)
        {
            if (mapping.toggle == null && mapping.transform != null)
                mapping.transform.gameObject.SetActive(visible);
        }

        if (enableVoiceDebug)
        {
            Debug.Log($"[VOICE DEBUG] Set all {allComponentMappings.Count} components to: {visible}");
        }
    }

    public void ToggleAllComponents()
    {
        // Toggle all toggles
        foreach (var mapping in allComponentMappings)
        {
            if (mapping.toggle != null)
                mapping.toggle.isOn = !mapping.toggle.isOn;
        }

        // Toggle all transforms without toggles
        foreach (var mapping in allComponentMappings)
        {
            if (mapping.toggle == null && mapping.transform != null)
                mapping.transform.gameObject.SetActive(!mapping.transform.gameObject.activeSelf);
        }

        if (enableVoiceDebug)
            Debug.Log("[VOICE DEBUG] Toggled all components");
    }

    // Legacy methods for backward compatibility
    public bool SetSystemVisibility(string systemName, bool visible) => SetComponentVisibility(systemName, visible);
    public bool GetSystemVisibility(string systemName) => GetComponentVisibility(systemName);
    public void SetAllSystemsVisibility(bool visible) => SetAllComponentsVisibility(visible);
    public void ToggleAllSystems() => ToggleAllComponents();
    public List<string> GetAvailableBuildingSystems() => GetAvailableBuildingComponents();

    // Enhanced methods for GPT Bridge integration
    public void ToggleArchitecturalComponents()
    {
        // Materialized once: the predicate is evaluated per call, not per enumeration
        var architecturalMappings = GetArchitecturalMappings();

        foreach (var mapping in architecturalMappings)
        {
            if (mapping.toggle != null)
            {
                mapping.toggle.isOn = !mapping.toggle.isOn;
            }
            else
            {
                mapping.transform.gameObject.SetActive(!mapping.transform.gameObject.activeSelf);
            }
        }

        if (enableVoiceDebug)
        {
            Debug.Log($"[VOICE DEBUG] Toggled {architecturalMappings.Count} architectural components");
        }
    }

    public void SetArchitecturalVisibility(bool visible)
    {
        var architecturalMappings = GetArchitecturalMappings();

        foreach (var mapping in architecturalMappings)
        {
            if (mapping.toggle != null)
            {
                mapping.toggle.isOn = visible;
            }
            else
            {
                mapping.transform.gameObject.SetActive(visible);
            }
        }

        if (enableVoiceDebug)
        {
            Debug.Log($"[VOICE DEBUG] Set {architecturalMappings.Count} architectural components to: {visible}");
        }
    }

    private List<ComponentMapping> GetArchitecturalMappings()
    {
        return allComponentMappings
            .Where(m => m.transform != null &&
                        (IsArchitecturalComponent(m.originalName) || IsArchitecturalComponent(m.normalizedName)))
            .ToList();
    }

    private static bool IsArchitecturalComponent(string componentName)
    {
        if (string.IsNullOrEmpty(componentName)) return false;

        string name = componentName.ToLower();
        foreach (var keyword in ArchitecturalKeywords)
        {
            if (name.Contains(keyword))
                return true;
        }
        return false;
    }

    #endregion

    #region Utility Methods

    private string NormalizeComponentName(string componentName)
    {
        if (string.IsNullOrEmpty(componentName)) return "";

        return componentName.ToLower()
            .Replace("exterior walls", "exteriorwalls")
            .Replace("interior walls", "interiorwalls")
            .Replace("exterior wall", "exteriorwalls")
            .Replace("interior wall", "interiorwalls")
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace("(", "")
            .Replace(")", "")
            .Trim();
    }

    private string NormalizeModuleName(string moduleName)
    {
        if (string.IsNullOrEmpty(moduleName)) return "";

        return moduleName.ToLower()
            .Replace("installation on site", "installationonsite")
            .Replace(" ", "")
            .Replace("-", "")
            .Trim();
    }

    #endregion

    #region Context Menu Testing

    [ContextMenu("Debug XR Setup")]
    public void DebugXRSetup()
    {
        Debug.Log($"=== XR INTERACTION DEBUG ===");
        Debug.Log($"Total XR interactable objects: {xrInteractableObjects.Count}");
        Debug.Log($"Components with metadata: {allComponentMappings.Count(m => m.hasMetadata)}");
        Debug.Log($"Components with XR interaction: {allComponentMappings.Count(m => m.hasXRInteraction)}");

        foreach (var obj in xrInteractableObjects.Take(10))
        {
            if (obj == null) continue;
            Debug.Log($"XR Object: {obj.name} | Metadata: {obj.TryGetComponent<Metadata>(out _)} | Interactable: {obj.TryGetComponent<XRSimpleInteractable>(out _)}");
        }
    }

    [ContextMenu("Setup XR for Current Module")]
    public void SetupXRForCurrentModule()
    {
        if (currentModule != null)
        {
            SetupModuleForXRInteraction(currentModule);
            Debug.Log($"XR setup complete for {currentModule.name}: {xrInteractableObjects.Count} objects");
        }
    }

    #endregion
}