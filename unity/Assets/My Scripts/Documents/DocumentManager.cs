// DocumentManager.cs - Image Document Viewer for AEC Projects in Mixed Reality
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[System.Serializable]
public class DrawingElement
{
    public string elementName;
    public DrawingCategory category;
    public List<DrawingSheet> sheets = new List<DrawingSheet>();
}

[System.Serializable]
public class ProjectDrawings
{
    public string projectName;
    public List<DrawingElement> elements = new List<DrawingElement>();

    public DrawingElement GetOrCreateElement(DrawingCategory category, string elementName = null)
    {
        string name = elementName ?? GetCategoryName(category);
        var element = elements.FirstOrDefault(e => e.category == category && e.elementName == name);
        if (element == null)
        {
            element = new DrawingElement
            {
                category = category,
                elementName = name
            };
            elements.Add(element);
        }
        return element;
    }

    private string GetCategoryName(DrawingCategory category)
    {
        return category switch
        {
            DrawingCategory.Plans => "Plans",
            DrawingCategory.Elevations => "Elevations",
            DrawingCategory.Sections => "Sections",
            DrawingCategory.Details => "Details",
            DrawingCategory.Specifications => "Specifications",
            DrawingCategory.Schedules => "Schedules",
            DrawingCategory.Structural => "Structural",
            DrawingCategory.MEP => "MEP",
            _ => category.ToString()
        };
    }
}

[System.Serializable]
public class DrawingSheet
{
    public Texture2D sheetTexture;
    public bool isLoaded;

    public string SheetName => sheetTexture != null ? sheetTexture.name : "Unnamed Sheet";
}

public enum DrawingCategory
{
    Plans,
    Elevations,
    Sections,
    Details,
    Specifications,
    Schedules,
    Structural,
    MEP
}

public class DocumentManager : MonoBehaviour
{
    [Header("References")]
    public ModuleManager moduleManager;

    [Header("Panel Toggle")]
    public GameObject documentPanelRoot;
    public bool isPanelOpen = false;

    [Header("UI Layout")]
    public GameObject splitViewContainer;
    public Transform leftPanel;
    public Transform rightPanel;

    [Header("Project Selection")]
    public TMP_Dropdown projectDropdown;
    public Button refreshProjectsButton;

    [Header("Element Selection")]
    public TMP_Dropdown elementDropdown;

    [Header("Drawing List")]
    public ScrollRect drawingScrollView;
    public Transform drawingListContent;
    public GameObject drawingButtonPrefab;

    [Header("Image Viewer")]
    public RawImage imageDisplay;
    public GameObject imageViewerContainer;
    public TMP_Text sheetTitleText;
    public Button zoomInButton;
    public Button zoomOutButton;
    public Button resetViewButton;
    public Slider zoomSlider;

    [Header("Pan Controls")]
    public Button panLeftButton;
    public Button panRightButton;
    public Button panUpButton;
    public Button panDownButton;
    public float panStep = 50f;

    [Header("Spatial Settings")]
    public float viewerDistance = 2f;
    public Vector3 viewerScale = Vector3.one;
    public bool enableGrabInteraction = true;

    [Header("Project Drawings")]
    public List<ProjectDrawings> projectDrawings = new List<ProjectDrawings>();

    private DrawingCategory currentCategory = DrawingCategory.Plans;
    private DrawingSheet currentSheet;
    private List<GameObject> drawingButtons = new List<GameObject>();
    private float currentZoom = 1f;
    private const float minZoom = 0.1f;
    private const float maxZoom = 3f;
    private RectTransform imageRectTransform;
    private RectTransform containerRectTransform;
    private Vector2 originalImageSize;
    private float baseScale = 1f;

    private void Start()
    {
        InitializeDocumentManager();
        SetupProjectDropdown();
        SetupElementDropdown();
        SetupImageControls();
        SetPanelVisibility(false);

        if (imageDisplay != null)
        {
            imageRectTransform = imageDisplay.GetComponent<RectTransform>();
            if (imageViewerContainer != null)
            {
                containerRectTransform = imageViewerContainer.GetComponent<RectTransform>();
                SetupImageMasking();
            }
        }
    }

    private void OnEnable()
    {
        if (moduleManager != null)
            moduleManager.OnModuleSpawned += OnModuleChanged;
    }

    private void OnDisable()
    {
        if (moduleManager != null)
            moduleManager.OnModuleSpawned -= OnModuleChanged;
    }

    private void InitializeDocumentManager()
    {
        if (moduleManager == null)
            moduleManager = FindAnyObjectByType<ModuleManager>();

        // Auto-load documents if none exist
        if (projectDrawings.Count == 0)
        {
            LoadDocumentsFromResources();
        }
    }

    private void LoadDocumentsFromResources()
    {
        BIMLog.Verbose("[DocumentManager] Loading documents from Resources folder...");

        // Try to load textures from Resources/Documents folder
        Texture2D[] documentTextures = Resources.LoadAll<Texture2D>("Documents");

        if (documentTextures.Length == 0)
        {
            Debug.LogWarning("[DocumentManager] No documents found in Resources/Documents folder. Creating sample project.");
            AddSampleProjectWithDrawings();
            return;
        }

        // Create a project for the loaded documents
        var project = new ProjectDrawings { projectName = "Loaded Project" };

        foreach (var texture in documentTextures)
        {
            // Determine category based on filename
            DrawingCategory category = DetermineCategoryFromFilename(texture.name);

            var element = project.GetOrCreateElement(category);
            var sheet = new DrawingSheet
            {
                sheetTexture = texture,
                isLoaded = true
            };

            element.sheets.Add(sheet);
            BIMLog.Verbose($"[DocumentManager] Loaded document: {texture.name} as {category}");
        }

        projectDrawings.Add(project);
        RefreshProjectList();
        RefreshDrawingList();

        BIMLog.Verbose($"[DocumentManager] Successfully loaded {documentTextures.Length} documents into project '{project.projectName}'");
    }

    private DrawingCategory DetermineCategoryFromFilename(string filename)
    {
        string lowerName = filename.ToLower();

        if (lowerName.Contains("plan") || lowerName.Contains("floor"))
            return DrawingCategory.Plans;
        else if (lowerName.Contains("elevation") || lowerName.Contains("elev"))
            return DrawingCategory.Elevations;
        else if (lowerName.Contains("section") || lowerName.Contains("sect"))
            return DrawingCategory.Sections;
        else if (lowerName.Contains("detail") || lowerName.Contains("det"))
            return DrawingCategory.Details;
        else if (lowerName.Contains("spec") || lowerName.Contains("specification"))
            return DrawingCategory.Specifications;
        else if (lowerName.Contains("schedule") || lowerName.Contains("sched"))
            return DrawingCategory.Schedules;
        else if (lowerName.Contains("structural") || lowerName.Contains("struct"))
            return DrawingCategory.Structural;
        else if (lowerName.Contains("mep") || lowerName.Contains("mechanical") || lowerName.Contains("electrical") || lowerName.Contains("plumbing"))
            return DrawingCategory.MEP;
        else
            return DrawingCategory.Plans; // Default to Plans
    }

    private void SetupImageMasking()
    {
        // Ensure the container has a Mask component to clip content outside bounds
        if (imageViewerContainer != null)
        {
            var mask = imageViewerContainer.GetComponent<Mask>();
            if (mask == null)
            {
                mask = imageViewerContainer.AddComponent<Mask>();
                mask.showMaskGraphic = false;
            }

            // Ensure container has an Image component for the mask to work
            var containerImage = imageViewerContainer.GetComponent<Image>();
            if (containerImage == null)
            {
                containerImage = imageViewerContainer.AddComponent<Image>();
                containerImage.color = new Color(0, 0, 0, 0); // Transparent
            }
        }
    }

    private void SetupProjectDropdown()
    {
        if (projectDropdown != null)
        {
            projectDropdown.onValueChanged.AddListener(OnProjectSelected);
            RefreshProjectList();
        }

        if (refreshProjectsButton != null)
            refreshProjectsButton.onClick.AddListener(RefreshProjectList);
    }

    private void SetupElementDropdown()
    {
        if (elementDropdown != null)
        {
            elementDropdown.ClearOptions();
            var categoryNames = new List<string>();

            foreach (DrawingCategory category in Enum.GetValues(typeof(DrawingCategory)))
            {
                categoryNames.Add(GetCategoryDisplayName(category));
            }

            elementDropdown.AddOptions(categoryNames);
            elementDropdown.onValueChanged.AddListener(OnElementSelected);
            elementDropdown.value = (int)currentCategory;
        }

        RefreshDrawingList();
    }

    private void RefreshProjectList()
    {
        if (projectDropdown == null) return;

        projectDropdown.ClearOptions();
        var projectNames = projectDrawings.Select(p => p.projectName).ToList();

        if (projectNames.Count == 0)
            projectNames.Add("No Projects Available");

        projectDropdown.AddOptions(projectNames);
    }

    public void OnProjectSelected(int index)
    {
        RefreshDrawingList();
        ClearImageViewer();
    }

    public void OnElementSelected(int index)
    {
        if (index >= 0 && index < Enum.GetValues(typeof(DrawingCategory)).Length)
        {
            currentCategory = (DrawingCategory)index;
            RefreshDrawingList();
        }
    }

    public void ToggleDocumentPanel()
    {
        isPanelOpen = !isPanelOpen;
        SetPanelVisibility(isPanelOpen);
    }

    public void OpenDocumentPanel()
    {
        isPanelOpen = true;
        SetPanelVisibility(true);
    }

    public void CloseDocumentPanel()
    {
        isPanelOpen = false;
        SetPanelVisibility(false);
    }

    private void SetPanelVisibility(bool visible)
    {
        if (documentPanelRoot != null)
        {
            documentPanelRoot.SetActive(visible);
            BIMLog.Verbose($"[DocumentManager] Panel visibility set to: {visible}");

            if (visible && splitViewContainer != null)
            {
                PositionSplitView();
            }
        }
        else
        {
            Debug.LogError("[DocumentManager] DocumentPanelRoot is null! Please assign it in the Inspector.");
        }
    }

    private void PositionSplitView()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Position the container in 3D space
        Vector3 targetPosition = cam.transform.position + cam.transform.forward * viewerDistance;
        splitViewContainer.transform.position = targetPosition;

        // Look at camera (billboard effect)
        Vector3 lookDirection = targetPosition - cam.transform.position;
        splitViewContainer.transform.rotation = Quaternion.LookRotation(lookDirection);
        splitViewContainer.transform.localScale = viewerScale;

        // Ensure proper Canvas setup for 3D UI
        SetupCanvasFor3DUI();

        // Remove or disable physics on UI elements
        DisablePhysicsOnUIElements();

        if (enableGrabInteraction)
            SetupSpatialInteraction();

        BIMLog.Verbose($"[DocumentManager] Positioned split view at {targetPosition} facing camera");
    }

    private void SetupCanvasFor3DUI()
    {
        if (splitViewContainer == null) return;

        // Find all Canvas components in the splitViewContainer and its children
        Canvas[] canvases = splitViewContainer.GetComponentsInChildren<Canvas>();

        foreach (Canvas canvas in canvases)
        {
            // Set to World Space for proper 3D positioning
            canvas.renderMode = RenderMode.WorldSpace;

            // Ensure the Canvas has appropriate scale for 3D viewing
            if (canvas.transform == splitViewContainer.transform)
            {
                // This is the main canvas, set appropriate world space scale
                canvas.transform.localScale = Vector3.one * 0.001f; // Adjust scale for world space
            }

            // Ensure proper sorting
            canvas.sortingOrder = 100; // High sorting order to appear on top

            BIMLog.Verbose($"[DocumentManager] Setup canvas '{canvas.name}' for 3D world space rendering");
        }
    }

    private void DisablePhysicsOnUIElements()
    {
        if (splitViewContainer == null) return;

        // Remove Rigidbody components from UI elements
        Rigidbody[] rigidbodies = splitViewContainer.GetComponentsInChildren<Rigidbody>();
        foreach (Rigidbody rb in rigidbodies)
        {
            // Check if this is actually a UI element (has RectTransform)
            if (rb.GetComponent<RectTransform>() != null)
            {
                BIMLog.Verbose($"[DocumentManager] Removing Rigidbody from UI element: {rb.name}");
                if (Application.isPlaying)
                    Destroy(rb);
                else
                    DestroyImmediate(rb);
            }
        }

        // Ensure UI elements are kinematic if they must have Rigidbody
        Rigidbody[] remainingRigidbodies = splitViewContainer.GetComponentsInChildren<Rigidbody>();
        foreach (Rigidbody rb in remainingRigidbodies)
        {
            if (rb.GetComponent<RectTransform>() != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                BIMLog.Verbose($"[DocumentManager] Made UI Rigidbody kinematic: {rb.name}");
            }
        }

        // Check for any Colliders on UI elements that might interfere
        Collider[] colliders = splitViewContainer.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            if (col.GetComponent<RectTransform>() != null && !(col is BoxCollider && col.isTrigger))
            {
                // This is a UI element with a non-trigger collider that might cause physics issues
                col.isTrigger = true;
                BIMLog.Verbose($"[DocumentManager] Made UI collider trigger: {col.name}");
            }
        }
    }

    private string GetCategoryDisplayName(DrawingCategory category)
    {
        return category switch
        {
            DrawingCategory.Plans => "Plans",
            DrawingCategory.Elevations => "Elevations",
            DrawingCategory.Sections => "Sections",
            DrawingCategory.Details => "Details",
            DrawingCategory.Specifications => "Specifications",
            DrawingCategory.Schedules => "Schedules",
            DrawingCategory.Structural => "Structural",
            DrawingCategory.MEP => "MEP",
            _ => category.ToString()
        };
    }

    private void SetupImageControls()
    {
        if (zoomInButton != null)
            zoomInButton.onClick.AddListener(() => AdjustZoom(0.25f));

        if (zoomOutButton != null)
            zoomOutButton.onClick.AddListener(() => AdjustZoom(-0.25f));

        if (resetViewButton != null)
            resetViewButton.onClick.AddListener(ResetView);

        if (zoomSlider != null)
        {
            zoomSlider.minValue = minZoom;
            zoomSlider.maxValue = maxZoom;
            zoomSlider.value = 1f;
            zoomSlider.onValueChanged.AddListener(OnZoomSliderChanged);
        }

        // Setup pan buttons
        if (panLeftButton != null)
            panLeftButton.onClick.AddListener(PanLeft);

        if (panRightButton != null)
            panRightButton.onClick.AddListener(PanRight);

        if (panUpButton != null)
            panUpButton.onClick.AddListener(PanUp);

        if (panDownButton != null)
            panDownButton.onClick.AddListener(PanDown);

        UpdatePanButtonsInteractability();
    }

    private void OnModuleChanged(GameObject newModule)
    {
        RefreshProjectList();
        RefreshDrawingList();
        ClearImageViewer();
    }

    private ProjectDrawings GetSelectedProject()
    {
        if (projectDropdown == null || projectDropdown.value < 0 || projectDropdown.value >= projectDrawings.Count)
            return null;

        return projectDrawings[projectDropdown.value];
    }

    private void RefreshDrawingList()
    {
        foreach (var btn in drawingButtons)
        {
            if (btn != null) Destroy(btn);
        }
        drawingButtons.Clear();

        if (drawingListContent == null)
        {
            Debug.LogError("[DocumentManager] DrawingListContent is null! Please assign it in the Inspector.");
            return;
        }

        var selectedProject = GetSelectedProject();
        if (selectedProject == null)
        {
            Debug.LogWarning($"[DocumentManager] No project selected. Total projects: {projectDrawings.Count}");
            return;
        }

        var element = selectedProject.elements.FirstOrDefault(e => e.category == currentCategory);
        if (element != null)
        {
            BIMLog.Verbose($"[DocumentManager] Found {element.sheets.Count} sheets in category {currentCategory}");
            foreach (var sheet in element.sheets)
            {
                CreateDrawingButton(sheet);
            }
        }
        else
        {
            BIMLog.Verbose($"[DocumentManager] No element found for category {currentCategory} in project {selectedProject.projectName}");
        }

        UpdateDrawingSelection();
    }

    private void CreateDrawingButton(DrawingSheet sheet)
    {
        if (drawingButtonPrefab == null || drawingListContent == null) return;

        GameObject drawingBtn = Instantiate(drawingButtonPrefab, drawingListContent);

        var button = drawingBtn.GetComponent<Button>();
        var text = drawingBtn.GetComponentInChildren<TMP_Text>();

        if (text != null)
            text.text = sheet.SheetName;

        if (button != null)
            button.onClick.AddListener(() => OpenDrawingSheet(sheet));

        drawingButtons.Add(drawingBtn);
    }

    public void OpenDrawingSheet(DrawingSheet sheet)
    {
        if (sheet.sheetTexture == null)
        {
            Debug.LogError("Sheet texture is null");
            return;
        }

        currentSheet = sheet;
        DisplaySheet(sheet);
        UpdateDrawingSelection();
    }

    private void DisplaySheet(DrawingSheet sheet)
    {
        if (imageDisplay != null && sheet.sheetTexture != null)
        {
            imageDisplay.texture = sheet.sheetTexture;

            if (imageViewerContainer != null)
                imageViewerContainer.SetActive(true);

            if (sheetTitleText != null)
                sheetTitleText.text = sheet.SheetName;

            // Store original image dimensions
            originalImageSize = new Vector2(sheet.sheetTexture.width, sheet.sheetTexture.height);

            // Fit image to container
            FitToContainer();
        }
    }

    private void FitToContainer()
    {
        if (imageRectTransform == null || containerRectTransform == null || imageDisplay.texture == null)
            return;

        // Get container dimensions
        Vector2 containerSize = containerRectTransform.rect.size;

        // Get image dimensions
        float imageWidth = imageDisplay.texture.width;
        float imageHeight = imageDisplay.texture.height;

        // Calculate scale to fit within container
        float scaleX = containerSize.x / imageWidth;
        float scaleY = containerSize.y / imageHeight;

        // Use the smaller scale to ensure the entire image fits
        baseScale = Mathf.Min(scaleX, scaleY) * 0.95f; // 0.95f for small margin

        // Set image size to fit container while preserving aspect ratio
        float newWidth = imageWidth * baseScale;
        float newHeight = imageHeight * baseScale;

        imageRectTransform.sizeDelta = new Vector2(newWidth, newHeight);

        // Reset position to center
        imageRectTransform.anchoredPosition = Vector2.zero;

        // Reset zoom
        currentZoom = 1f;
        ApplyZoom();
    }

    private void UpdateDrawingSelection()
    {
        foreach (var btn in drawingButtons)
        {
            var text = btn.GetComponentInChildren<TMP_Text>();
            var image = btn.GetComponent<Image>();

            if (text != null && image != null)
            {
                bool isSelected = currentSheet != null &&
                    text.text == currentSheet.SheetName;

                image.color = isSelected ?
                    new Color(0.2f, 0.6f, 1f, 1f) : Color.white;
            }
        }
    }

    private void AdjustZoom(float delta)
    {
        currentZoom = Mathf.Clamp(currentZoom + delta, minZoom, maxZoom);
        ApplyZoom();
    }

    private void OnZoomSliderChanged(float value)
    {
        currentZoom = value;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (imageRectTransform != null && imageDisplay.texture != null)
        {
            // Calculate new size based on base scale and zoom
            float imageWidth = imageDisplay.texture.width;
            float imageHeight = imageDisplay.texture.height;

            float newWidth = imageWidth * baseScale * currentZoom;
            float newHeight = imageHeight * baseScale * currentZoom;

            // Apply scaled dimensions
            imageRectTransform.sizeDelta = new Vector2(newWidth, newHeight);

            // Constrain position to keep image within bounds when zoomed out
            if (currentZoom <= 1f)
            {
                imageRectTransform.anchoredPosition = Vector2.zero;
            }
            else
            {
                // Allow panning when zoomed in
                ConstrainImagePosition();
            }

            if (zoomSlider != null && zoomSlider.value != currentZoom)
                zoomSlider.value = currentZoom;

            UpdatePanButtonsInteractability();
        }
    }

    private void ConstrainImagePosition()
    {
        if (imageRectTransform == null || containerRectTransform == null)
            return;

        Vector2 containerSize = containerRectTransform.rect.size;
        Vector2 imageSize = imageRectTransform.sizeDelta;
        Vector2 currentPos = imageRectTransform.anchoredPosition;

        // Calculate boundaries
        float maxX = Mathf.Max(0, (imageSize.x - containerSize.x) / 2);
        float maxY = Mathf.Max(0, (imageSize.y - containerSize.y) / 2);

        // Constrain position
        currentPos.x = Mathf.Clamp(currentPos.x, -maxX, maxX);
        currentPos.y = Mathf.Clamp(currentPos.y, -maxY, maxY);

        imageRectTransform.anchoredPosition = currentPos;
    }

    private void PanLeft()
    {
        if (currentZoom <= 1f || imageRectTransform == null) return;

        Vector2 newPos = imageRectTransform.anchoredPosition;
        newPos.x += panStep;
        imageRectTransform.anchoredPosition = newPos;
        ConstrainImagePosition();
    }

    private void PanRight()
    {
        if (currentZoom <= 1f || imageRectTransform == null) return;

        Vector2 newPos = imageRectTransform.anchoredPosition;
        newPos.x -= panStep;
        imageRectTransform.anchoredPosition = newPos;
        ConstrainImagePosition();
    }

    private void PanUp()
    {
        if (currentZoom <= 1f || imageRectTransform == null) return;

        Vector2 newPos = imageRectTransform.anchoredPosition;
        newPos.y -= panStep;
        imageRectTransform.anchoredPosition = newPos;
        ConstrainImagePosition();
    }

    private void PanDown()
    {
        if (currentZoom <= 1f || imageRectTransform == null) return;

        Vector2 newPos = imageRectTransform.anchoredPosition;
        newPos.y += panStep;
        imageRectTransform.anchoredPosition = newPos;
        ConstrainImagePosition();
    }

    private void UpdatePanButtonsInteractability()
    {
        bool canPan = currentZoom > 1f && imageDisplay.texture != null;

        if (panLeftButton != null)
            panLeftButton.interactable = canPan;

        if (panRightButton != null)
            panRightButton.interactable = canPan;

        if (panUpButton != null)
            panUpButton.interactable = canPan;

        if (panDownButton != null)
            panDownButton.interactable = canPan;
    }

    private void ResetView()
    {
        FitToContainer();
    }

    private void SetupSpatialInteraction()
    {
        if (splitViewContainer == null) return;

        // Only setup grab interaction on the main container, not UI elements
        var grabInteractable = splitViewContainer.GetComponent<XRGrabInteractable>();
        if (grabInteractable == null)
            grabInteractable = splitViewContainer.AddComponent<XRGrabInteractable>();

        grabInteractable.enabled = enableGrabInteraction;

        // Setup collider for grab interaction on the container only
        var collider = splitViewContainer.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = splitViewContainer.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 1.5f, 0.1f);
            collider.isTrigger = false; // Container can have physics for grabbing
        }

        // Ensure the container has a Rigidbody for XR interaction, but make it kinematic to prevent falling
        var containerRigidbody = splitViewContainer.GetComponent<Rigidbody>();
        if (containerRigidbody == null)
        {
            containerRigidbody = splitViewContainer.AddComponent<Rigidbody>();
        }

        // Make the container kinematic to prevent it from falling due to gravity
        containerRigidbody.isKinematic = true;
        containerRigidbody.useGravity = false;

        BIMLog.Verbose("[DocumentManager] Setup spatial interaction with kinematic container");
    }

    public void ClearImageViewer()
    {
        if (imageDisplay != null)
        {
            imageDisplay.texture = null;
        }

        if (imageViewerContainer != null)
            imageViewerContainer.SetActive(false);

        currentSheet = null;
        currentZoom = 1f;
        baseScale = 1f;
        UpdatePanButtonsInteractability();
    }

    public int GetTotalDrawingCount()
    {
        int count = 0;
        foreach (var project in projectDrawings)
        {
            foreach (var element in project.elements)
            {
                count += element.sheets.Count;
            }
        }
        return count;
    }

    #region Public API for Voice Commands

    public bool OpenDrawingByName(string sheetName)
    {
        foreach (var project in projectDrawings)
        {
            foreach (var element in project.elements)
            {
                var sheet = element.sheets.FirstOrDefault(s =>
                    s.SheetName.ToLower().Contains(sheetName.ToLower()));

                if (sheet != null)
                {
                    if (!isPanelOpen)
                        OpenDocumentPanel();

                    int projectIndex = projectDrawings.IndexOf(project);
                    if (projectDropdown != null && projectIndex >= 0)
                        projectDropdown.value = projectIndex;

                    currentCategory = element.category;
                    if (elementDropdown != null)
                        elementDropdown.value = (int)currentCategory;

                    RefreshDrawingList();
                    OpenDrawingSheet(sheet);
                    return true;
                }
            }
        }
        return false;
    }

    public void ShowPlans() => SelectElement(DrawingCategory.Plans);
    public void ShowElevations() => SelectElement(DrawingCategory.Elevations);
    public void ShowSections() => SelectElement(DrawingCategory.Sections);
    public void ShowDetails() => SelectElement(DrawingCategory.Details);
    public void ShowSpecifications() => SelectElement(DrawingCategory.Specifications);
    public void ShowSchedules() => SelectElement(DrawingCategory.Schedules);
    public void ShowStructural() => SelectElement(DrawingCategory.Structural);
    public void ShowMEP() => SelectElement(DrawingCategory.MEP);

    // GPT Bridge integration method
    public void OpenDocumentation()
    {
        OpenDocumentPanelViaVoice();
    }

    // Special method for voice command activation with enhanced stability
    public void OpenDocumentPanelViaVoice()
    {
        BIMLog.Verbose("[DocumentManager] Opening document panel via voice command with enhanced stability");

        isPanelOpen = true;

        if (documentPanelRoot != null)
        {
            documentPanelRoot.SetActive(true);

            // Wait one frame for UI elements to initialize, then position
            StartCoroutine(PositionSplitViewDelayed());
        }
        else
        {
            Debug.LogError("[DocumentManager] DocumentPanelRoot is null! Please assign it in the Inspector.");
        }
    }

    private System.Collections.IEnumerator PositionSplitViewDelayed()
    {
        // Wait one frame for UI elements to be properly initialized
        yield return null;

        if (splitViewContainer != null)
        {
            PositionSplitView();

            // Additional frame wait to ensure physics changes take effect
            yield return null;

            // Double-check that physics are properly disabled
            DisablePhysicsOnUIElements();
        }
    }

    private void SelectElement(DrawingCategory category)
    {
        currentCategory = category;
        if (elementDropdown != null)
            elementDropdown.value = (int)category;
        RefreshDrawingList();
    }

    #endregion

    #region Element Management

    public void AddSheetToElement(DrawingCategory category, Texture2D sheetTexture, string elementName = null)
    {
        var selectedProject = GetSelectedProject();
        if (selectedProject == null) return;

        var element = selectedProject.GetOrCreateElement(category, elementName);
        var sheet = new DrawingSheet
        {
            sheetTexture = sheetTexture,
            isLoaded = true
        };

        element.sheets.Add(sheet);
        RefreshDrawingList();
    }

    public void RemoveSheetFromElement(DrawingCategory category, string sheetName)
    {
        var selectedProject = GetSelectedProject();
        if (selectedProject == null) return;

        var element = selectedProject.elements.FirstOrDefault(e => e.category == category);
        if (element != null)
        {
            var sheet = element.sheets.FirstOrDefault(s => s.SheetName == sheetName);
            if (sheet != null)
            {
                element.sheets.Remove(sheet);
                RefreshDrawingList();
            }
        }
    }

    #endregion

    #region External UI Integration

    public void OnDocumentPanelButtonClicked()
    {
        ToggleDocumentPanel();
    }

    #endregion

    #region Context Menu Testing

    [ContextMenu("Add Sample Project with Drawings")]
    public void AddSampleProjectWithDrawings()
    {
        var project = new ProjectDrawings { projectName = "Sample Building Project" };

        var plansElement = project.GetOrCreateElement(DrawingCategory.Plans);
        plansElement.sheets.Add(new DrawingSheet { isLoaded = false });
        plansElement.sheets.Add(new DrawingSheet { isLoaded = false });

        var elevationsElement = project.GetOrCreateElement(DrawingCategory.Elevations);
        elevationsElement.sheets.Add(new DrawingSheet { isLoaded = false });

        projectDrawings.Add(project);
        RefreshProjectList();
        RefreshDrawingList();

        Debug.Log($"Added sample project '{project.projectName}' with drawing elements");
    }

    [ContextMenu("Debug UI Physics")]
    public void DebugUIPhysics()
    {
        if (splitViewContainer == null)
        {
            Debug.LogError("[DocumentManager] SplitViewContainer is null!");
            return;
        }

        Debug.Log("=== UI Physics Debug Report ===");

        // Check Canvas settings
        Canvas[] canvases = splitViewContainer.GetComponentsInChildren<Canvas>();
        Debug.Log($"Found {canvases.Length} Canvas components:");
        foreach (Canvas canvas in canvases)
        {
            Debug.Log($"  - {canvas.name}: RenderMode={canvas.renderMode}, SortingOrder={canvas.sortingOrder}");
        }

        // Check Rigidbody components
        Rigidbody[] rigidbodies = splitViewContainer.GetComponentsInChildren<Rigidbody>();
        Debug.Log($"Found {rigidbodies.Length} Rigidbody components:");
        foreach (Rigidbody rb in rigidbodies)
        {
            Debug.Log($"  - {rb.name}: IsKinematic={rb.isKinematic}, UseGravity={rb.useGravity}, HasRectTransform={rb.GetComponent<RectTransform>() != null}");
        }

        // Check Collider components
        Collider[] colliders = splitViewContainer.GetComponentsInChildren<Collider>();
        Debug.Log($"Found {colliders.Length} Collider components:");
        foreach (Collider col in colliders)
        {
            Debug.Log($"  - {col.name}: Type={col.GetType().Name}, IsTrigger={col.isTrigger}, HasRectTransform={col.GetComponent<RectTransform>() != null}");
        }

        Debug.Log("=== End UI Physics Debug Report ===");
    }

    [ContextMenu("Fix UI Physics Issues")]
    public void FixUIPhysicsIssues()
    {
        Debug.Log("[DocumentManager] Manually fixing UI physics issues...");
        DisablePhysicsOnUIElements();
        SetupCanvasFor3DUI();
        Debug.Log("[DocumentManager] UI physics fix completed");
    }

    #endregion
}