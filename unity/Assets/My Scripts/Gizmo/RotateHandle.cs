using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class RotateHandle : MonoBehaviour
{
    [HideInInspector] public GameObject module;    // the target you’ll spin

    UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor _grabber;
    Quaternion _startGrabRot;
    Quaternion _startModuleRot;

    void Awake()
    {
        var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        _grabber = args.interactorObject as UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor;
        _startGrabRot = _grabber.transform.rotation;
        _startModuleRot = module.transform.rotation;
    }

    void OnRelease(SelectExitEventArgs args)
    {
        _grabber = null;
    }

    void Update()
    {
        // while you’re holding the ring, compute how much your controller has twisted
        if (_grabber == null) return;

        var twistDelta = Quaternion.Inverse(_startGrabRot) * _grabber.transform.rotation;
        module.transform.rotation = twistDelta * _startModuleRot;
    }
}
