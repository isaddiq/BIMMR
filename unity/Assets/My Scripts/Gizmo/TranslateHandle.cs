using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class TranslateHandle : MonoBehaviour
{
    [HideInInspector] public GameObject module;    // what you’ll slide
    [HideInInspector] public Vector3 axis;      // which axis to move along

    UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor _grabber;
    Vector3 _startGrabPos;
    Vector3 _startModulePos;

    void Awake()
    {
        var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        _grabber = args.interactorObject as UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor;
        _startGrabPos = _grabber.transform.position;
        _startModulePos = module.transform.position;
    }

    void OnRelease(SelectExitEventArgs args)
    {
        _grabber = null;
    }

    void Update()
    {
        if (_grabber == null) return;

        // project your controller’s movement onto the chosen axis
        Vector3 delta = _grabber.transform.position - _startGrabPos;
        float dist = Vector3.Dot(delta, axis.normalized);
        module.transform.position = _startModulePos + axis.normalized * dist;
    }
}
