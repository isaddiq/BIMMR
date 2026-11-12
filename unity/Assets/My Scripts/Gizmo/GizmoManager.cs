using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Spawns XYZ translation handles and XYZ rotation rings at the module's center,
/// and keeps them in sync as the module moves.
/// </summary>
public class GizmoManager : MonoBehaviour
{
    [Header("References")]
    public ModuleManager moduleManager;         // your existing ModuleManager
    public Button toggleGizmoButton;            // UI Button to show/hide the gizmo

    [Header("Translate Prefabs")]
    public GameObject translateXPrefab;         // Red X-axis handle prefab
    public GameObject translateYPrefab;         // Green Y-axis handle prefab
    public GameObject translateZPrefab;         // Blue Z-axis handle prefab
    [Tooltip("Distance from center to place translate handles")]
    public float translateHandleOffset = 0.2f;

    [Header("Rotate Prefabs")]
    public GameObject rotateXPrefab;            // Red X-axis rotation curve prefab
    public GameObject rotateYPrefab;            // Green Y-axis rotation curve prefab
    public GameObject rotateZPrefab;            // Blue Z-axis rotation curve prefab

    private GameObject _gizmoRoot;
    private GameObject _gizmoModule;        // module the gizmo was built for
    private Vector3 _moduleLocalCenter;     // center in module-local space, so following is a single TransformPoint

    void Awake()
    {
        if (toggleGizmoButton != null)
            toggleGizmoButton.onClick.AddListener(ToggleGizmo);
    }

    void OnDestroy()
    {
        if (toggleGizmoButton != null)
            toggleGizmoButton.onClick.RemoveListener(ToggleGizmo);
    }

    void ToggleGizmo()
    {
        if (_gizmoRoot != null)
        {
            DestroyGizmo();
            return;
        }

        var module = moduleManager != null ? moduleManager.GetCurrentModule() : null;
        if (module == null) return;

        // Create a parent root at module center
        Vector3 center = ComputeModuleCenter(module);
        _gizmoModule = module;
        _moduleLocalCenter = module.transform.InverseTransformPoint(center);

        _gizmoRoot = new GameObject("ModuleGizmo");
        _gizmoRoot.transform.position = center;
        _gizmoRoot.transform.rotation = Quaternion.identity;

        // --- Translation Handles ---
        SpawnTranslateHandle(translateXPrefab, Vector3.right);
        SpawnTranslateHandle(translateYPrefab, Vector3.up);
        SpawnTranslateHandle(translateZPrefab, Vector3.forward);

        // --- Rotation Handles ---
        SpawnRotateHandle(rotateXPrefab, Quaternion.Euler(0f, 0f, 90f));
        SpawnRotateHandle(rotateYPrefab, Quaternion.identity);
        SpawnRotateHandle(rotateZPrefab, Quaternion.Euler(90f, 0f, 0f));
    }

    void Update()
    {
        if (_gizmoRoot == null) return;

        // Handles are bound to the module they were built for; drop the gizmo if that module is gone or replaced
        var module = moduleManager != null ? moduleManager.GetCurrentModule() : null;
        if (module == null || module != _gizmoModule)
        {
            DestroyGizmo();
            return;
        }

        // Follow the module's movements without re-scanning every renderer each frame
        _gizmoRoot.transform.position = module.transform.TransformPoint(_moduleLocalCenter);
    }

    void DestroyGizmo()
    {
        if (_gizmoRoot != null)
            Destroy(_gizmoRoot);
        _gizmoRoot = null;
        _gizmoModule = null;
    }

    void SpawnTranslateHandle(GameObject prefab, Vector3 axis)
    {
        if (prefab == null) return;

        var go = Instantiate(prefab,
                             Vector3.zero,
                             Quaternion.identity,
                             _gizmoRoot.transform);
        go.transform.localPosition = axis.normalized * translateHandleOffset;
        if (go.TryGetComponent<TranslateHandle>(out var handle))
        {
            handle.module = _gizmoModule;
            handle.axis = axis.normalized;
        }
    }

    void SpawnRotateHandle(GameObject prefab, Quaternion localRot)
    {
        if (prefab == null) return;

        var go = Instantiate(prefab,
                             Vector3.zero,
                             Quaternion.identity,
                             _gizmoRoot.transform);
        go.transform.localRotation = localRot;
        if (go.TryGetComponent<RotateHandle>(out var handle))
            handle.module = _gizmoModule;
    }

    Vector3 ComputeModuleCenter(GameObject module)
    {
        var renderers = module.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return module.transform.position;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return b.center;
    }
}