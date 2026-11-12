// ModuleCenterCalculator.cs
using UnityEngine;

[ExecuteInEditMode]
public class ModuleCenterCalculator : MonoBehaviour
{
    [Header("Center Calculation")]
    [SerializeField] private Vector3 calculatedCenter;
    [SerializeField] private bool useCustomCenter = false;
    [SerializeField] private Vector3 customCenter = Vector3.zero;

    [Header("Settings")]
    public bool includeMeshRenderers = true;
    public bool includeSkinnedMeshRenderers = true;
    public bool excludeUIElements = true;
    public bool excludeInactiveObjects = true;

    [Header("Gizmos")]
    [SerializeField] private bool showCenterGizmo = true;
    [SerializeField] private float gizmoSize = 0.1f;
    [SerializeField] private Color gizmoColor = Color.red;

    private int meshCount = 0;
    private Vector3 boundingBoxSize = Vector3.zero;
    private Bounds lastCalculatedBounds;

    /// <summary>
    /// World-space pivot for rotation.
    /// </summary>
    public Vector3 GetModuleCenter()
    {
        if (useCustomCenter)
            return transform.TransformPoint(customCenter);
        return transform.TransformPoint(calculatedCenter);
    }

    /// <summary>
    /// Local-space origin used for scaling adjustments.
    /// </summary>
    public Vector3 GetLocalCenter()
    {
        return useCustomCenter ? customCenter : calculatedCenter;
    }

    /// <summary>
    /// (Re)compute the mesh-based center.
    /// </summary>
    [ContextMenu("Calculate Center")]
    public void CalculateCenter()
    {
        var combined = new Bounds();
        meshCount = 0;

        if (includeMeshRenderers)
        {
            foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!ShouldIncludeRenderer(mr)) continue;
                if (!mr.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;

                // Mesh bounds -> module local space
                Matrix4x4 toLocal = transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;
                EncapsulateTransformed(ref combined, mf.sharedMesh.bounds, toLocal);
            }
        }

        if (includeSkinnedMeshRenderers)
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!ShouldIncludeRenderer(smr)) continue;

                // World-space bounds -> module local space
                EncapsulateTransformed(ref combined, smr.bounds, transform.worldToLocalMatrix);
            }
        }

        if (meshCount == 0)
        {
            Debug.LogWarning($"ModuleCenterCalculator: no renderers found on {name}");
            calculatedCenter = Vector3.zero;
            boundingBoxSize = Vector3.zero;
            return;
        }

        calculatedCenter = combined.center;
        boundingBoxSize = combined.size;
        lastCalculatedBounds = combined;
    }

    /// <summary>
    /// Transforms all 8 corners of <paramref name="source"/> so rotated children produce
    /// correct (non-inverted) bounds, then grows <paramref name="combined"/> to include them.
    /// </summary>
    private void EncapsulateTransformed(ref Bounds combined, Bounds source, Matrix4x4 matrix)
    {
        Vector3 c = source.center;
        Vector3 e = source.extents;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                c.x + ((i & 1) == 0 ? -e.x : e.x),
                c.y + ((i & 2) == 0 ? -e.y : e.y),
                c.z + ((i & 4) == 0 ? -e.z : e.z));
            Vector3 p = matrix.MultiplyPoint3x4(corner);

            if (meshCount == 0 && i == 0)
                combined = new Bounds(p, Vector3.zero);
            else
                combined.Encapsulate(p);
        }

        meshCount++;
    }

    private bool ShouldIncludeRenderer(Renderer r)
    {
        if (excludeInactiveObjects && !r.gameObject.activeInHierarchy)
            return false;
        if (excludeUIElements)
        {
            if (r.GetComponentInParent<Canvas>() != null) return false;
            if (r.TryGetComponent<TMPro.TextMeshPro>(out _) ||
                r.TryGetComponent<TMPro.TextMeshProUGUI>(out _))
                return false;
        }
        return true;
    }

    private void OnDrawGizmos()
    {
        if (!showCenterGizmo) return;
        Vector3 c = GetModuleCenter();
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(c, gizmoSize);
    }
}
