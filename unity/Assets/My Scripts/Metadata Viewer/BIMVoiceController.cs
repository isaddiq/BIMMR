// BIMVoiceController.cs - Updated for unified metadata support
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using ArchiBIM.MR;

namespace ArchiBIM.Voice
{
    public class BIMVoiceController : MonoBehaviour
    {
        [Header("Core BIM Integration")]
        [SerializeField] private ArchiBIM.MR.BIMMetadataViewer metadataViewer;

        [Header("XR Controller Configuration")]
        [SerializeField] private bool enableHapticFeedback = true;
        [SerializeField] private bool enableAudioFeedback = true;
        [SerializeField] private float hapticIntensity = 0.3f;
        [SerializeField] private float hapticDuration = 0.2f;

        [Header("Component Selection Feedback")]
        [SerializeField] private bool enableSelectionFeedback = true;
        [SerializeField] private bool enablePropertyFeedback = true;
        [SerializeField] private bool enableDebugLogging = true;

        [Header("Building System Navigation")]
        [SerializeField] private bool enableSystemFiltering = true;
        [SerializeField] private ModuleManager moduleManager;

        // Events for system integration
        public event Action<GameObject, IMetadataProvider> OnBIMComponentSelected;
        public event Action<string, string> OnBIMPropertyViewed;
        public event Action<string> OnBuildingSystemActivated;

        // XR controller references
        private InputDevice rightController;
        private InputDevice leftController;

        // Component interaction state
        private IMetadataProvider lastSelectedMetadata;
        private string currentBuildingSystem;

        // Building system keywords for filtering
        private readonly Dictionary<string, string[]> buildingSystemKeywords = new Dictionary<string, string[]>
        {
            ["Structural"] = new[] { "structural", "structure", "load bearing", "column", "beam", "foundation", "slab", "floor" },
            ["Architectural"] = new[] { "architectural", "wall", "door", "window", "facade", "envelope", "cladding" },
            ["MEP"] = new[] { "mep", "mechanical", "electrical", "plumbing", "hvac", "duct", "pipe", "conduit" },
            ["Fire Safety"] = new[] { "fire", "sprinkler", "alarm", "emergency", "safety", "suppression" }
        };

        private void Start()
        {
            InitializeController();
            ConnectToMetadataViewer();
            InitializeXRControllers();
        }

        private void Update()
        {
            UpdateXRControllerReferences();
        }

        #region Initialization

        private void InitializeController()
        {
            if (metadataViewer == null)
                metadataViewer = FindFirstObjectByType<BIMMetadataViewer>();

            if (moduleManager == null)
                moduleManager = FindAnyObjectByType<ModuleManager>();

            if (metadataViewer == null)
            {
                Debug.LogError("[BIM Voice Controller] BIMMetadataViewer not found - controller disabled");
                enabled = false;
                return;
            }

            if (enableDebugLogging)
                Debug.Log("[BIM Voice Controller] Controller initialized successfully");
        }

        private void ConnectToMetadataViewer()
        {
            if (metadataViewer != null)
            {
                metadataViewer.OnElementSelected += HandleComponentSelected;
                metadataViewer.OnPropertyViewed += HandlePropertyViewed;
                metadataViewer.OnSelectionCleared += HandleComponentDeselected;

                if (enableDebugLogging)
                    Debug.Log("[BIM Voice Controller] Connected to metadata viewer events");
            }
        }

        private void InitializeXRControllers()
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevices(devices);

            foreach (var device in devices)
            {
                if (device.characteristics.HasFlag(InputDeviceCharacteristics.Right))
                    rightController = device;
                else if (device.characteristics.HasFlag(InputDeviceCharacteristics.Left))
                    leftController = device;
            }
        }

        private void UpdateXRControllerReferences()
        {
            if (!rightController.isValid)
                rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            if (!leftController.isValid)
                leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        }

        #endregion

        #region Component Selection Events

        private void HandleComponentSelected(GameObject component, IMetadataProvider metadataProvider)
        {
            lastSelectedMetadata = metadataProvider;
            currentBuildingSystem = GetComponentBuildingSystem(metadataProvider);

            if (enableSelectionFeedback)
                TriggerComponentSelectionFeedback();

            if (enableDebugLogging)
            {
                string elementType = GetElementTypeFromMetadata(metadataProvider);
                int propertyCount = GetValidPropertyCount(metadataProvider);

                Debug.Log($"[BIM Voice Controller] Component selected: {component.name}");
                Debug.Log($"[BIM Voice Controller] Element Type: {elementType}");
                Debug.Log($"[BIM Voice Controller] Building System: {currentBuildingSystem}");
                Debug.Log($"[BIM Voice Controller] Valid Properties: {propertyCount}");
                Debug.Log($"[BIM Voice Controller] Metadata Source: {metadataProvider.GetComponentTypeName()}");

                // Debug property details
                PrintMetadataDetails(metadataProvider);
            }

            OnBIMComponentSelected?.Invoke(component, metadataProvider);

            // Announce selection
            if (!string.IsNullOrEmpty(currentBuildingSystem))
                OnBuildingSystemActivated?.Invoke(currentBuildingSystem);
        }

        private void HandlePropertyViewed(string propertyKey, string propertyValue)
        {
            if (enablePropertyFeedback)
                TriggerPropertyViewFeedback();

            if (enableDebugLogging)
                Debug.Log($"[BIM Voice Controller] Property viewed: {propertyKey} = {propertyValue}");

            OnBIMPropertyViewed?.Invoke(propertyKey, propertyValue);
        }

        private void HandleComponentDeselected()
        {
            lastSelectedMetadata = null;
            currentBuildingSystem = null;

            if (enableDebugLogging)
                Debug.Log("[BIM Voice Controller] Component deselected");
        }

        #endregion

        #region Enhanced Metadata Access

        /// <summary>
        /// Get count of valid (non-empty) properties from IMetadataProvider
        /// </summary>
        private int GetValidPropertyCount(IMetadataProvider metadataProvider)
        {
            var properties = metadataProvider?.GetProperties();
            if (properties == null) return 0;

            return properties.Count(p => !string.IsNullOrEmpty(p.Key) && !string.IsNullOrEmpty(p.Value));
        }

        /// <summary>
        /// Get all valid properties as dictionary from IMetadataProvider
        /// </summary>
        private Dictionary<string, string> GetValidProperties(IMetadataProvider metadataProvider)
        {
            var validProperties = new Dictionary<string, string>();
            var properties = metadataProvider?.GetProperties();

            if (properties == null) return validProperties;

            foreach (var property in properties)
            {
                if (!string.IsNullOrEmpty(property.Key) && !string.IsNullOrEmpty(property.Value))
                {
                    validProperties[property.Key] = property.Value;
                }
            }

            return validProperties;
        }

        /// <summary>
        /// Debug print metadata details for troubleshooting
        /// </summary>
        private void PrintMetadataDetails(IMetadataProvider metadataProvider)
        {
            if (!enableDebugLogging || metadataProvider == null) return;

            var gameObject = metadataProvider.GetGameObject();
            var properties = metadataProvider.GetProperties();

            Debug.Log($"[BIM Voice Controller] === METADATA DETAILS for {gameObject.name} ===");
            Debug.Log($"[BIM Voice Controller] Metadata Type: {metadataProvider.GetComponentTypeName()}");
            Debug.Log($"[BIM Voice Controller] Total Properties: {properties?.Count ?? 0}");

            if (properties != null)
            {
                var validProps = GetValidProperties(metadataProvider);
                Debug.Log($"[BIM Voice Controller] Valid Properties: {validProps.Count}");

                foreach (var prop in validProps.Take(5)) // Show first 5 properties
                {
                    Debug.Log($"[BIM Voice Controller] - {prop.Key}: {prop.Value}");
                }

                if (validProps.Count > 5)
                    Debug.Log($"[BIM Voice Controller] ... and {validProps.Count - 5} more properties");
            }
        }

        #endregion

        #region Enhanced Building System Analysis

        private string GetComponentBuildingSystem(IMetadataProvider metadataProvider)
        {
            var properties = metadataProvider?.GetProperties();
            if (properties == null) return "Unknown System";

            // Check direct building system properties first
            var systemProperties = new[] { "Building_System", "System_Type", "MEP_System", "Structural_System", "Architectural_System" };

            foreach (var sysProp in systemProperties)
            {
                var systemValue = GetPropertyValue(metadataProvider, sysProp);
                if (!string.IsNullOrEmpty(systemValue))
                    return systemValue;
            }

            // Analyze element type for system classification
            string elementType = GetElementTypeFromMetadata(metadataProvider);
            if (string.IsNullOrEmpty(elementType))
            {
                // Try to get from object name
                elementType = metadataProvider.GetGameObject().name;
            }

            elementType = elementType.ToLower();

            // Check against building system keywords
            foreach (var system in buildingSystemKeywords)
            {
                foreach (var keyword in system.Value)
                {
                    if (elementType.Contains(keyword))
                        return system.Key;
                }
            }

            // Check properties for system indicators
            var validProperties = GetValidProperties(metadataProvider);
            foreach (var prop in validProperties)
            {
                string combinedText = $"{prop.Key} {prop.Value}".ToLower();

                foreach (var system in buildingSystemKeywords)
                {
                    foreach (var keyword in system.Value)
                    {
                        if (combinedText.Contains(keyword))
                            return system.Key;
                    }
                }
            }

            return "General Building";
        }

        /// <summary>
        /// Get property value from IMetadataProvider with proper null checking
        /// </summary>
        private string GetPropertyValue(IMetadataProvider metadataProvider, string propertyKey)
        {
            var properties = metadataProvider?.GetProperties();
            if (properties == null) return "";

            var property = properties.FirstOrDefault(p =>
                p.Key != null && p.Key.Equals(propertyKey, StringComparison.OrdinalIgnoreCase));

            return property?.Value ?? "";
        }

        /// <summary>
        /// Enhanced element type extraction with fallback options
        /// </summary>
        private string GetElementTypeFromMetadata(IMetadataProvider metadataProvider)
        {
            var properties = metadataProvider?.GetProperties();
            if (properties == null) return "Unknown Element";

            // Check common BIM property keys for element type
            string[] typeKeys = { "ElementType", "Element_Type", "Type", "Category", "Family", "IfcType", "ComponentType", "Classification" };

            foreach (string key in typeKeys)
            {
                string value = GetPropertyValue(metadataProvider, key);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            // Check for element name property
            string elementName = GetPropertyValue(metadataProvider, "Element_Name") ??
                               GetPropertyValue(metadataProvider, "Component_Name") ??
                               GetPropertyValue(metadataProvider, "Name");

            if (!string.IsNullOrEmpty(elementName))
                return elementName;

            // Fallback to object name analysis
            string objectName = metadataProvider.GetGameObject().name.ToLower();

            if (objectName.Contains("wall")) return "Wall Element";
            if (objectName.Contains("door")) return "Door Element";
            if (objectName.Contains("window")) return "Window Element";
            if (objectName.Contains("beam")) return "Structural Beam";
            if (objectName.Contains("column")) return "Structural Column";
            if (objectName.Contains("floor") || objectName.Contains("slab")) return "Floor Element";
            if (objectName.Contains("roof")) return "Roof Element";
            if (objectName.Contains("panel")) return "Building Panel";
            if (objectName.Contains("pipe")) return "MEP Piping";
            if (objectName.Contains("duct")) return "HVAC Ductwork";
            if (objectName.Contains("fixture")) return "Fixture Element";

            return "Building Component";
        }

        #endregion

        #region XR Feedback Systems

        private void TriggerComponentSelectionFeedback()
        {
            if (enableHapticFeedback)
                TriggerHapticFeedback(hapticIntensity, hapticDuration);

            if (enableAudioFeedback)
                TriggerAudioFeedback("ComponentSelected");
        }

        private void TriggerPropertyViewFeedback()
        {
            if (enableHapticFeedback)
                TriggerHapticFeedback(hapticIntensity * 0.5f, hapticDuration * 0.5f);

            if (enableAudioFeedback)
                TriggerAudioFeedback("PropertyViewed");
        }

        private void TriggerHapticFeedback(float intensity, float duration)
        {
            try
            {
                if (rightController.isValid)
                {
                    rightController.SendHapticImpulse(0, intensity, duration);
                }
                else if (leftController.isValid)
                {
                    leftController.SendHapticImpulse(0, intensity, duration);
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[BIM Voice Controller] Haptic feedback failed: {ex.Message}");
            }
        }

        private void TriggerAudioFeedback(string feedbackType)
        {
            if (enableDebugLogging)
                Debug.Log($"[BIM Voice Controller] Audio feedback: {feedbackType}");
        }

        #endregion

        #region Building System Navigation

        public void FilterByBuildingSystem(string systemName)
        {
            if (!enableSystemFiltering || moduleManager == null) return;

            try
            {
                bool success = moduleManager.SetComponentVisibility(systemName, true);

                if (success)
                {
                    OnBuildingSystemActivated?.Invoke(systemName);
                    if (enableDebugLogging)
                        Debug.Log($"[BIM Voice Controller] Filtered to {systemName} system components");
                }
                else
                {
                    if (enableDebugLogging)
                        Debug.LogWarning($"[BIM Voice Controller] Failed to filter {systemName} system");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BIM Voice Controller] Error filtering building system '{systemName}': {ex.Message}");
            }
        }

        public void ShowAllSystems()
        {
            if (!enableSystemFiltering || moduleManager == null) return;

            try
            {
                foreach (var system in buildingSystemKeywords.Keys)
                {
                    moduleManager.SetComponentVisibility(system, true);
                }

                if (enableDebugLogging)
                    Debug.Log("[BIM Voice Controller] Showing all building system components");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BIM Voice Controller] Error showing all systems: {ex.Message}");
            }
        }

        #endregion

        #region Enhanced Public API

        public void ActivateMetadataViewer()
        {
            if (metadataViewer != null)
            {
                metadataViewer.ActivateViewer();
                TriggerComponentSelectionFeedback();

                if (enableDebugLogging)
                    Debug.Log("[BIM Voice Controller] Metadata viewer activated via controller");
            }
        }

        public void DeactivateMetadataViewer()
        {
            if (metadataViewer != null)
            {
                metadataViewer.DeactivateViewer();
                if (enableDebugLogging)
                    Debug.Log("[BIM Voice Controller] Metadata viewer deactivated via controller");
            }
        }

        public bool IsMetadataViewerActive()
        {
            return metadataViewer != null && metadataViewer.IsActive;
        }

        public IMetadataProvider GetCurrentSelectedMetadata()
        {
            return lastSelectedMetadata;
        }

        public string GetCurrentBuildingSystem()
        {
            return currentBuildingSystem;
        }

        public void ShowMetadataForComponent(string componentName)
        {
            if (metadataViewer != null)
            {
                bool success = metadataViewer.SelectElementByName(componentName);

                if (success && enableSelectionFeedback)
                    TriggerComponentSelectionFeedback();

                if (enableDebugLogging)
                    Debug.Log($"[BIM Voice Controller] Search for component '{componentName}': {(success ? "Found" : "Not Found")}");
            }
        }

        public string GetPropertyValue(string propertyKey)
        {
            if (lastSelectedMetadata != null)
            {
                string value = GetPropertyValue(lastSelectedMetadata, propertyKey);

                if (!string.IsNullOrEmpty(value) && enablePropertyFeedback)
                    TriggerPropertyViewFeedback();

                return value;
            }

            return "";
        }

        public Dictionary<string, string> GetAllProperties()
        {
            if (lastSelectedMetadata != null)
            {
                return GetValidProperties(lastSelectedMetadata);
            }

            return new Dictionary<string, string>();
        }

        #endregion

        #region Enhanced Component Analysis

        public Dictionary<string, int> GetBuildingSystemSummary()
        {
            var summary = new Dictionary<string, int>();

            // Check Metadata components
            var metadataComponents = FindObjectsByType<Metadata>(FindObjectsSortMode.None);
            foreach (var component in metadataComponents)
            {
                var provider = new MetadataProvider(component);
                var system = GetComponentBuildingSystem(provider);

                if (summary.ContainsKey(system))
                    summary[system]++;
                else
                    summary[system] = 1;
            }

            return summary;
        }

        public List<IMetadataProvider> GetComponentsBySystem(string systemName)
        {
            var components = new List<IMetadataProvider>();

            // Check Metadata components
            var metadataComponents = FindObjectsByType<Metadata>(FindObjectsSortMode.None);
            foreach (var component in metadataComponents)
            {
                var provider = new MetadataProvider(component);
                var system = GetComponentBuildingSystem(provider);
                if (system.Equals(systemName, StringComparison.OrdinalIgnoreCase))
                {
                    components.Add(provider);
                }
            }

            return components;
        }

        public void PrintBuildingSystemSummary()
        {
            var summary = GetBuildingSystemSummary();

            BIMLog.Verbose("=== BUILDING SYSTEM SUMMARY ===");
            foreach (var kvp in summary.OrderByDescending(x => x.Value))
            {
                BIMLog.Verbose($"{kvp.Key}: {kvp.Value} components");
            }
            BIMLog.Verbose($"Total Components: {summary.Values.Sum()}");
        }

        public void PrintDetailedMetadataReport()
        {
            var metadataComponents = FindObjectsByType<Metadata>(FindObjectsSortMode.None);

            int totalComponents = metadataComponents.Length;
            int componentsWithMetadata = 0;
            int totalValidProperties = 0;

            BIMLog.Verbose("=== DETAILED METADATA REPORT ===");

            // Process Metadata components
            foreach (var component in metadataComponents)
            {
                var provider = new MetadataProvider(component);
                var validProps = GetValidProperties(provider);
                if (validProps.Count > 0)
                {
                    componentsWithMetadata++;
                    totalValidProperties += validProps.Count;
                }
            }

            BIMLog.Verbose($"Total Components: {totalComponents}");
            BIMLog.Verbose($"Metadata Components: {metadataComponents.Length}");
            BIMLog.Verbose($"Components with Metadata: {componentsWithMetadata}");
            BIMLog.Verbose($"Components without Metadata: {totalComponents - componentsWithMetadata}");
            BIMLog.Verbose($"Total Valid Properties: {totalValidProperties}");
            BIMLog.Verbose($"Average Properties per Component: {(totalValidProperties / (float)Math.Max(1, componentsWithMetadata)):F2}");
        }

        #endregion

        #region Context Menu Testing

        [ContextMenu("Test - Activate Metadata Viewer")]
        private void TestActivateMetadataViewer()
        {
            ActivateMetadataViewer();
        }

        [ContextMenu("Test - Show Building System Summary")]
        private void TestShowBuildingSystemSummary()
        {
            PrintBuildingSystemSummary();
        }

        [ContextMenu("Test - Show Detailed Metadata Report")]
        private void TestShowDetailedMetadataReport()
        {
            PrintDetailedMetadataReport();
        }

        [ContextMenu("Test - Filter Structural Components")]
        private void TestFilterStructuralComponents()
        {
            FilterByBuildingSystem("Structural");
        }

        [ContextMenu("Test - Show All Systems")]
        private void TestShowAllSystems()
        {
            ShowAllSystems();
        }

        [ContextMenu("Test - Haptic Feedback")]
        private void TestHapticFeedback()
        {
            TriggerHapticFeedback(0.5f, 0.3f);
        }

        [ContextMenu("Test - Debug Selected Component")]
        private void TestDebugSelectedComponent()
        {
            if (lastSelectedMetadata != null)
            {
                PrintMetadataDetails(lastSelectedMetadata);
            }
            else
            {
                Debug.Log("[BIM Voice Controller] No component currently selected");
            }
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            if (metadataViewer != null)
            {
                metadataViewer.OnElementSelected -= HandleComponentSelected;
                metadataViewer.OnPropertyViewed -= HandlePropertyViewed;
                metadataViewer.OnSelectionCleared -= HandleComponentDeselected;
            }
        }

        #endregion
    }
}