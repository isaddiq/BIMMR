using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Keyboard shortcuts for the existing hand menu, for testing in the Meta XR Simulator.
///
///   1..9   Press the hand menu's tab toggles in order (Spawn, Transform, Components, ...).
///          Opens the hand menu first if it is closed.
///   + / -  More / less building transparency (drives the existing transparency slider).
///
/// The tab toggles are read from the menu's "Toggles" container, so new tabs are picked
/// up automatically. The key-to-tab list is logged at start.
///
/// Auto-added in the Editor. Add it to a GameObject manually to change settings.
/// </summary>
public class SimulatorKeyboardShortcuts : MonoBehaviour
{
    [Tooltip("Opens the hand menu before switching tabs. Auto-found if left empty.")]
    [SerializeField] private HandMenuKeyboardToggle handMenuToggle;

    [Tooltip("Tab toggles mapped to keys 1..9 in order. Auto-filled from the container below if empty.")]
    [SerializeField] private List<Toggle> tabToggles = new List<Toggle>();

    [Tooltip("Name of the hand menu child that holds the tab toggles.")]
    [SerializeField] private string tabTogglesContainerName = "Toggles";

    [Tooltip("Auto-found if left empty.")]
    [SerializeField] private ModuleTransparencyController transparencyController;

    [Tooltip("How much each + / - press changes transparency (0..1).")]
    [Range(0.01f, 0.5f)] [SerializeField] private float transparencyStep = 0.1f;

    private float fallbackAlpha = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isEditor || FindFirstObjectByType<SimulatorKeyboardShortcuts>() != null)
            return;

        new GameObject(nameof(SimulatorKeyboardShortcuts)).AddComponent<SimulatorKeyboardShortcuts>();
    }

    private void Start()
    {
        if (handMenuToggle == null)
            handMenuToggle = FindFirstObjectByType<HandMenuKeyboardToggle>();

        if (transparencyController == null)
            transparencyController = FindAnyObjectByType<ModuleTransparencyController>();

        if (tabToggles.Count == 0)
            FindTabToggles();

        LogShortcuts();
    }

    private void Update()
    {
        int count = Mathf.Min(tabToggles.Count, 9);
        for (int i = 0; i < count; i++)
        {
            if (SimulatorKeys.WasPressedThisFrame(Key.Digit1 + i) || SimulatorKeys.WasPressedThisFrame(Key.Numpad1 + i))
                PressTab(i);
        }

        if (SimulatorKeys.WasPressedThisFrame(Key.Equals) || SimulatorKeys.WasPressedThisFrame(Key.NumpadPlus))
            ChangeTransparency(transparencyStep);

        if (SimulatorKeys.WasPressedThisFrame(Key.Minus) || SimulatorKeys.WasPressedThisFrame(Key.NumpadMinus))
            ChangeTransparency(-transparencyStep);
    }

    private void FindTabToggles()
    {
        var menu = handMenuToggle != null && handMenuToggle.Menu != null
            ? handMenuToggle.Menu.handMenuUIGameObject
            : null;
        if (menu == null)
            return;

        var container = FindChildRecursive(menu.transform, tabTogglesContainerName);
        if (container == null)
        {
            Debug.LogWarning($"[SimulatorKeyboardShortcuts] No \"{tabTogglesContainerName}\" object found in the hand menu.");
            return;
        }

        foreach (Transform child in container)
        {
            if (child.TryGetComponent<Toggle>(out var toggle))
                tabToggles.Add(toggle);
        }
    }

    private static Transform FindChildRecursive(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName)
                return child;

            var found = FindChildRecursive(child, childName);
            if (found != null)
                return found;
        }
        return null;
    }

    private void PressTab(int index)
    {
        var toggle = tabToggles[index];
        if (toggle == null || !toggle.interactable)
            return;

        // Make sure the menu is visible so the ToggleGroup handles the switch like a real click.
        if (handMenuToggle != null)
            handMenuToggle.Open();

        toggle.isOn = !toggle.isOn;
    }

    /// <summary>Positive = more transparent, negative = more opaque.</summary>
    private void ChangeTransparency(float delta)
    {
        if (transparencyController == null)
            return;

        // The slider value is opacity (1 = solid), so more transparency lowers it.
        var slider = transparencyController.transparencySlider;
        if (slider != null)
        {
            slider.value = Mathf.Clamp01(slider.value - delta);
            Debug.Log($"[SimulatorKeyboardShortcuts] Building opacity: {slider.value:0.00}");
            return;
        }

        fallbackAlpha = Mathf.Clamp01(fallbackAlpha - delta);
        transparencyController.SetBuildingTransparency(fallbackAlpha);
        Debug.Log($"[SimulatorKeyboardShortcuts] Building opacity: {fallbackAlpha:0.00}");
    }

    private void LogShortcuts()
    {
        var sb = new StringBuilder("[SimulatorKeyboardShortcuts] ");
        for (int i = 0; i < Mathf.Min(tabToggles.Count, 9); i++)
        {
            if (tabToggles[i] != null)
                sb.Append($"{i + 1} = {tabToggles[i].name.Replace("Toggle_", "")}, ");
        }
        sb.Append("+ / - = transparency");
        Debug.Log(sb.ToString());
    }
}
