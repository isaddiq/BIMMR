using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Adds <see cref="MeshCollider"/>s to BIM elements that have a renderable
    /// mesh but no collider. Without colliders the raycast-based selector
    /// can't pick the element.
    ///
    /// Usage: assign <see cref="bimModelRoots"/> in the inspector (or via the
    /// bootstrapper) and call <see cref="AddMissingColliders"/> from an
    /// inspector button or at runtime.
    /// </summary>
    public class BIMInspectionColliderUtility : MonoBehaviour
    {
        [Tooltip("Root transforms of the BIM models to scan.")]
        public Transform[] bimModelRoots;

        [Tooltip("If true, generated MeshColliders are non-convex (accurate).")]
        public bool nonConvex = true;

        [Tooltip("Skip objects whose bounding box is smaller than this size on " +
                 "any axis. Useful to ignore tiny decorative objects.")]
        public float minimumBoundsSize = 0.02f; // ~2 cm

        [Tooltip("If true, ignore objects that already have ANY Collider component.")]
        public bool skipObjectsWithExistingCollider = true;

        /// <summary>Scan <see cref="bimModelRoots"/> and add MeshColliders where missing.
        /// Returns the number of colliders that were added.</summary>
        public int AddMissingColliders()
        {
            if (bimModelRoots == null || bimModelRoots.Length == 0)
            {
                Debug.LogWarning("[BIM Inspection] No BIM model roots assigned; nothing to do.");
                return 0;
            }

            int added = 0;
            foreach (var root in bimModelRoots)
            {
                if (root == null) continue;
                var filters = root.GetComponentsInChildren<MeshFilter>(includeInactive: false);
                foreach (var mf in filters)
                {
                    if (mf == null || mf.sharedMesh == null) continue;

                    var go = mf.gameObject;
                    if (skipObjectsWithExistingCollider && go.GetComponent<Collider>() != null)
                        continue;

                    // Require a visible renderer.
                    var rend = go.GetComponent<MeshRenderer>();
                    if (rend == null || !rend.enabled) continue;

                    // Size threshold.
                    var size = rend.bounds.size;
                    if (size.x < minimumBoundsSize &&
                        size.y < minimumBoundsSize &&
                        size.z < minimumBoundsSize) continue;

                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = !nonConvex ? true : false;
                    added++;
                }
            }

            Debug.Log($"[BIM Inspection] Added {added} MeshCollider(s) to BIM elements.");
            return added;
        }
    }
}
