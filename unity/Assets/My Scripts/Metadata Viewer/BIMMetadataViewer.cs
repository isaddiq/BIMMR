// BIMMetadataViewer.cs - Enhanced with unified metadata support
// REVISED: Only reads metadata from Metadata.cs and IFCMetadata.cs (SemanticEnrichment)
// No longer extracts Unity inspector data, transform properties, or computed values
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.InputSystem;
using BIMUniXchange.SemanticEnrichment;

namespace ArchiBIM.MR
{
    // Unified metadata interface for handling metadata
    public interface IMetadataProvider
    {
        List<global::MetadataProperty> GetProperties();
        string GetComponentTypeName();
        GameObject GetGameObject();
    }

    // Wrapper for Metadata component
    public class MetadataProvider : IMetadataProvider
    {
        private readonly Metadata metadata;

        public MetadataProvider(Metadata metadata)
        {
            this.metadata = metadata;
        }

        public List<global::MetadataProperty> GetProperties()
        {
            return metadata.Properties;
        }
        public string GetComponentTypeName() => "Metadata";
        public GameObject GetGameObject() => metadata.gameObject;
    }

    // Wrapper for IFCMetadata from SemanticEnrichment
    public class SemanticMetadataProvider : IMetadataProvider
    {
        private readonly IFCMetadata semanticMetadata;

        public SemanticMetadataProvider(IFCMetadata semanticMetadata)
        {
            this.semanticMetadata = semanticMetadata;
        }

        public List<global::MetadataProperty> GetProperties()
        {
            var properties = new List<global::MetadataProperty>();

            // Add IFC identification properties
            if (!string.IsNullOrEmpty(semanticMetadata.ElementId))
                properties.Add(new global::MetadataProperty { Key = "ElementId", Value = semanticMetadata.ElementId });
            if (!string.IsNullOrEmpty(semanticMetadata.GlobalId))
                properties.Add(new global::MetadataProperty { Key = "GlobalId", Value = semanticMetadata.GlobalId });
            if (!string.IsNullOrEmpty(semanticMetadata.IfcClass))
                properties.Add(new global::MetadataProperty { Key = "IfcClass", Value = semanticMetadata.IfcClass });
            if (!string.IsNullOrEmpty(semanticMetadata.ElementName))
                properties.Add(new global::MetadataProperty { Key = "ElementName", Value = semanticMetadata.ElementName });
            if (!string.IsNullOrEmpty(semanticMetadata.Description))
                properties.Add(new global::MetadataProperty { Key = "Description", Value = semanticMetadata.Description });
            if (!string.IsNullOrEmpty(semanticMetadata.ObjectType))
                properties.Add(new global::MetadataProperty { Key = "ObjectType", Value = semanticMetadata.ObjectType });

            // Add IFC hierarchy properties
            if (!string.IsNullOrEmpty(semanticMetadata.Project))
                properties.Add(new global::MetadataProperty { Key = "Project", Value = semanticMetadata.Project });
            if (!string.IsNullOrEmpty(semanticMetadata.Site))
                properties.Add(new global::MetadataProperty { Key = "Site", Value = semanticMetadata.Site });
            if (!string.IsNullOrEmpty(semanticMetadata.Building))
                properties.Add(new global::MetadataProperty { Key = "Building", Value = semanticMetadata.Building });
            if (!string.IsNullOrEmpty(semanticMetadata.ParentGlobalId))
                properties.Add(new global::MetadataProperty { Key = "ParentGlobalId", Value = semanticMetadata.ParentGlobalId });
            if (!string.IsNullOrEmpty(semanticMetadata.Storey))
                properties.Add(new global::MetadataProperty { Key = "Storey", Value = semanticMetadata.Storey });
            if (!string.IsNullOrEmpty(semanticMetadata.SpatialPath))
                properties.Add(new global::MetadataProperty { Key = "SpatialPath", Value = semanticMetadata.SpatialPath });

            // Add classification & discipline
            if (!string.IsNullOrEmpty(semanticMetadata.Discipline))
                properties.Add(new global::MetadataProperty { Key = "Discipline", Value = semanticMetadata.Discipline });
            if (!string.IsNullOrEmpty(semanticMetadata.Category))
                properties.Add(new global::MetadataProperty { Key = "Category", Value = semanticMetadata.Category });

            // Add material information
            if (!string.IsNullOrEmpty(semanticMetadata.MaterialName))
                properties.Add(new global::MetadataProperty { Key = "MaterialName", Value = semanticMetadata.MaterialName });

            // Add material layers
            if (semanticMetadata.MaterialLayers != null && semanticMetadata.MaterialLayers.Count > 0)
            {
                properties.Add(new global::MetadataProperty { Key = "MaterialLayers", Value = string.Join(", ", semanticMetadata.MaterialLayers) });
            }

            // Add relationship properties
            if (!string.IsNullOrEmpty(semanticMetadata.FillsVoid))
                properties.Add(new global::MetadataProperty { Key = "FillsVoid", Value = semanticMetadata.FillsVoid });
            if (!string.IsNullOrEmpty(semanticMetadata.Decomposes))
                properties.Add(new global::MetadataProperty { Key = "Decomposes", Value = semanticMetadata.Decomposes });

            // Add list relationships as comma-separated values
            if (semanticMetadata.ContainedElements != null && semanticMetadata.ContainedElements.Count > 0)
                properties.Add(new global::MetadataProperty { Key = "ContainedElements", Value = string.Join(", ", semanticMetadata.ContainedElements) });
            if (semanticMetadata.ConnectedTo != null && semanticMetadata.ConnectedTo.Count > 0)
                properties.Add(new global::MetadataProperty { Key = "ConnectedTo", Value = string.Join(", ", semanticMetadata.ConnectedTo) });
            if (semanticMetadata.ConnectedFrom != null && semanticMetadata.ConnectedFrom.Count > 0)
                properties.Add(new global::MetadataProperty { Key = "ConnectedFrom", Value = string.Join(", ", semanticMetadata.ConnectedFrom) });
            if (semanticMetadata.HasOpenings != null && semanticMetadata.HasOpenings.Count > 0)
                properties.Add(new global::MetadataProperty { Key = "HasOpenings", Value = string.Join(", ", semanticMetadata.HasOpenings) });
            if (semanticMetadata.DecomposedBy != null && semanticMetadata.DecomposedBy.Count > 0)
                properties.Add(new global::MetadataProperty { Key = "DecomposedBy", Value = string.Join(", ", semanticMetadata.DecomposedBy) });
            if (semanticMetadata.AssignedToGroups != null && semanticMetadata.AssignedToGroups.Count > 0)
                properties.Add(new global::MetadataProperty { Key = "AssignedToGroups", Value = string.Join(", ", semanticMetadata.AssignedToGroups) });
            if (semanticMetadata.AssignedToSystems != null && semanticMetadata.AssignedToSystems.Count > 0)
                properties.Add(new global::MetadataProperty { Key = "AssignedToSystems", Value = string.Join(", ", semanticMetadata.AssignedToSystems) });
            if (semanticMetadata.BoundingSpaces != null && semanticMetadata.BoundingSpaces.Count > 0)
                properties.Add(new global::MetadataProperty { Key = "BoundingSpaces", Value = string.Join(", ", semanticMetadata.BoundingSpaces) });

            // Add all properties from property sets
            if (semanticMetadata.Properties != null)
            {
                foreach (var prop in semanticMetadata.Properties)
                {
                    if (!string.IsNullOrEmpty(prop.Key))
                    {
                        properties.Add(new global::MetadataProperty { Key = prop.Key, Value = prop.Value ?? "" });
                    }
                }
            }

            return properties;
        }

        public string GetComponentTypeName() => "IFCMetadata";
        public GameObject GetGameObject() => semanticMetadata.gameObject;
    }

    [DisallowMultipleComponent]
    public class BIMMetadataViewer : MonoBehaviour
    {
        [Header("Module Integration")]
        [SerializeField] private ModuleManager moduleManager;

        [Header("XR Controller Configuration")]
        [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rightRayInteractor;

        [Header("Input Actions")]
        [SerializeField] private InputActionReference triggerAction;
        [SerializeField] private InputActionReference selectAction;

        [Header("UI Components")]
        [SerializeField] private ScrollRect metadataScrollView;
        [SerializeField] private Transform tableRowContainer;
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private GameObject propertyRowPrefab;

        [Header("Enable/Disable Controls")]
        [SerializeField] private Button enableViewerButton;
        [SerializeField] private TMP_Text buttonText;
        [SerializeField] private Image buttonImage;

        [Header("Visual Feedback")]
        [SerializeField] private Color hoverHighlightColor = new Color(0.7f, 0.7f, 1f, 0.5f);
        [SerializeField] private Color selectedHighlightColor = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        [SerializeField] private Color enabledButtonColor = new Color(0.3f, 0.8f, 0.3f);
        [SerializeField] private Color disabledButtonColor = new Color(0.8f, 0.3f, 0.3f);

        [Header("Display Settings")]
        [SerializeField] private bool enablePropertyGrouping = true;
        [SerializeField] private bool showEmptyProperties = true;
        [SerializeField] private bool enableAlternatingRows = true;
        [SerializeField] private int maxValueDisplayLength = 150;
        [SerializeField] private float rowHeight = 35f;

        // Events
        public event Action<GameObject, IMetadataProvider> OnElementSelected;
        public event Action<string, string> OnPropertyViewed;
        public event Action OnSelectionCleared;
        public event Action<bool> OnViewerToggled;

        // Backward compatibility events for existing scripts
        public event Action<GameObject, Metadata> OnMetadataSelected;

        // State management
        private IMetadataProvider currentMetadataProvider;
        private GameObject currentSelectedElement;
        private GameObject currentHoveredElement;
        private Renderer currentSelectedRenderer;
        private Renderer currentHoveredRenderer;
        private Material[] originalSelectedMaterials;
        private Material[] originalHoveredMaterials;
        private bool isViewerEnabled = false;
        private bool isInitialized = false;

        // Module tracking
        private GameObject currentModule;
        private List<GameObject> currentModuleXRObjects = new List<GameObject>();

        // Input tracking
        private bool triggerPressed = false;

        private void Start()
        {
            InitializeMetadataViewer();
            // Register events here (after moduleManager has been found in InitializeMetadataViewer).
            // OnEnable runs before Start and can't see the auto-found moduleManager reference.
            RegisterEvents();
        }

        private void OnEnable()
        {
            EnableInputActions();
            // Only register events if we already initialized (re-enable case).
            if (isInitialized)
                RegisterEvents();
        }

        private void OnDisable()
        {
            DisableInputActions();
            UnregisterEvents();
        }

        private void Update()
        {
            if (isViewerEnabled && isInitialized)
            {
                HandleRayInteraction();
                HandleTriggerInput();
            }
        }

        #region Unified Metadata Detection

        private IMetadataProvider GetMetadataProvider(GameObject obj)
        {
            // Check for Metadata component first
            var metadata = obj.GetComponent<Metadata>();
            if (metadata != null)
                return new MetadataProvider(metadata);

            // Check for IFCMetadata from SemanticEnrichment
            var ifcMetadata = obj.GetComponent<IFCMetadata>();
            if (ifcMetadata != null)
                return new SemanticMetadataProvider(ifcMetadata);

            // Check parent objects for Metadata
            metadata = obj.GetComponentInParent<Metadata>();
            if (metadata != null)
                return new MetadataProvider(metadata);

            // Check parent objects for IFCMetadata from SemanticEnrichment
            ifcMetadata = obj.GetComponentInParent<IFCMetadata>();
            if (ifcMetadata != null)
                return new SemanticMetadataProvider(ifcMetadata);

            return null;
        }

        private bool HasAnyMetadata(GameObject obj)
        {
            return GetMetadataProvider(obj) != null;
        }

        private void FindAllMetadata()
        {
            currentModuleXRObjects.Clear();

            // Find all Metadata components
            var allMetadata = FindObjectsByType<Metadata>(FindObjectsSortMode.None);
            currentModuleXRObjects.AddRange(allMetadata.Select(m => m.gameObject));

            // Find all IFCMetadata components from SemanticEnrichment
            var allIfcMetadata = FindObjectsByType<IFCMetadata>(FindObjectsSortMode.None);
            foreach (var ifcMeta in allIfcMetadata)
            {
                if (!currentModuleXRObjects.Contains(ifcMeta.gameObject))
                {
                    currentModuleXRObjects.Add(ifcMeta.gameObject);
                }
            }
        }

        #endregion

        #region Initialization

        private void InitializeMetadataViewer()
        {
            try
            {
                if (metadataScrollView == null)
                    metadataScrollView = GetComponentInChildren<ScrollRect>();

                if (tableRowContainer == null && metadataScrollView != null)
                    tableRowContainer = metadataScrollView.content;

                if (rightRayInteractor == null)
                    rightRayInteractor = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();

                if (moduleManager == null)
                    moduleManager = FindAnyObjectByType<ModuleManager>();

                SetupScrollViewForVerticalScrolling();
                SetupToggleButton();
                ClearMetadataDisplay();
                ShowWelcomeMessage();
                ConnectToModuleManager();

                isInitialized = true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BIM Metadata Viewer] Initialization failed: {e.Message}");
            }
        }

        private void SetupScrollViewForVerticalScrolling()
        {
            if (metadataScrollView == null || tableRowContainer == null) return;

            // Configure ScrollRect for vertical scrolling only
            metadataScrollView.horizontal = false;
            metadataScrollView.vertical = true;
            metadataScrollView.movementType = ScrollRect.MovementType.Clamped;
            metadataScrollView.scrollSensitivity = 30f;
            metadataScrollView.inertia = true;
            metadataScrollView.decelerationRate = 0.135f;

            // Ensure Content Size Fitter is properly configured
            var contentSizeFitter = tableRowContainer.GetComponent<ContentSizeFitter>();
            if (contentSizeFitter == null)
                contentSizeFitter = tableRowContainer.gameObject.AddComponent<ContentSizeFitter>();

            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Ensure Vertical Layout Group is properly configured
            var verticalLayoutGroup = tableRowContainer.GetComponent<VerticalLayoutGroup>();
            if (verticalLayoutGroup == null)
                verticalLayoutGroup = tableRowContainer.gameObject.AddComponent<VerticalLayoutGroup>();

            verticalLayoutGroup.childAlignment = TextAnchor.UpperCenter;
            verticalLayoutGroup.childControlWidth = true;
            verticalLayoutGroup.childControlHeight = true;
            verticalLayoutGroup.childForceExpandWidth = true;
            verticalLayoutGroup.childForceExpandHeight = false;
            verticalLayoutGroup.spacing = 2f;
            verticalLayoutGroup.padding = new RectOffset(10, 10, 10, 10);

            // Configure content RectTransform
            var contentRect = tableRowContainer.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                contentRect.anchorMin = new Vector2(0, 1);
                contentRect.anchorMax = new Vector2(1, 1);
                contentRect.pivot = new Vector2(0.5f, 1);
                contentRect.offsetMin = new Vector2(0, contentRect.offsetMin.y);
                contentRect.offsetMax = new Vector2(0, contentRect.offsetMax.y);
            }

            // Configure viewport RectTransform if needed
            if (metadataScrollView.viewport != null)
            {
                var viewportRect = metadataScrollView.viewport.GetComponent<RectTransform>();
                if (viewportRect != null)
                {
                    // Add Mask component for proper clipping
                    var mask = metadataScrollView.viewport.GetComponent<Mask>();
                    if (mask == null)
                        mask = metadataScrollView.viewport.gameObject.AddComponent<Mask>();
                    mask.showMaskGraphic = false;
                }
            }
        }

        private void SetupToggleButton()
        {
            if (enableViewerButton == null)
                enableViewerButton = GetComponentInChildren<Button>();

            if (enableViewerButton != null)
            {
                enableViewerButton.onClick.RemoveAllListeners();
                enableViewerButton.onClick.AddListener(ToggleViewer);

                if (buttonText == null)
                    buttonText = enableViewerButton.GetComponentInChildren<TMP_Text>();

                if (buttonImage == null)
                    buttonImage = enableViewerButton.GetComponent<Image>();

                UpdateButtonAppearance();
            }
        }

        private void ConnectToModuleManager()
        {
            if (moduleManager == null)
            {
                FindAllMetadata();
                return;
            }

            currentModule = moduleManager.GetCurrentModule();
            if (currentModule != null)
            {
                RefreshCurrentModuleXRObjects();
            }
            else
            {
                FindAllMetadata();
            }
        }

        private void EnableInputActions()
        {
            if (triggerAction != null)
                triggerAction.action.Enable();
            if (selectAction != null)
                selectAction.action.Enable();
        }

        private void DisableInputActions()
        {
            if (triggerAction != null)
                triggerAction.action.Disable();
            if (selectAction != null)
                selectAction.action.Disable();
        }

        private void RegisterEvents()
        {
            if (moduleManager != null)
            {
                // Prevent double-registration
                moduleManager.OnModuleSpawned -= OnModuleSpawned;
                moduleManager.OnModuleSpawned += OnModuleSpawned;

                ((IBimModelProvider)moduleManager).OnModelUnloaded -= OnModelUnloadedHandler;
                ((IBimModelProvider)moduleManager).OnModelUnloaded += OnModelUnloadedHandler;
            }
        }

        private void UnregisterEvents()
        {
            if (moduleManager != null)
            {
                moduleManager.OnModuleSpawned -= OnModuleSpawned;
                ((IBimModelProvider)moduleManager).OnModelUnloaded -= OnModelUnloadedHandler;
            }
        }

        private void OnModelUnloadedHandler(string modelId)
        {
            // The current module is being destroyed — clear selection and highlights
            // before the renderers/materials become invalid.
            RemoveAllHighlights();
            currentSelectedElement = null;
            currentHoveredElement = null;
            currentSelectedRenderer = null;
            currentHoveredRenderer = null;
            originalSelectedMaterials = null;
            originalHoveredMaterials = null;
            currentMetadataProvider = null;
            currentModuleXRObjects.Clear();
            ClearMetadataDisplay();
            ShowWelcomeMessage();
        }

        #endregion

        #region Toggle Button

        private void ToggleViewer()
        {
            isViewerEnabled = !isViewerEnabled;

            if (!isViewerEnabled)
            {
                RemoveAllHighlights();
                ClearSelection();
                ClearMetadataDisplay();
                ShowWelcomeMessage();
            }
            else
            {
                RefreshCurrentModuleXRObjects();
            }

            UpdateButtonAppearance();
            OnViewerToggled?.Invoke(isViewerEnabled);
        }

        private void UpdateButtonAppearance()
        {
            if (buttonText != null)
            {
                buttonText.text = isViewerEnabled ? "Disable Metadata Viewer" : "Enable Metadata Viewer";
            }

            if (buttonImage != null)
            {
                buttonImage.color = isViewerEnabled ? enabledButtonColor : disabledButtonColor;
            }
        }

        #endregion

        #region Module Integration

        private void OnModuleSpawned(GameObject newModule)
        {
            currentModule = newModule;
            RefreshCurrentModuleXRObjects();
            ClearSelection();
        }

        private void RefreshCurrentModuleXRObjects()
        {
            currentModuleXRObjects.Clear();

            if (currentModule != null)
            {
                if (moduleManager != null)
                {
                    var xrObjects = moduleManager.GetXRInteractableObjects();
                    currentModuleXRObjects.AddRange(xrObjects.Where(obj => obj != null));
                }
                else
                {
                    // Find Metadata components in current module
                    var metadataComponents = currentModule.GetComponentsInChildren<Metadata>(true);
                    currentModuleXRObjects.AddRange(metadataComponents.Select(m => m.gameObject));

                    // Find IFCMetadata from SemanticEnrichment in current module
                    var ifcMetadataComponents = currentModule.GetComponentsInChildren<IFCMetadata>(true);
                    foreach (var ifcMeta in ifcMetadataComponents)
                    {
                        if (!currentModuleXRObjects.Contains(ifcMeta.gameObject))
                        {
                            currentModuleXRObjects.Add(ifcMeta.gameObject);
                        }
                    }
                }
            }
            else
            {
                FindAllMetadata();
            }
        }

        #endregion

        #region XR Interaction Handling

        private void HandleRayInteraction()
        {
            // Prefer the visible right-hand ray: it stops at UI in front of the model.
            RaycastHit hit = default;
            bool hasHit = RightHandRay.Interactor != null
                ? RightHandRay.TryGet3DHit(out hit)
                : rightRayInteractor != null && rightRayInteractor.TryGetCurrent3DRaycastHit(out hit);

            if (hasHit)
            {
                GameObject hitObject = hit.collider.gameObject;

                if (IsValidXRObject(hitObject))
                {
                    if (currentHoveredElement != hitObject)
                    {
                        RemoveHoverHighlight();
                        currentHoveredElement = hitObject;

                        // Only apply hover highlight if not currently selected
                        if (currentSelectedElement != hitObject)
                        {
                            ApplyHoverHighlight(hitObject);
                        }
                    }
                }
                else
                {
                    if (currentHoveredElement != null)
                    {
                        RemoveHoverHighlight();
                        currentHoveredElement = null;
                    }
                }
            }
            else
            {
                if (currentHoveredElement != null)
                {
                    RemoveHoverHighlight();
                    currentHoveredElement = null;
                }
            }
        }

        private void HandleTriggerInput()
        {
            bool currentTriggerState = false;

            if (triggerAction != null)
                currentTriggerState = triggerAction.action.ReadValue<float>() > 0.5f;
            else if (selectAction != null)
                currentTriggerState = selectAction.action.triggered;

            if (currentTriggerState && !triggerPressed)
            {
                if (currentHoveredElement != null)
                {
                    SelectBuildingElement(currentHoveredElement);
                }
            }

            triggerPressed = currentTriggerState;
        }

        private bool IsValidXRObject(GameObject obj)
        {
            if (obj == null) return false;

            // Direct metadata or metadata on a parent (collider may be on a child mesh)
            if (HasAnyMetadata(obj))
                return true;

            // Tracked XR object — even if the hit collider itself has no metadata
            if (currentModuleXRObjects.Contains(obj))
                return true;

            return false;
        }

        #endregion

        #region Element Highlighting

        private void ApplyHoverHighlight(GameObject element)
        {
            var renderer = GetElementRenderer(element);
            if (renderer == null) return;

            currentHoveredRenderer = renderer;
            // Capture sharedMaterials (the original asset refs) so we don't create unwanted instances.
            originalHoveredMaterials = renderer.sharedMaterials;

            var highlightedMaterials = CreateHighlightMaterials(originalHoveredMaterials, hoverHighlightColor);
            renderer.materials = highlightedMaterials;
        }

        private void ApplySelectionHighlight(GameObject element)
        {
            var renderer = GetElementRenderer(element);
            if (renderer == null) return;

            currentSelectedRenderer = renderer;
            originalSelectedMaterials = renderer.sharedMaterials;

            var highlightedMaterials = CreateHighlightMaterials(originalSelectedMaterials, selectedHighlightColor);
            renderer.materials = highlightedMaterials;
        }

        private void RemoveHoverHighlight()
        {
            if (currentHoveredRenderer != null && originalHoveredMaterials != null)
            {
                // Destroy highlight material instances BEFORE replacing them on the renderer.
                var highlightMaterials = currentHoveredRenderer.sharedMaterials;
                currentHoveredRenderer.sharedMaterials = originalHoveredMaterials;
                DestroyMaterialInstances(highlightMaterials, originalHoveredMaterials);
                currentHoveredRenderer = null;
                originalHoveredMaterials = null;
            }
        }

        private void RemoveSelectionHighlight()
        {
            if (currentSelectedRenderer != null && originalSelectedMaterials != null)
            {
                var highlightMaterials = currentSelectedRenderer.sharedMaterials;
                currentSelectedRenderer.sharedMaterials = originalSelectedMaterials;
                DestroyMaterialInstances(highlightMaterials, originalSelectedMaterials);
                currentSelectedRenderer = null;
                originalSelectedMaterials = null;
            }
        }

        private void RemoveAllHighlights()
        {
            RemoveHoverHighlight();
            RemoveSelectionHighlight();
        }

        private Renderer GetElementRenderer(GameObject element)
        {
            var renderer = element.GetComponent<Renderer>();
            if (renderer == null)
                renderer = element.GetComponentInChildren<Renderer>();
            return renderer;
        }

        private Material[] CreateHighlightMaterials(Material[] originalMaterials, Color highlightColor)
        {
            var highlightedMaterials = new Material[originalMaterials.Length];
            for (int i = 0; i < highlightedMaterials.Length; i++)
            {
                if (originalMaterials[i] == null) continue;

                highlightedMaterials[i] = new Material(originalMaterials[i]);
                highlightedMaterials[i].name = originalMaterials[i].name + "_Highlight";

                if (highlightedMaterials[i].HasProperty("_Color"))
                {
                    highlightedMaterials[i].color = Color.Lerp(originalMaterials[i].color, highlightColor, 0.7f);
                }
                else if (highlightedMaterials[i].HasProperty("_BaseColor"))
                {
                    Color baseColor = highlightedMaterials[i].GetColor("_BaseColor");
                    highlightedMaterials[i].SetColor("_BaseColor", Color.Lerp(baseColor, highlightColor, 0.7f));
                }

                if (highlightedMaterials[i].HasProperty("_EmissionColor"))
                {
                    highlightedMaterials[i].SetColor("_EmissionColor", highlightColor * 0.3f);
                    highlightedMaterials[i].EnableKeyword("_EMISSION");
                }
            }
            return highlightedMaterials;
        }

        private void DestroyMaterialInstances(Material[] highlightMaterials, Material[] originalMaterials)
        {
            if (highlightMaterials == null) return;
            foreach (var material in highlightMaterials)
            {
                if (material == null) continue;
                // Only destroy if it's not one of the originals (safety check)
                if (originalMaterials != null && originalMaterials.Contains(material)) continue;

                if (Application.isPlaying)
                    Destroy(material);
                else
                    DestroyImmediate(material);
            }
        }

        #endregion

        #region Element Selection and Metadata Display

        private void SelectBuildingElement(GameObject element)
        {
            var metadataProvider = GetMetadataProvider(element);
            if (metadataProvider == null)
            {
                ShowNoMetadataMessage();
                return;
            }

            // The hit collider may sit on a child mesh while metadata lives on a parent —
            // anchor the selection on the metadata-owning GameObject so highlighting and
            // deselect-on-second-click work consistently.
            var metadataOwner = metadataProvider.GetGameObject();
            if (metadataOwner != null && metadataOwner != element)
                element = metadataOwner;

            // Check if clicking on already selected element (toggle deselect)
            if (currentSelectedElement == element)
            {
                DeselectCurrentElement();
                return;
            }

            // Deselect previous element if any
            if (currentSelectedElement != null)
            {
                RemoveSelectionHighlight();
            }

            // Remove hover highlight since we're selecting
            RemoveHoverHighlight();

            // Select new element
            currentSelectedElement = element;
            currentMetadataProvider = metadataProvider;

            // Apply selection highlight
            ApplySelectionHighlight(element);

            // Display metadata
            DisplayMetadataTable(metadataProvider);
            UpdateHeaderDisplay(element, metadataProvider);

            // Notify events
            OnElementSelected?.Invoke(element, metadataProvider);

            // Fire legacy events for backward compatibility
            if (metadataProvider is MetadataProvider metadataProviderStandard)
            {
                var metadata = element.GetComponent<Metadata>();
                OnMetadataSelected?.Invoke(element, metadata);
            }
        }

        private void DeselectCurrentElement()
        {
            if (currentSelectedElement != null)
            {
                RemoveSelectionHighlight();
                currentSelectedElement = null;
                currentMetadataProvider = null;
                ClearMetadataDisplay();
                ShowWelcomeMessage();
                OnSelectionCleared?.Invoke();
            }
        }

        private void DisplayMetadataTable(IMetadataProvider metadataProvider)
        {
            if (!ValidateUIComponents()) return;

            ClearMetadataDisplay();

            var allProperties = GetAllProperties(metadataProvider);

            if (allProperties.Count == 0)
            {
                ShowNoMetadataMessage();
                return;
            }

            if (metadataScrollView != null)
                Canvas.ForceUpdateCanvases();

            if (enablePropertyGrouping)
                DisplayCategorizedProperties(allProperties);
            else
                DisplayFlatPropertyList(allProperties);

            UpdateStatisticsDisplay(allProperties);
            RefreshScrollView();
        }

        private bool ValidateUIComponents()
        {
            return metadataScrollView != null && tableRowContainer != null && propertyRowPrefab != null;
        }

        private Dictionary<string, string> GetAllProperties(IMetadataProvider metadataProvider)
        {
            var allProperties = new Dictionary<string, string>();

            // Get properties from the metadata provider ONLY
            var properties = metadataProvider.GetProperties();
            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    if (!string.IsNullOrEmpty(prop.Key))
                    {
                        string value = prop.Value ?? "";
                        if (showEmptyProperties || !string.IsNullOrEmpty(value))
                        {
                            allProperties[prop.Key] = value;
                        }
                    }
                }
            }

            // Add metadata source information for reference
            allProperties["Metadata_Source"] = metadataProvider.GetComponentTypeName();

            // Only add the object name for basic identification
            allProperties["Object_Name"] = metadataProvider.GetGameObject().name;

            return allProperties;
        }

        private void DisplayCategorizedProperties(Dictionary<string, string> properties)
        {
            int rowIndex = 0;
            var categorized = new HashSet<string>();

            var categories = new Dictionary<string, List<string>>
            {
                ["Metadata Information"] = new List<string> { "Metadata_Source", "Object_Name" },
                ["Element Identity"] = new List<string> { "ElementID", "Element_Id", "GUID", "UniqueID", "ElementType", "Element_Type", "Element_Name", "Component_Name", "Family_Name", "Type_Name", "Name", "ID" },
                ["Geometric Properties"] = new List<string> { "Length", "Width", "Height", "Area", "Volume", "Thickness", "Diameter", "Radius" },
                ["Materials & Specifications"] = new List<string> { "Material_Type", "Material_Category", "Material_Grade", "Manufacturer", "Model", "Series", "Brand" },
                ["Construction Details"] = new List<string> { "Level", "Phase", "Workset", "Category", "Type", "Comments", "Mark", "Description" },
                ["Technical Properties"] = new List<string> { "Structural_Material", "Load_Bearing", "Fire_Rating", "Thermal_Properties", "R_Value", "U_Value" }
            };

            foreach (var category in categories)
            {
                var categoryProps = new List<KeyValuePair<string, string>>();

                foreach (var key in category.Value)
                {
                    if (properties.ContainsKey(key))
                    {
                        categoryProps.Add(new KeyValuePair<string, string>(key, properties[key]));
                        categorized.Add(key);
                    }
                }

                foreach (var prop in properties)
                {
                    if (!categorized.Contains(prop.Key))
                    {
                        foreach (var categoryKey in category.Value)
                        {
                            if (prop.Key.ToLower().Contains(categoryKey.ToLower().Replace("_", "")))
                            {
                                categoryProps.Add(prop);
                                categorized.Add(prop.Key);
                                break;
                            }
                        }
                    }
                }

                if (categoryProps.Count > 0)
                {
                    CreateCategoryHeader(category.Key, categoryProps.Count, rowIndex++);
                    categoryProps.Sort((a, b) => a.Key.CompareTo(b.Key));

                    foreach (var prop in categoryProps)
                    {
                        CreatePropertyRow(prop.Key, prop.Value, rowIndex++);
                    }
                }
            }

            var uncategorized = properties.Where(p => !categorized.Contains(p.Key)).OrderBy(p => p.Key).ToList();
            if (uncategorized.Count > 0)
            {
                CreateCategoryHeader("Additional Properties", uncategorized.Count, rowIndex++);
                foreach (var prop in uncategorized)
                {
                    CreatePropertyRow(prop.Key, prop.Value, rowIndex++);
                }
            }
        }

        private void DisplayFlatPropertyList(Dictionary<string, string> properties)
        {
            int rowIndex = 0;
            foreach (var prop in properties.OrderBy(p => p.Key))
            {
                CreatePropertyRow(prop.Key, prop.Value, rowIndex++);
            }
        }

        private void CreateCategoryHeader(string categoryName, int count, int rowIndex)
        {
            if (propertyRowPrefab == null || tableRowContainer == null) return;

            var headerRow = Instantiate(propertyRowPrefab, tableRowContainer);
            var textComponents = headerRow.GetComponentsInChildren<TMP_Text>();

            if (textComponents.Length >= 2)
            {
                textComponents[0].text = $"{categoryName} ({count})";
                textComponents[0].fontStyle = FontStyles.Bold;
                textComponents[0].fontSize = 13f;
                textComponents[0].color = new Color(0.2f, 0.4f, 0.8f);
                textComponents[1].text = "";
            }

            var headerImage = headerRow.GetComponent<Image>();
            if (headerImage != null)
            {
                headerImage.color = new Color(0.85f, 0.9f, 1f, 0.8f);
            }

            // Ensure proper layout element configuration
            var layoutElement = headerRow.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = headerRow.AddComponent<LayoutElement>();

            layoutElement.preferredHeight = rowHeight * 1.1f;
            layoutElement.minHeight = rowHeight * 1.1f;
            layoutElement.flexibleHeight = 0;
            layoutElement.flexibleWidth = 1;

            // Configure RectTransform for proper width
            var rectTransform = headerRow.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0, 0);
                rectTransform.anchorMax = new Vector2(1, 0);
                rectTransform.sizeDelta = new Vector2(0, rowHeight * 1.1f);
            }
        }

        private void CreatePropertyRow(string key, string value, int rowIndex)
        {
            if (propertyRowPrefab == null || tableRowContainer == null) return;

            var propertyRow = Instantiate(propertyRowPrefab, tableRowContainer);
            var textComponents = propertyRow.GetComponentsInChildren<TMP_Text>();

            if (textComponents.Length >= 2)
            {
                textComponents[0].text = FormatPropertyKey(key);
                textComponents[0].color = new Color(0.2f, 0.2f, 0.2f);

                string formattedValue = FormatPropertyValue(value);
                textComponents[1].text = formattedValue;
                textComponents[1].color = string.IsNullOrEmpty(value) ?
                    new Color(0.6f, 0.6f, 0.6f) : new Color(0.1f, 0.1f, 0.1f);
                textComponents[1].textWrappingMode = TextWrappingModes.Normal;
            }

            if (enableAlternatingRows)
            {
                var rowImage = propertyRow.GetComponent<Image>();
                if (rowImage != null)
                {
                    rowImage.color = rowIndex % 2 == 0 ?
                        new Color(0.98f, 0.98f, 0.98f, 0.9f) :
                        new Color(0.94f, 0.94f, 0.94f, 0.9f);
                }
            }

            // Ensure proper layout element configuration for scrolling
            var layoutElement = propertyRow.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = propertyRow.AddComponent<LayoutElement>();

            layoutElement.preferredHeight = rowHeight;
            layoutElement.minHeight = rowHeight;
            layoutElement.flexibleHeight = 0;
            layoutElement.flexibleWidth = 1;

            // Configure RectTransform for proper width
            var rectTransform = propertyRow.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0, 0);
                rectTransform.anchorMax = new Vector2(1, 0);
                rectTransform.sizeDelta = new Vector2(0, rowHeight);
            }

            var button = propertyRow.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OnPropertyViewed?.Invoke(key, value));
            }

            propertyRow.SetActive(true);
        }

        private string FormatPropertyKey(string key)
        {
            return key.Replace("_", " ").Replace("-", " ");
        }

        private string FormatPropertyValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return showEmptyProperties ? "<Empty>" : "<Not Specified>";

            if (value.Length > maxValueDisplayLength)
                return value.Substring(0, maxValueDisplayLength) + "...";

            return value;
        }

        private void UpdateHeaderDisplay(GameObject element, IMetadataProvider metadataProvider)
        {
            if (headerText != null)
            {
                string elementType = GetElementType(metadataProvider);
                string moduleName = currentModule != null ? currentModule.name : "Scene";
                int propertyCount = GetAllProperties(metadataProvider).Count;
                string sourceType = metadataProvider.GetComponentTypeName();
                headerText.text = $"{moduleName} - {element.name} ({elementType}) [{sourceType}] - {propertyCount} Properties";
            }
        }

        private void UpdateStatisticsDisplay(Dictionary<string, string> allProperties)
        {
            if (statsText != null)
            {
                int nonEmpty = allProperties.Count(p => !string.IsNullOrEmpty(p.Value));
                int empty = allProperties.Count - nonEmpty;

                statsText.text = $"Total: {allProperties.Count} | Populated: {nonEmpty} | Empty: {empty}";
            }
        }

        private string GetElementType(IMetadataProvider metadataProvider)
        {
            var allProperties = GetAllProperties(metadataProvider);

            string[] typeKeys = { "ElementType", "Type", "Category", "Family", "IfcType", "Component_Type" };
            foreach (string key in typeKeys)
            {
                if (allProperties.ContainsKey(key) && !string.IsNullOrEmpty(allProperties[key]))
                    return allProperties[key];
            }

            string name = metadataProvider.GetGameObject().name.ToLower();
            if (name.Contains("wall")) return "Wall";
            if (name.Contains("door")) return "Door";
            if (name.Contains("window")) return "Window";
            if (name.Contains("beam")) return "Beam";
            if (name.Contains("column")) return "Column";
            if (name.Contains("floor")) return "Floor";
            if (name.Contains("slab")) return "Slab";
            if (name.Contains("roof")) return "Roof";

            return "Building Element";
        }

        #endregion

        #region UI Management

        private void ClearMetadataDisplay()
        {
            if (tableRowContainer == null) return;

            foreach (Transform child in tableRowContainer)
            {
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void ShowWelcomeMessage()
        {
            if (propertyRowPrefab == null || tableRowContainer == null) return;

            var welcomeRow = Instantiate(propertyRowPrefab, tableRowContainer);
            var textComponents = welcomeRow.GetComponentsInChildren<TMP_Text>();

            if (textComponents.Length >= 2)
            {
                textComponents[0].text = "BIM Metadata Viewer";
                textComponents[0].fontStyle = FontStyles.Bold;
                textComponents[0].fontSize = 16f;
                textComponents[0].color = new Color(0.2f, 0.6f, 1f);

                string message = isViewerEnabled ?
                    "Point at building elements and pull trigger to view metadata" :
                    "Enable the viewer to start inspecting element metadata";

                textComponents[1].text = message;
                textComponents[1].fontStyle = FontStyles.Italic;
                textComponents[1].fontSize = 14f;
                textComponents[1].color = new Color(0.4f, 0.4f, 0.4f);
            }
        }

        private void ShowNoMetadataMessage()
        {
            ClearMetadataDisplay();

            if (propertyRowPrefab == null || tableRowContainer == null) return;

            var noDataRow = Instantiate(propertyRowPrefab, tableRowContainer);
            var textComponents = noDataRow.GetComponentsInChildren<TMP_Text>();

            if (textComponents.Length >= 2)
            {
                textComponents[0].text = "No Metadata Available";
                textComponents[0].fontStyle = FontStyles.Bold;
                textComponents[0].fontSize = 14f;
                textComponents[0].color = new Color(0.8f, 0.4f, 0.4f);

                textComponents[1].text = "This building element has no associated metadata";
                textComponents[1].fontStyle = FontStyles.Italic;
                textComponents[1].fontSize = 12f;
                textComponents[1].color = new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private void RefreshScrollView()
        {
            if (metadataScrollView == null || tableRowContainer == null) return;

            // Force immediate layout calculation
            Canvas.ForceUpdateCanvases();

            // Force content size fitter to recalculate
            var contentSizeFitter = tableRowContainer.GetComponent<ContentSizeFitter>();
            if (contentSizeFitter != null)
            {
                contentSizeFitter.enabled = false;
                contentSizeFitter.enabled = true;
            }

            // Force layout group to recalculate
            var layoutGroup = tableRowContainer.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.enabled = false;
                layoutGroup.enabled = true;
                layoutGroup.CalculateLayoutInputHorizontal();
                layoutGroup.CalculateLayoutInputVertical();
                layoutGroup.SetLayoutHorizontal();
                layoutGroup.SetLayoutVertical();
            }

            // Wait one frame then force another layout update
            StartCoroutine(DelayedLayoutRefresh());
        }

        private System.Collections.IEnumerator DelayedLayoutRefresh()
        {
            yield return null; // Wait one frame

            Canvas.ForceUpdateCanvases();

            // Ensure content height is properly calculated
            var contentRect = tableRowContainer.GetComponent<RectTransform>();
            if (contentRect != null && tableRowContainer.childCount > 0)
            {
                float totalHeight = 0f;
                var layoutGroup = tableRowContainer.GetComponent<VerticalLayoutGroup>();
                if (layoutGroup != null)
                {
                    totalHeight += layoutGroup.padding.top + layoutGroup.padding.bottom;
                    totalHeight += (tableRowContainer.childCount - 1) * layoutGroup.spacing;
                }

                for (int i = 0; i < tableRowContainer.childCount; i++)
                {
                    var child = tableRowContainer.GetChild(i);
                    var layoutElement = child.GetComponent<LayoutElement>();
                    if (layoutElement != null)
                    {
                        totalHeight += layoutElement.preferredHeight > 0 ? layoutElement.preferredHeight : rowHeight;
                    }
                    else
                    {
                        totalHeight += rowHeight;
                    }
                }

                // Set content height manually if needed
                if (contentRect.sizeDelta.y < totalHeight)
                {
                    contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, totalHeight);
                }
            }

            // Reset scroll position to top
            if (metadataScrollView != null)
            {
                metadataScrollView.verticalNormalizedPosition = 1f;
            }

            // Final canvas update
            Canvas.ForceUpdateCanvases();
        }

        #endregion

        #region Public API

        public bool IsViewerEnabled => isViewerEnabled;
        public bool IsActive => isViewerEnabled;
        public IMetadataProvider GetSelectedMetadataProvider() => currentMetadataProvider;
        public GameObject GetSelectedElement() => currentSelectedElement;

        public void ClearSelection()
        {
            DeselectCurrentElement();
            currentHoveredElement = null;
        }

        public void EnableViewer()
        {
            if (!isViewerEnabled)
                ToggleViewer();
        }

        public void DisableViewer()
        {
            if (isViewerEnabled)
                ToggleViewer();
        }

        public void ActivateViewer() => EnableViewer();
        public void DeactivateViewer() => DisableViewer();

        // Force scroll view refresh
        public void ForceScrollRefresh()
        {
            RefreshScrollView();
        }

        // Test scrolling with sample data
        public void TestScrolling()
        {
            ClearMetadataDisplay();

            // Create many test rows to test scrolling
            for (int i = 0; i < 50; i++)
            {
                CreatePropertyRow($"Test Property {i:D2}", $"Test Value {i:D2} - This is a longer value to test text wrapping and display", i);
            }

            RefreshScrollView();
        }

        public bool SelectElementByName(string elementName)
        {
            if (!isViewerEnabled) return false;

            if (currentModule != null)
            {
                // Search Metadata components in current module
                var metadataComponents = currentModule.GetComponentsInChildren<Metadata>(true);
                foreach (var metadata in metadataComponents)
                {
                    if (metadata.gameObject.name.Equals(elementName, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectBuildingElement(metadata.gameObject);
                        return true;
                    }
                }

                // Search IFCMetadata from SemanticEnrichment in current module
                var ifcMetadataComponents = currentModule.GetComponentsInChildren<IFCMetadata>(true);
                foreach (var ifcMetadata in ifcMetadataComponents)
                {
                    if (ifcMetadata.gameObject.name.Equals(elementName, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectBuildingElement(ifcMetadata.gameObject);
                        return true;
                    }
                }
            }

            foreach (var obj in currentModuleXRObjects)
            {
                if (obj != null && obj.name.Equals(elementName, StringComparison.OrdinalIgnoreCase))
                {
                    if (HasAnyMetadata(obj))
                    {
                        SelectBuildingElement(obj);
                        return true;
                    }
                }
            }

            // Search all Metadata components in scene
            var allMetadata = FindObjectsByType<Metadata>(FindObjectsSortMode.None);
            foreach (var metadata in allMetadata)
            {
                if (metadata.gameObject.name.Equals(elementName, StringComparison.OrdinalIgnoreCase))
                {
                    SelectBuildingElement(metadata.gameObject);
                    return true;
                }
            }

            // Search all IFCMetadata from SemanticEnrichment in scene
            var allIfcMetadata = FindObjectsByType<IFCMetadata>(FindObjectsSortMode.None);
            foreach (var ifcMetadata in allIfcMetadata)
            {
                if (ifcMetadata.gameObject.name.Equals(elementName, StringComparison.OrdinalIgnoreCase))
                {
                    SelectBuildingElement(ifcMetadata.gameObject);
                    return true;
                }
            }

            return false;
        }

        public Dictionary<string, string> GetAllPropertiesForSelectedElement()
        {
            return currentMetadataProvider != null ? GetAllProperties(currentMetadataProvider) : new Dictionary<string, string>();
        }

        #endregion

        private void OnDestroy()
        {
            RemoveAllHighlights();
            DisableInputActions();
            UnregisterEvents();
        }
    }
}