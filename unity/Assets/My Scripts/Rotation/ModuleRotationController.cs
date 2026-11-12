// ModuleRotationController.cs
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.UI;

public class ModuleRotationController : MonoBehaviour
{
    [Header("References")]
    public ModuleManager moduleManager;
    public InputActionReference rightThumbstickAction;

    [Header("Rotation Settings")]
    [Range(1f, 360f)] public float rotationSpeed = 90f;
    public float inputDeadzone = 0.1f;

    [Header("Testing (Inspector)")]
    [Tooltip("Enable to drive rotation from the inspector slider instead of thumbstick")]
    public bool useTestSlider = false;
    [Tooltip("Target Y-angle for inspector testing")]
    [Range(0f, 360f)]
    public float testRotationAngle = 0f;

    [Header("UI Controls")]
    public GameObject uiPanel;
    public TMP_Text speedDisplayText;
    public Button increaseSpeedButton;
    public Button decreaseSpeedButton;
    public Button resetSpeedButton;
    [Tooltip("Amount to change speed per click")]
    public float speedIncrement = 10f;
    [Tooltip("Hide UI when no module is present")]
    public bool hideUIWhenNoModule = true;
    [Tooltip("Show UI at startup")]
    public bool showUIOnStart = true;

    [Header("Smoothing")]
    [Range(0.1f, 1f)]
    public float rotationSmoothness = 0.8f;
    public bool useSmoothing = true;

    // internal
    private float defaultRotationSpeed;
    private float lastTestY;
    private GameObject lastModule;
    private ModuleCenterCalculator centerCalc;
    private float targetY;
    private float velocity;
    private bool initialized = false;

    void Start()
    {
        defaultRotationSpeed = rotationSpeed;
        SetupUI();
    }

    void OnEnable()
    {
        rightThumbstickAction?.action.Enable();
        if (moduleManager != null)
        {
            moduleManager.OnModuleSpawned -= OnModuleSpawned;
            moduleManager.OnModuleSpawned += OnModuleSpawned;
        }
    }

    void OnDisable()
    {
        rightThumbstickAction?.action.Disable();
        if (moduleManager != null)
            moduleManager.OnModuleSpawned -= OnModuleSpawned;
    }

    private void OnModuleSpawned(GameObject mod)
    {
        initialized = false;
        lastModule = null;
    }

    void Update()
    {
        var mod = moduleManager != null ? moduleManager.GetCurrentModule() : null;
        HandleUIVisibility(mod);

        if (mod == null) return;

        if (!initialized || mod != lastModule)
            Initialize(mod);

        if (useTestSlider)
        {
            // inspector slider-driven absolute rotation
            if (Mathf.Abs(Mathf.DeltaAngle(lastTestY, testRotationAngle)) > 0.001f)
            {
                targetY = testRotationAngle;
                lastTestY = testRotationAngle;
                ApplyRotation(mod, targetY);
            }
        }
        else if (rightThumbstickAction != null && rightThumbstickAction.action != null)
        {
            // thumbstick-driven continuous rotation
            Vector2 input = rightThumbstickAction.action.ReadValue<Vector2>();
            float h = input.x;
            if (Mathf.Abs(h) > inputDeadzone)
            {
                targetY += h * rotationSpeed * Time.deltaTime;
                ApplyRotation(mod, targetY);
            }
        }
    }

    private void Initialize(GameObject mod)
    {
        lastModule = mod;

        // ModuleManager already calculates the center when it spawns a module
        if (!mod.TryGetComponent(out centerCalc))
            Debug.LogWarning($"ModuleRotationController: Missing ModuleCenterCalculator on {mod.name}, rotating around its pivot");

        // seed our tracking variables (marked initialized even without a calculator, so this doesn't re-run every frame)
        targetY = mod.transform.eulerAngles.y;
        lastTestY = targetY;
        velocity = 0f;
        initialized = true;
    }

    private void ApplyRotation(GameObject mod, float desiredY)
    {
        float currentY = mod.transform.eulerAngles.y;
        float delta = Mathf.DeltaAngle(currentY, desiredY);
        if (Mathf.Abs(delta) < 0.001f) return;

        Vector3 pivot = centerCalc != null ? centerCalc.GetModuleCenter() : mod.transform.position;

        if (useSmoothing && !useTestSlider)
        {
            float smooth = Mathf.SmoothDampAngle(currentY, desiredY, ref velocity, 1f - rotationSmoothness);
            float step = Mathf.DeltaAngle(currentY, smooth);
            mod.transform.RotateAround(pivot, Vector3.up, step);
        }
        else
        {
            mod.transform.RotateAround(pivot, Vector3.up, delta);
        }
    }

    private void SetupUI()
    {
        if (uiPanel != null)
            uiPanel.SetActive(showUIOnStart);

        if (increaseSpeedButton != null)
            increaseSpeedButton.onClick.AddListener(() => ChangeSpeed(rotationSpeed + speedIncrement));

        if (decreaseSpeedButton != null)
            decreaseSpeedButton.onClick.AddListener(() => ChangeSpeed(rotationSpeed - speedIncrement));

        if (resetSpeedButton != null)
            resetSpeedButton.onClick.AddListener(() => ChangeSpeed(defaultRotationSpeed));

        UpdateSpeedDisplay();
    }

    private void ChangeSpeed(float newSpeed)
    {
        rotationSpeed = Mathf.Clamp(newSpeed, 1f, 360f);
        UpdateSpeedDisplay();
    }

    private void UpdateSpeedDisplay()
    {
        if (speedDisplayText != null)
            speedDisplayText.text = $"Speed: {rotationSpeed:F0}°/s";
    }

    private void HandleUIVisibility(GameObject mod)
    {
        if (uiPanel == null || !hideUIWhenNoModule) return;
        bool shouldShow = mod != null;
        if (uiPanel.activeSelf != shouldShow)
            uiPanel.SetActive(shouldShow);
    }

    // Public methods for GPT Bridge integration
    public void RotateLeft()
    {
        RotateCurrentModule(Vector3.up * -rotationSpeed * Time.deltaTime);
    }

    public void RotateRight()
    {
        RotateCurrentModule(Vector3.up * rotationSpeed * Time.deltaTime);
    }

    public void RotateUp()
    {
        RotateCurrentModule(Vector3.right * rotationSpeed * Time.deltaTime);
    }

    public void RotateDown()
    {
        RotateCurrentModule(Vector3.right * -rotationSpeed * Time.deltaTime);
    }

    public void RotateBy(Vector3 eulerAngles)
    {
        RotateCurrentModule(eulerAngles);
    }

    private void RotateCurrentModule(Vector3 eulerAngles)
    {
        if (moduleManager == null) return;

        GameObject currentModule = moduleManager.GetCurrentModule();
        if (currentModule == null) return;

        // Get center calculator if available
        if (currentModule.TryGetComponent<ModuleCenterCalculator>(out var calc))
        {
            // Rotate around calculated center
            currentModule.transform.RotateAround(calc.GetModuleCenter(), eulerAngles.normalized, eulerAngles.magnitude);
        }
        else
        {
            // Rotate around object center
            currentModule.transform.Rotate(eulerAngles, Space.World);
        }
    }
}
