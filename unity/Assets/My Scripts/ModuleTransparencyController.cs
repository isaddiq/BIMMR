using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Controls per-renderer transparency based on distance from the user (camera).
/// Geometry CLOSER to the user fades according to the slider value, while geometry
/// further away stays opaque. This lets the user walk into a module and see through
/// the nearby (occluding) walls to inspect interior elements.
///
/// The slider is interpreted as: "how transparent should the closest geometry become?"
///   1.0 -> fully opaque (no transparency applied at all)
///   0.0 -> nearest geometry fully invisible
/// </summary>
public class ModuleTransparencyController : MonoBehaviour
{
    [Header("Link your ModuleManager here")]
    public ModuleManager moduleManager;

    [Header("Assign the UI Controls here")]
    public Button buttonReset;
    public Slider transparencySlider;

    [Header("Distance-Based Fade")]
    [Tooltip("Renderers closer than this distance (m) from the camera fully use the slider's alpha.")]
    public float nearFadeDistance = 0.5f;

    [Tooltip("Renderers farther than this distance (m) from the camera stay fully opaque.")]
    public float farFadeDistance = 4.0f;

    [Tooltip("How often (seconds) to recompute distance-based alpha while the user moves. 0 = every frame.")]
    [Range(0f, 0.5f)]
    public float updateInterval = 0.05f;

    [Tooltip("Camera movement (m) required to trigger a recompute between intervals. Lower = more responsive.")]
    public float cameraMoveThreshold = 0.02f;

    [Header("Rendering Settings")]
    [Tooltip("Base render queue spacing between building components")]
    public int renderQueueSpacing = 1;

    [Tooltip("Minimum alpha threshold so faded elements never disappear entirely")]
    [Range(0f, 0.2f)]
    public float minAlphaThreshold = 0.0f;

    [Tooltip("Apply transparency to all nested building assemblies")]
    public bool includeNestedAssemblies = true;

    [Tooltip("Enable double-sided rendering for transparent materials")]
    public bool enableDoubleSidedRendering = true;

    [Tooltip("Disable shadows on faded materials")]
    public bool disableShadowsWhenTransparent = true;

    [Tooltip("Optional: only fade renderers whose center is between the camera and the module center.")]
    public bool onlyFadeOccluders = true;

    [Tooltip("Verbose logging")]
    public bool debugLogging = false;

    private BuildingComponentMaterial[] _originalBuildingMaterials;
    private GameObject _lastConstructionModule;
    private float _currentSliderAlpha = 1f;
    private float _nextUpdateTime;
    private Vector3 _lastCameraPosition;
    private Transform _cachedCameraTransform;
    private Vector3 _moduleCenterCached;
    private int _transparentCount;

    // Cached shader property IDs (avoids string hashing on every material update)
    private static readonly int[] ColorPropertyIds =
    {
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_MainColor"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_Albedo")
    };
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");

    [System.Serializable]
    private struct BuildingComponentMaterial
    {
        public Material originalMaterial;
        public Material instanceMaterial;
        public Renderer renderer;
        public int materialIndex;
        public Color originalColor;
        public int originalSrcBlend;
        public int originalDstBlend;
        public int originalZWrite;
        public int originalZTest;
        public int originalRenderQueue;
        public int originalCullMode;
        public bool originalAlphaTest;
        public bool originalAlphaBlend;
        public bool originalAlphaPremultiply;
        public float originalAlphaCutoff;
        public int originalRenderingMode;
        public UnityEngine.Rendering.ShadowCastingMode originalShadowMode;
        public bool isCurrentlyTransparent;
        public float lastAppliedAlpha;
    }

    void Start()
    {
        InitializeUIControls();
        TryConnectToModuleManager();
        UpdateTargetConstructionModule();

        if (moduleManager != null)
            moduleManager.OnModuleSpawned += HandleModuleSpawned;
    }

    private void OnDestroy()
    {
        if (moduleManager != null)
            moduleManager.OnModuleSpawned -= HandleModuleSpawned;
        RestoreOriginalBuildingMaterials();
    }

    private void InitializeUIControls()
    {
        if (buttonReset != null)
            buttonReset.onClick.AddListener(ResetBuildingTransparency);

        if (transparencySlider != null)
        {
            transparencySlider.minValue = 0f;
            transparencySlider.maxValue = 1f;
            transparencySlider.value = 1f;
            transparencySlider.onValueChanged.AddListener(OnSliderValueChanged);
        }
        _currentSliderAlpha = 1f;
    }

    private void TryConnectToModuleManager()
    {
        if (moduleManager == null)
            moduleManager = FindAnyObjectByType<ModuleManager>();
    }

    void Update()
    {
        if (moduleManager == null) return;

        // Detect module change (in case OnModuleSpawned wasn't wired up)
        GameObject currentModule = moduleManager.GetCurrentModule();
        if (currentModule != _lastConstructionModule)
        {
            HandleModuleSpawned(currentModule);
            return;
        }

        if (_originalBuildingMaterials == null || _originalBuildingMaterials.Length == 0)
            return;

        // Throttle expensive distance recomputation
        if (Time.unscaledTime < _nextUpdateTime)
        {
            // But still update immediately if the camera moves a lot
            if (CameraMovedSignificantly() == false) return;
        }
        _nextUpdateTime = Time.unscaledTime + updateInterval;

        ApplyDistanceBasedTransparency(_currentSliderAlpha);
    }

    private bool CameraMovedSignificantly()
    {
        if (_cachedCameraTransform == null) return true;
        return (_cachedCameraTransform.position - _lastCameraPosition).sqrMagnitude > cameraMoveThreshold * cameraMoveThreshold;
    }

    private void HandleModuleSpawned(GameObject newModule)
    {
        RestoreOriginalBuildingMaterials();
        _lastConstructionModule = newModule;
        CacheBuildingComponentMaterials();
        _currentSliderAlpha = transparencySlider != null ? transparencySlider.value : 1f;
        ApplyDistanceBasedTransparency(_currentSliderAlpha);
    }

    private void UpdateTargetConstructionModule()
    {
        if (moduleManager == null) return;
        _lastConstructionModule = moduleManager.GetCurrentModule();
        CacheBuildingComponentMaterials();
    }

    private void CacheBuildingComponentMaterials()
    {
        _transparentCount = 0;

        if (_lastConstructionModule == null)
        {
            _originalBuildingMaterials = null;
            return;
        }

        Renderer[] allRenderers = _lastConstructionModule.GetComponentsInChildren<Renderer>(includeNestedAssemblies);
        var list = new List<BuildingComponentMaterial>(allRenderers.Length * 2);

        foreach (var renderer in allRenderers)
        {
            if (renderer == null || IsUIComponent(renderer.gameObject)) continue;

            Material[] originalMaterials = renderer.sharedMaterials;
            Material[] newInstances = new Material[originalMaterials.Length];

            for (int i = 0; i < originalMaterials.Length; i++)
            {
                Material orig = originalMaterials[i];
                if (orig == null)
                {
                    newInstances[i] = null;
                    continue;
                }

                Material instance = new Material(orig);
                instance.name = $"{orig.name}_TransparencyInstance_{renderer.GetEntityId()}_{i}";
                newInstances[i] = instance;

                list.Add(new BuildingComponentMaterial
                {
                    originalMaterial = orig,
                    instanceMaterial = instance,
                    renderer = renderer,
                    materialIndex = i,
                    originalColor = GetMaterialColor(orig),
                    originalSrcBlend = GetMaterialInt(orig, "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One),
                    originalDstBlend = GetMaterialInt(orig, "_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero),
                    originalZWrite = GetMaterialInt(orig, "_ZWrite", 1),
                    originalZTest = GetMaterialInt(orig, "_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual),
                    originalCullMode = GetMaterialInt(orig, "_Cull", (int)UnityEngine.Rendering.CullMode.Back),
                    originalRenderQueue = orig.renderQueue,
                    originalAlphaTest = orig.IsKeywordEnabled("_ALPHATEST_ON"),
                    originalAlphaBlend = orig.IsKeywordEnabled("_ALPHABLEND_ON"),
                    originalAlphaPremultiply = orig.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"),
                    originalAlphaCutoff = GetMaterialFloat(orig, "_Cutoff", 0.5f),
                    originalRenderingMode = GetMaterialInt(orig, "_Mode", 0),
                    originalShadowMode = renderer.shadowCastingMode,
                    isCurrentlyTransparent = false,
                    lastAppliedAlpha = 1f
                });
            }

            renderer.materials = newInstances;
        }

        _originalBuildingMaterials = list.ToArray();

        // Cache module center for occluder detection
        var center = _lastConstructionModule.GetComponent<ModuleCenterCalculator>();
        _moduleCenterCached = center != null ? center.GetModuleCenter() : _lastConstructionModule.transform.position;

        if (debugLogging)
            Debug.Log($"[Transparency] Cached {_originalBuildingMaterials.Length} materials across {allRenderers.Length} renderers in {_lastConstructionModule.name}");
    }

    /// <summary>
    /// Apply per-renderer transparency: closer renderers use the slider's alpha,
    /// farther renderers stay opaque. Recomputed each frame as the user moves.
    /// </summary>
    private void ApplyDistanceBasedTransparency(float sliderAlpha)
    {
        if (_originalBuildingMaterials == null || _originalBuildingMaterials.Length == 0)
            return;

        EnsureCameraCached();
        if (_cachedCameraTransform == null) return;

        Vector3 camPos = _cachedCameraTransform.position;
        Vector3 camForward = _cachedCameraTransform.forward;
        _lastCameraPosition = camPos;

        sliderAlpha = Mathf.Clamp01(sliderAlpha);
        bool sliderForcesOpaque = sliderAlpha >= 0.999f;

        // Fully opaque and nothing left to restore: skip the per-material pass entirely
        if (sliderForcesOpaque && _transparentCount == 0)
            return;

        float nearD = Mathf.Max(0.01f, nearFadeDistance);
        float farD = Mathf.Max(nearD + 0.01f, farFadeDistance);

        // Cache the previous renderer so we don't recompute the distance per material
        Renderer lastRenderer = null;
        float cachedAlpha = 1f;
        bool cachedNeedTransparent = false;

        for (int i = 0; i < _originalBuildingMaterials.Length; i++)
        {
            var cm = _originalBuildingMaterials[i];
            if (cm.instanceMaterial == null || cm.renderer == null) continue;

            float targetAlpha;
            bool needTransparent;

            if (cm.renderer == lastRenderer)
            {
                targetAlpha = cachedAlpha;
                needTransparent = cachedNeedTransparent;
            }
            else
            {
                if (sliderForcesOpaque)
                {
                    targetAlpha = 1f;
                    needTransparent = false;
                }
                else
                {
                    targetAlpha = ComputeAlphaForRenderer(cm.renderer, camPos, camForward, sliderAlpha, nearD, farD);
                    needTransparent = targetAlpha < 0.999f;
                }
                lastRenderer = cm.renderer;
                cachedAlpha = targetAlpha;
                cachedNeedTransparent = needTransparent;
            }

            // Skip if no change since last frame (saves a lot of material work)
            if (Mathf.Approximately(targetAlpha, cm.lastAppliedAlpha) &&
                cm.isCurrentlyTransparent == needTransparent)
                continue;

            ApplyTransparencyToMaterial(ref cm, targetAlpha, needTransparent, i);
            _originalBuildingMaterials[i] = cm;
        }
    }

    private float ComputeAlphaForRenderer(Renderer renderer, Vector3 camPos, Vector3 camForward,
        float sliderAlpha, float nearD, float farD)
    {
        Vector3 boundsCenter = renderer.bounds.center;
        float distance = Vector3.Distance(boundsCenter, camPos);

        // Skip elements that are behind the user
        Vector3 toRenderer = boundsCenter - camPos;
        if (Vector3.Dot(toRenderer, camForward) < 0f)
            return 1f;

        // Optionally only fade elements between the user and the module center
        if (onlyFadeOccluders)
        {
            float distToCenter = Vector3.Distance(camPos, _moduleCenterCached);
            if (distance > distToCenter + 0.1f)
                return 1f;
        }

        // Distance fade: t=0 at nearD → use slider alpha; t=1 at farD → opaque
        float t = Mathf.InverseLerp(nearD, farD, distance);
        float alpha = Mathf.Lerp(sliderAlpha, 1f, t);
        return Mathf.Max(alpha, minAlphaThreshold);
    }

    private void ApplyTransparencyToMaterial(ref BuildingComponentMaterial cm, float alpha, bool needTransparent, int sortingIndex)
    {
        Material mat = cm.instanceMaterial;

        Color color = cm.originalColor;
        color.a = alpha;
        SetMaterialColor(mat, color);

        if (needTransparent)
        {
            if (!cm.isCurrentlyTransparent)
            {
                SetupTransparentMaterial(mat, sortingIndex);
                if (enableDoubleSidedRendering)
                    mat.SetInt(CullId, (int)UnityEngine.Rendering.CullMode.Off);
                if (disableShadowsWhenTransparent && cm.renderer != null)
                    cm.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _transparentCount++;
            }
            cm.isCurrentlyTransparent = true;
        }
        else
        {
            if (cm.isCurrentlyTransparent)
            {
                RestoreOpaqueMaterial(mat, cm);
                mat.SetInt(CullId, cm.originalCullMode);
                if (cm.renderer != null)
                    cm.renderer.shadowCastingMode = cm.originalShadowMode;
                _transparentCount--;
            }
            cm.isCurrentlyTransparent = false;
        }

        cm.lastAppliedAlpha = alpha;
    }

    private void EnsureCameraCached()
    {
        if (_cachedCameraTransform != null && _cachedCameraTransform.gameObject.activeInHierarchy)
            return;

        var cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        _cachedCameraTransform = cam != null ? cam.transform : null;
    }

    private Color GetMaterialColor(Material mat)
    {
        foreach (int id in ColorPropertyIds)
        {
            if (mat.HasProperty(id)) return mat.GetColor(id);
        }
        return Color.white;
    }

    private void SetMaterialColor(Material mat, Color color)
    {
        foreach (int id in ColorPropertyIds)
        {
            if (mat.HasProperty(id)) mat.SetColor(id, color);
        }
    }

    private float GetMaterialFloat(Material mat, string propertyName, float defaultValue)
    {
        return mat.HasProperty(propertyName) ? mat.GetFloat(propertyName) : defaultValue;
    }

    private int GetMaterialInt(Material mat, string propertyName, int defaultValue)
    {
        return mat.HasProperty(propertyName) ? mat.GetInt(propertyName) : defaultValue;
    }

    private bool IsUIComponent(GameObject obj)
    {
        if (obj.GetComponentInParent<Canvas>() != null) return true;
        if (obj.GetComponent<Graphic>() != null ||
            obj.GetComponent<Selectable>() != null ||
            obj.GetComponent<Canvas>() != null) return true;
        return false;
    }

    public void SetBuildingTransparency(float alpha)
    {
        _currentSliderAlpha = Mathf.Clamp01(alpha);
        ApplyDistanceBasedTransparency(_currentSliderAlpha);
    }

    private void SetupTransparentMaterial(Material mat, int sortingIndex)
    {
        if (mat.HasProperty(ModeId)) mat.SetFloat(ModeId, 3);
        mat.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt(ZWriteId, 0);
        mat.SetInt(ZTestId, (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + (sortingIndex * renderQueueSpacing);

        if (mat.HasProperty(AlphaClipId)) mat.SetFloat(AlphaClipId, 0);
        if (mat.HasProperty(SurfaceId)) mat.SetFloat(SurfaceId, 1); // URP transparent
    }

    private void RestoreOpaqueMaterial(Material mat, BuildingComponentMaterial cm)
    {
        if (mat.HasProperty(ModeId)) mat.SetFloat(ModeId, cm.originalRenderingMode);
        mat.SetInt(SrcBlendId, cm.originalSrcBlend);
        mat.SetInt(DstBlendId, cm.originalDstBlend);
        mat.SetInt(ZWriteId, cm.originalZWrite);
        mat.SetInt(ZTestId, cm.originalZTest);

        if (cm.originalAlphaTest) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
        if (cm.originalAlphaBlend) mat.EnableKeyword("_ALPHABLEND_ON"); else mat.DisableKeyword("_ALPHABLEND_ON");
        if (cm.originalAlphaPremultiply) mat.EnableKeyword("_ALPHAPREMULTIPLY_ON"); else mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        mat.renderQueue = cm.originalRenderQueue;
        if (mat.HasProperty(SurfaceId)) mat.SetFloat(SurfaceId, 0);
    }

    private void RestoreOriginalBuildingMaterials()
    {
        if (_originalBuildingMaterials == null) return;

        var rendererGroups = _originalBuildingMaterials.GroupBy(m => m.renderer);
        foreach (var group in rendererGroups)
        {
            var renderer = group.Key;
            if (renderer == null) continue;

            var current = renderer.sharedMaterials;
            var materialsArray = new Material[current.Length];
            for (int i = 0; i < materialsArray.Length; i++)
                materialsArray[i] = current[i];

            foreach (var cm in group)
            {
                if (cm.materialIndex < materialsArray.Length)
                    materialsArray[cm.materialIndex] = cm.originalMaterial;
                renderer.shadowCastingMode = cm.originalShadowMode;
            }
            renderer.materials = materialsArray;
        }

        // Destroy instanced materials
        foreach (var cm in _originalBuildingMaterials)
        {
            if (cm.instanceMaterial != null && cm.instanceMaterial != cm.originalMaterial)
            {
                if (Application.isPlaying) Destroy(cm.instanceMaterial);
                else DestroyImmediate(cm.instanceMaterial);
            }
        }

        _originalBuildingMaterials = null;
        _transparentCount = 0;
    }

    // UI Event Handlers
    private void OnSliderValueChanged(float value)
    {
        SetBuildingTransparency(value);
    }

    public void ResetBuildingTransparency()
    {
        RestoreOriginalBuildingMaterials();
        CacheBuildingComponentMaterials();
        _currentSliderAlpha = 1f;
        ApplyDistanceBasedTransparency(1f);
        if (transparencySlider != null)
            transparencySlider.value = 1f;
    }

    // Public API for external systems
    public void SetBuildingComponentTransparency(float alpha) => SetBuildingTransparency(alpha);

    public void ToggleBuildingVisibility()
    {
        float current = transparencySlider != null ? transparencySlider.value : _currentSliderAlpha;
        float newAlpha = current > 0.5f ? 0.3f : 1.0f;
        SetBuildingTransparency(newAlpha);
        if (transparencySlider != null)
            transparencySlider.value = newAlpha;
    }

    [ContextMenu("Force Recache Materials")]
    public void ForceRecacheMaterials()
    {
        RestoreOriginalBuildingMaterials();
        CacheBuildingComponentMaterials();
        ApplyDistanceBasedTransparency(_currentSliderAlpha);
    }

    [ContextMenu("Debug Material Info")]
    public void DebugMaterialInformation()
    {
        if (_originalBuildingMaterials == null)
        {
            Debug.Log("[Transparency Debug] No materials cached");
            return;
        }
        Debug.Log($"[Transparency Debug] Total materials: {_originalBuildingMaterials.Length}");
        int transparentCount = _originalBuildingMaterials.Count(m => m.isCurrentlyTransparent);
        Debug.Log($"[Transparency Debug] Currently transparent: {transparentCount}");
    }
}
