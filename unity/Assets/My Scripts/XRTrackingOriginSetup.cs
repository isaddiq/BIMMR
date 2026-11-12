using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

/// <summary>
/// Forces the XR Origin into Unbounded tracking-origin mode at startup so the user
/// can roam freely in passthrough/MR without the runtime re-centering whenever they
/// step outside the local Floor reference space.
///
/// Order of preference: Unbounded -> Floor -> whatever the runtime ends up with.
/// Quest 3 + OpenXR + Meta Quest Support feature satisfies Unbounded when the headset
/// is in passthrough mode AND the user has either disabled the boundary or set the
/// guardian to "Roam" in the headset settings (this part is a headset-side toggle —
/// nothing the project can change for you).
/// </summary>
[DefaultExecutionOrder(-200)]
public class XRTrackingOriginSetup : MonoBehaviour
{
    public enum PreferredMode
    {
        Unbounded,
        Floor,
        Device
    }

    [Header("Tracking Origin")]
    [Tooltip("The XR Origin in the scene. Auto-found if left empty.")]
    [SerializeField] private XROrigin xrOrigin;

    [Tooltip("Preferred tracking origin mode. Unbounded enables free roaming.")]
    [SerializeField] private PreferredMode preferredMode = PreferredMode.Unbounded;

    [Tooltip("If Unbounded isn't supported by the runtime, fall back to Floor.")]
    [SerializeField] private bool fallbackToFloor = true;

    [Tooltip("Retry attempts while the XR subsystem comes online before giving up.")]
    [Min(1)] [SerializeField] private int retryCount = 20;

    [Tooltip("Seconds between retries.")]
    [Min(0.05f)] [SerializeField] private float retryInterval = 0.25f;

    [Tooltip("Verbose logging.")]
    [SerializeField] private bool debugLogging = true;

    private void Start()
    {
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();

        if (xrOrigin == null)
        {
            Debug.LogWarning("[XRTrackingOriginSetup] No XROrigin found in scene.");
            return;
        }

        StartCoroutine(ApplyTrackingOrigin());
    }

    private IEnumerator ApplyTrackingOrigin()
    {
        var primary = ToXRMode(preferredMode);
        var fallback = fallbackToFloor && primary != XROrigin.TrackingOriginMode.Floor
            ? XROrigin.TrackingOriginMode.Floor
            : (XROrigin.TrackingOriginMode?)null;

        for (int attempt = 0; attempt < retryCount; attempt++)
        {
            var subsystem = GetInputSubsystem();
            if (subsystem != null && subsystem.running)
            {
                if (TryRequest(subsystem, primary))
                {
                    Log($"Tracking origin set to {primary}");
                    yield break;
                }

                if (fallback.HasValue && TryRequest(subsystem, fallback.Value))
                {
                    Log($"Preferred mode {primary} not supported. Fell back to {fallback.Value}.");
                    yield break;
                }

                Debug.LogWarning($"[XRTrackingOriginSetup] Runtime did not accept {primary} or fallback. Supported modes: {subsystem.GetSupportedTrackingOriginModes()}");
                yield break;
            }

            yield return new WaitForSeconds(retryInterval);
        }

        Debug.LogWarning("[XRTrackingOriginSetup] Gave up waiting for XR input subsystem.");
    }

    private bool TryRequest(XRInputSubsystem subsystem, XROrigin.TrackingOriginMode mode)
    {
        var supported = subsystem.GetSupportedTrackingOriginModes();
        var flag = ToFlag(mode);
        if (flag != TrackingOriginModeFlags.Unknown && (supported & flag) == 0)
            return false;

        xrOrigin.RequestedTrackingOriginMode = mode;
        // XROrigin pushes the request to the subsystem on the next frame; force it now.
        bool ok = subsystem.TrySetTrackingOriginMode(flag);
        if (ok) subsystem.TryRecenter();
        return ok;
    }

    private XRInputSubsystem GetInputSubsystem()
    {
        var subsystems = new System.Collections.Generic.List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);
        return subsystems.Count > 0 ? subsystems[0] : null;
    }

    private static XROrigin.TrackingOriginMode ToXRMode(PreferredMode m) => m switch
    {
        PreferredMode.Unbounded => XROrigin.TrackingOriginMode.Unbounded,
        PreferredMode.Floor => XROrigin.TrackingOriginMode.Floor,
        PreferredMode.Device => XROrigin.TrackingOriginMode.Device,
        _ => XROrigin.TrackingOriginMode.Unbounded
    };

    private static TrackingOriginModeFlags ToFlag(XROrigin.TrackingOriginMode mode) => mode switch
    {
        XROrigin.TrackingOriginMode.Unbounded => TrackingOriginModeFlags.Unbounded,
        XROrigin.TrackingOriginMode.Floor => TrackingOriginModeFlags.Floor,
        XROrigin.TrackingOriginMode.Device => TrackingOriginModeFlags.Device,
        _ => TrackingOriginModeFlags.Unknown
    };

    private void Log(string msg)
    {
        if (debugLogging) Debug.Log($"[XRTrackingOriginSetup] {msg}");
    }

    [ContextMenu("Re-apply Tracking Origin Now")]
    private void ReapplyFromMenu()
    {
        if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null) StartCoroutine(ApplyTrackingOrigin());
    }
}
