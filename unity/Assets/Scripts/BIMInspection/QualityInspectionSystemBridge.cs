using System.Collections.Generic;
using UnityEngine;
using BIMMR.Inspection;

namespace ArchiBIM.QualityControl
{
    /// <summary>
    /// Compatibility shim for older project scripts (VoiceCommandGPTBridge,
    /// AECGPTVoiceAssistant) that reference
    /// <c>ArchiBIM.QualityControl.QualityInspectionSystem</c>.
    ///
    /// This class forwards its operations to <see cref="BIMInspectionManager"/>
    /// in the <see cref="BIMMR.Inspection"/> module, so the voice "inspect"
    /// command actually drives the new inspection module.
    ///
    /// Drop this on the same GameObject as <c>BIMInspectionBootstrapper</c>
    /// (or anywhere in the scene). It resolves the manager lazily via
    /// <see cref="Object.FindObjectOfType"/> when no explicit reference is set.
    /// </summary>
    public class QualityInspectionSystem : MonoBehaviour
    {
        [Tooltip("Optional explicit reference to the new BIMInspectionManager. " +
                 "Leave empty to auto-resolve at runtime.")]
        public BIMInspectionManager manager;

        private void Awake()
        {
            if (manager == null) manager = FindObjectOfType<BIMInspectionManager>();
        }

        // ---- API expected by legacy scripts -------------------------------

        /// <summary>Start inspection mode. Called by voice "inspect" commands.</summary>
        public void StartInspection()
        {
            EnsureManager();
            if (manager == null)
            {
                Debug.LogWarning("[QualityInspectionSystem] No BIMInspectionManager " +
                                 "found in the scene. Inspection cannot start.");
                return;
            }
            manager.StartInspectionMode();
        }

        /// <summary>Stop inspection mode.</summary>
        public void StopInspection()
        {
            EnsureManager();
            manager?.StopInspectionMode();
        }

        /// <summary>True while inspection mode is active.</summary>
        public bool IsInspectionActive()
        {
            EnsureManager();
            return manager != null && manager.InspectionModeActive;
        }

        /// <summary>
        /// Returns the GameObjects currently considered "selected" by the
        /// inspection module. The new module tracks a single active selection;
        /// the result list will contain 0 or 1 elements.
        /// </summary>
        public IEnumerable<GameObject> GetSelectedElements()
        {
            EnsureManager();
            var result = new List<GameObject>();
            if (manager != null && manager.SelectedElement != null)
                result.Add(manager.SelectedElement);
            return result;
        }

        // ---- helpers ------------------------------------------------------

        private void EnsureManager()
        {
            if (manager == null) manager = FindObjectOfType<BIMInspectionManager>();
        }
    }
}
