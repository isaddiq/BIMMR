using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// "What is the right hand pointing at?" for BIM scripts that do their own selection
/// (metadata viewer, inspection, dimension tool).
///
/// Uses the same Near-Far Interactor that draws the visible ray (controller or tracked
/// hand, whichever is active), and gives UI priority: a 3D hit is only reported when no
/// UI panel is in front of it along the ray. Whatever the ray touches first wins.
/// </summary>
public static class RightHandRay
{
    private static NearFarInteractor s_Interactor;
    private static int s_LastSearchFrame = -1;

    /// <summary>The active right-hand Near-Far Interactor, or null if none is active.</summary>
    public static NearFarInteractor Interactor
    {
        get
        {
            if (s_Interactor != null && s_Interactor.isActiveAndEnabled)
                return s_Interactor;

            // Controllers and hands swap at runtime, so search again (at most once per frame).
            if (s_LastSearchFrame != Time.frameCount)
            {
                s_LastSearchFrame = Time.frameCount;
                s_Interactor = null;
                foreach (var interactor in Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None))
                {
                    if (interactor.isActiveAndEnabled && interactor.handedness == InteractorHandedness.Right)
                    {
                        s_Interactor = interactor;
                        break;
                    }
                }
            }
            return s_Interactor;
        }
    }

    /// <summary>The ray the right hand is casting (same origin and direction as the visible line).</summary>
    public static bool TryGetRay(out Ray ray)
    {
        var origin = Interactor != null ? Interactor.farInteractionCaster?.effectiveCastOrigin : null;
        if (origin == null)
        {
            ray = default;
            return false;
        }

        ray = new Ray(origin.position, origin.forward);
        return true;
    }

    /// <summary>True if the ray is on a UI element; distance is measured from the ray origin.</summary>
    public static bool IsPointingAtUI(out float distance)
    {
        distance = float.MaxValue;
        var interactor = Interactor;
        if (interactor == null || !TryGetRay(out var ray))
            return false;

        if (!interactor.TryGetCurrentUIRaycastResult(out RaycastResult uiHit))
            return false;

        distance = Vector3.Distance(ray.origin, uiHit.worldPosition);
        return true;
    }

    /// <summary>
    /// The 3D object directly under the ray. Returns false when nothing is hit or a UI
    /// panel is in front of the hit, so pointing at UI never selects the model behind it.
    /// </summary>
    public static bool TryGet3DHit(out RaycastHit hit, float maxDistance = 100f, int layerMask = Physics.DefaultRaycastLayers)
    {
        hit = default;
        if (!TryGetRay(out var ray))
            return false;

        if (!Physics.Raycast(ray, out hit, maxDistance, layerMask, QueryTriggerInteraction.Ignore))
            return false;

        return !(IsPointingAtUI(out float uiDistance) && uiDistance <= hit.distance);
    }

    /// <summary>True on the frame the right-hand select (trigger/grip as configured on the interactor) is pressed.</summary>
    public static bool WasSelectPressedThisFrame()
    {
        var interactor = Interactor;
        return interactor != null && interactor.selectInput.ReadWasPerformedThisFrame();
    }
}
