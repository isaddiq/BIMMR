using System.Collections.Generic;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Visual feedback for selection and inspection status.
    ///
    /// The highlighter applies an overlay color to a BIM element by swapping
    /// in a runtime-generated material. The ORIGINAL materials are cached so
    /// they can be restored when selection/status is cleared. We never
    /// permanently overwrite shared materials on the asset.
    /// </summary>
    public class BIMInspectionHighlighter : MonoBehaviour
    {
        [Header("Selection")]
        [Tooltip("Color applied to the currently selected element.")]
        public Color selectionColor = new Color(1f, 0.85f, 0.1f, 0.55f);

        [Header("Status Colors")]
        public Color notInspectedColor = new Color(0.6f, 0.6f, 0.6f, 0.35f);
        public Color inProgressColor   = new Color(0.2f, 0.5f, 1.0f, 0.45f);
        public Color passedColor       = new Color(0.2f, 0.85f, 0.3f, 0.45f);
        public Color failedColor       = new Color(0.95f, 0.15f, 0.15f, 0.55f);
        public Color needsReviewColor  = new Color(1.0f, 0.65f, 0.0f, 0.5f);

        // Original sharedMaterial arrays per renderer so we can restore them.
        private readonly Dictionary<Renderer, Material[]> _originalMaterials =
            new Dictionary<Renderer, Material[]>();

        // Which GameObjects are currently displayed with a status color.
        private readonly HashSet<GameObject> _statusColoredObjects = new HashSet<GameObject>();

        // The single currently selected GameObject (one at a time).
        private GameObject _selected;

        // Optional manager used to resolve stable ids back to GameObjects when
        // the single-arg <see cref="RefreshAllStatusColors"/> overload is used.
        [HideInInspector] public BIMInspectionManager manager;

        /// <summary>Apply the selection highlight to the element.</summary>
        public void HighlightSelection(GameObject element)
        {
            if (element == _selected) return;
            ClearSelectionHighlight();
            if (element == null) return;

            _selected = element;
            ApplyOverlay(element, selectionColor);
        }

        /// <summary>Restore the previous look of the selected element.</summary>
        public void ClearSelectionHighlight()
        {
            if (_selected == null) return;

            // If the element also has a status color we want to keep, re-apply
            // it after restoring originals.
            RestoreOriginal(_selected);
            _selected = null;
        }

        /// <summary>Apply a color overlay matching the inspection status.</summary>
        public void ApplyStatusColor(GameObject element, InspectionStatus status)
        {
            if (element == null) return;

            Color c = ColorFor(status);
            ApplyOverlay(element, c);
            _statusColoredObjects.Add(element);
        }

        /// <summary>
        /// Bulk-refresh: apply every record's status color. Records that no
        /// longer point at a tracked element are skipped silently. The caller
        /// supplies a way to resolve a stable id back to a GameObject — see
        /// <see cref="BIMInspectionManager"/>.
        /// </summary>
        public void RefreshAllStatusColors(Dictionary<string, InspectionRecord> records,
                                           System.Func<string, GameObject> resolver)
        {
            if (records == null || resolver == null) return;
            foreach (var kvp in records)
            {
                var go = resolver(kvp.Key);
                if (go == null) continue;
                ApplyStatusColor(go, kvp.Value.status);
            }
        }

        /// <summary>Spec-compatible overload. Uses the assigned
        /// <see cref="manager"/> (set by the bootstrapper) to resolve stable
        /// ids back to GameObjects.</summary>
        public void RefreshAllStatusColors(Dictionary<string, InspectionRecord> records)
        {
            if (manager == null)
            {
                Debug.LogWarning("[BIM Inspection] Highlighter has no manager assigned; " +
                                 "cannot resolve stable ids in RefreshAllStatusColors.");
                return;
            }
            RefreshAllStatusColors(records, manager.ResolveStableId);
        }

        public Color ColorFor(InspectionStatus s)
        {
            switch (s)
            {
                case InspectionStatus.InProgress:   return inProgressColor;
                case InspectionStatus.Passed:       return passedColor;
                case InspectionStatus.Failed:       return failedColor;
                case InspectionStatus.NeedsReview:  return needsReviewColor;
                default:                            return notInspectedColor;
            }
        }

        // --- internal -------------------------------------------------------

        private void ApplyOverlay(GameObject element, Color color)
        {
            var renderers = element.GetComponentsInChildren<Renderer>(includeInactive: false);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!_originalMaterials.ContainsKey(r))
                {
                    // Copy sharedMaterials (NOT .materials) to avoid instantiating.
                    var shared = r.sharedMaterials;
                    var copy = new Material[shared.Length];
                    System.Array.Copy(shared, copy, shared.Length);
                    _originalMaterials[r] = copy;
                }

                var overlay = CreateOverlayMaterial(color);
                var arr = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = overlay;
                r.materials = arr; // uses material instances, safe to overwrite.
            }
        }

        private void RestoreOriginal(GameObject element)
        {
            var renderers = element.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (_originalMaterials.TryGetValue(r, out var originals))
                {
                    r.sharedMaterials = originals;
                    _originalMaterials.Remove(r);
                }
            }
            _statusColoredObjects.Remove(element);
        }

        /// <summary>Build a simple transparent overlay material that works on
        /// the built-in render pipeline AND URP without a custom shader.</summary>
        private Material CreateOverlayMaterial(Color color)
        {
            // Try URP/Lit first, then standard. If neither exists fall back to
            // Unlit/Color so we never throw.
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Unlit/Color");

            var m = new Material(sh) { color = color };

            // Best-effort transparency setup for Standard / URP-Lit.
            if (m.HasProperty("_Mode"))     m.SetFloat("_Mode", 3f); // Transparent
            if (m.HasProperty("_Surface"))  m.SetFloat("_Surface", 1f); // URP: Transparent
            if (m.HasProperty("_Color"))    m.SetColor("_Color", color);
            if (m.HasProperty("_BaseColor"))m.SetColor("_BaseColor", color);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            return m;
        }
    }
}
