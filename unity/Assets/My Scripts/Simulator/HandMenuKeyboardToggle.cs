using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI.BodyUI;

/// <summary>
/// Opens/closes the existing XRI Hand Menu with a keyboard key, for testing in the
/// Meta XR Simulator where flipping the controller palm-up is awkward.
///
/// Open:  the HandMenu component is disabled (which makes it show its UI at full scale)
///        and the menu is placed in front of the headset camera.
/// Close: the HandMenu component is re-enabled, so it hides the menu and goes back to
///        the normal palm/controller-rotation behaviour.
///
/// Auto-added in the Editor when the scene has a HandMenu. Add it to a GameObject
/// manually if you want to change the key or placement in the Inspector.
/// </summary>
public class HandMenuKeyboardToggle : MonoBehaviour
{
    [Tooltip("The Hand Menu in the scene. Auto-found if left empty.")]
    [SerializeField] private HandMenu handMenu;

    [Tooltip("Key that opens/closes the menu.")]
    [SerializeField] private Key toggleKey = Key.Tab;

    [Tooltip("Also catch keys while the Meta XR Simulator window (not the Unity Game view) has focus. Windows Editor only.")]
    [SerializeField] private bool listenWhenUnityUnfocused = true;

    [Tooltip("Distance in front of the camera to place the menu (meters).")]
    [SerializeField] private float distance = 0.5f;

    [Tooltip("Vertical offset from eye height (meters).")]
    [SerializeField] private float verticalOffset = -0.1f;

    private bool menuOpen;

    public HandMenu Menu => handMenu;
    public bool IsOpen => menuOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isEditor || FindFirstObjectByType<HandMenuKeyboardToggle>() != null)
            return;

        if (FindFirstObjectByType<HandMenu>() == null)
            return;

        new GameObject(nameof(HandMenuKeyboardToggle)).AddComponent<HandMenuKeyboardToggle>();
    }

    private void Awake()
    {
        if (handMenu == null)
            handMenu = FindFirstObjectByType<HandMenu>();
    }

    private void Start()
    {
        if (handMenu == null)
        {
            Debug.LogWarning("[HandMenuKeyboardToggle] No HandMenu found in scene.");
            enabled = false;
            return;
        }

        Debug.Log($"[HandMenuKeyboardToggle] Press {toggleKey} to open/close the hand menu.");
    }

    private void Update()
    {
        SimulatorKeys.listenWhenUnityUnfocused = listenWhenUnityUnfocused;

        if (SimulatorKeys.WasPressedThisFrame(toggleKey))
            SetMenuOpen(!menuOpen);
    }

    private void OnDisable()
    {
        if (menuOpen)
            SetMenuOpen(false);
    }

    /// <summary>Opens the menu in front of the camera if it is not already open.</summary>
    public void Open()
    {
        if (!menuOpen && handMenu != null)
            SetMenuOpen(true);
    }

    private void SetMenuOpen(bool open)
    {
        menuOpen = open;

        if (!open)
        {
            handMenu.enabled = true;
            return;
        }

        // HandMenu.OnDisable activates the menu UI and restores its full scale.
        handMenu.enabled = false;
        PlaceInFrontOfCamera(handMenu.handMenuUIGameObject.transform);
    }

    private void PlaceInFrontOfCamera(Transform menu)
    {
        var cam = Camera.main;
        if (cam == null)
            return;

        var camTransform = cam.transform;
        var forward = Vector3.ProjectOnPlane(camTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = camTransform.up;
        forward.Normalize();

        var position = camTransform.position + forward * distance + Vector3.up * verticalOffset;
        menu.SetPositionAndRotation(position, Quaternion.LookRotation(position - camTransform.position, Vector3.up));
    }
}
