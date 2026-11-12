using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Makes the controller/hand rays point at and select exactly what they touch.
///
/// - Far casting uses a plain raycast instead of the Starter Assets cone cast, which
///   picks the "best" object near the ray rather than the one the ray is on.
/// - The line no longer snaps to the selected object's attach point when you click.
///   For BIM elements that point is the element's pivot, so the ray visibly bent away
///   from where you were pointing.
///
/// - World-space canvases that only have the mouse GraphicRaycaster get a
///   TrackedDeviceGraphicRaycaster, otherwise XR rays pass straight through them.
///   Rescanned periodically so panels spawned at runtime are covered too.
///
/// UI vs 3D: the Near-Far Interactor already ends on whichever is closer along the ray;
/// scripts doing their own raycasts use <see cref="RightHandRay"/> for the same rule.
///
/// Auto-added to every scene. Add it to a GameObject manually to change settings.
/// </summary>
public class DirectRayInteractionSetup : MonoBehaviour
{
    [Tooltip("Use a straight raycast instead of a cone for far hover/select.")]
    [SerializeField] private bool useRaycastInsteadOfCone = true;

    [Tooltip("Keep the line straight when selecting (don't bend it to the object's attach point).")]
    [SerializeField] private bool keepLineStraightOnSelect = true;

    [Tooltip("Add an XR UI raycaster to world-space canvases missing one, so rays stop at them.")]
    [SerializeField] private bool makeWorldCanvasesBlockRays = true;

    [Tooltip("Seconds between scans for newly spawned canvases.")]
    [Min(0.1f)] [SerializeField] private float canvasRescanInterval = 1f;

    private float nextCanvasScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<DirectRayInteractionSetup>() != null)
            return;

        new GameObject(nameof(DirectRayInteractionSetup)).AddComponent<DirectRayInteractionSetup>();
    }

    private void Start()
    {
        Apply();
    }

    /// <summary>Applies the settings to every Near-Far Interactor, including inactive hand/controller ones.</summary>
    public void Apply()
    {
        if (useRaycastInsteadOfCone)
        {
            foreach (var interactor in FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (interactor.farInteractionCaster is CurveInteractionCaster caster)
                    caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;
            }
        }

        if (keepLineStraightOnSelect)
        {
            foreach (var visual in FindObjectsByType<CurveVisualController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                visual.snapToSelectedAttachIfAvailable = false;
        }
    }

    private void Update()
    {
        if (!makeWorldCanvasesBlockRays || Time.unscaledTime < nextCanvasScanTime)
            return;

        nextCanvasScanTime = Time.unscaledTime + canvasRescanInterval;
        AddMissingXRRaycasters();
    }

    private static void AddMissingXRRaycasters()
    {
        foreach (var raycaster in FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None))
        {
            if (!raycaster.TryGetComponent<Canvas>(out var canvas) || canvas.renderMode != RenderMode.WorldSpace)
                continue;

            if (!raycaster.TryGetComponent<TrackedDeviceGraphicRaycaster>(out _))
                raycaster.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
    }
}
