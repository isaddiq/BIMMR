using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shows the PC webcam as the background behind the scene while running in the Editor
/// (e.g. with the Meta XR Simulator), as a stand-in for headset passthrough. The
/// simulator's own passthrough is a synthetic room and cannot use a real camera.
///
/// The feed is drawn on a head-locked quad in the Background render queue with depth
/// writes off, so every scene object (including the hand menu) renders on top of it.
/// Press the switch key (default C) to cycle through the webcams attached to the PC.
/// It does nothing in device builds, where real passthrough is used.
///
/// Auto-added in the Editor. Add it to a GameObject manually if you want to pick a
/// specific webcam or change the settings in the Inspector.
/// </summary>
public class EditorWebcamBackground : MonoBehaviour
{
    [Tooltip("Webcam device name to start with. Empty uses the first camera found (names are logged at start).")]
    [SerializeField] private string deviceName = "";

    [Tooltip("Key that switches to the next webcam.")]
    [SerializeField] private Key switchCameraKey = Key.C;

    [SerializeField] private int requestedWidth = 1280;
    [SerializeField] private int requestedHeight = 720;
    [SerializeField] private int requestedFps = 30;

    [Tooltip("Flip the image left/right (turn on if the feed looks mirrored).")]
    [SerializeField] private bool mirrorHorizontally = false;

    [Tooltip("Camera to attach the background to. Uses Camera.main if empty.")]
    [SerializeField] private Camera targetCamera;

    private WebCamTexture webcam;
    private int deviceIndex;
    private Transform quad;
    private Material material;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isEditor || FindFirstObjectByType<EditorWebcamBackground>() != null)
            return;

        new GameObject(nameof(EditorWebcamBackground)).AddComponent<EditorWebcamBackground>();
    }

    private void Start()
    {
        if (!Application.isEditor)
        {
            enabled = false;
            return;
        }

        var devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogWarning("[EditorWebcamBackground] No webcam found.");
            enabled = false;
            return;
        }

        deviceIndex = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log($"[EditorWebcamBackground] Webcam {i}: \"{devices[i].name}\"");
            if (devices[i].name == deviceName)
                deviceIndex = i;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[EditorWebcamBackground] URP Unlit shader not found.");
            enabled = false;
            return;
        }

        material = new Material(shader);
        material.SetFloat("_ZWrite", 0f);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Background;

        var quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadObject.name = "Webcam Background";
        Destroy(quadObject.GetComponent<Collider>());

        var meshRenderer = quadObject.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        quad = quadObject.transform;
        StartWebcam(deviceIndex);

        if (devices.Length > 1)
            Debug.Log($"[EditorWebcamBackground] Press {switchCameraKey} to switch webcam.");
    }

    private void Update()
    {
        if (SimulatorKeys.WasPressedThisFrame(switchCameraKey))
            NextCamera();
    }

    /// <summary>Switches the background to the next attached webcam.</summary>
    public void NextCamera()
    {
        var devices = WebCamTexture.devices;
        if (devices.Length < 2 || material == null)
            return;

        StartWebcam((deviceIndex + 1) % devices.Length);
    }

    private void StartWebcam(int index)
    {
        if (webcam != null)
        {
            webcam.Stop();
            Destroy(webcam);
        }

        deviceIndex = index;
        string selected = WebCamTexture.devices[index].name;
        webcam = new WebCamTexture(selected, requestedWidth, requestedHeight, requestedFps);
        webcam.Play();
        material.mainTexture = webcam;

        Debug.Log($"[EditorWebcamBackground] Using webcam {index}: \"{selected}\" as background.");
    }

    private void LateUpdate()
    {
        if (quad == null)
            return;

        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null)
            return;

        if (quad.parent != targetCamera.transform)
            quad.SetParent(targetCamera.transform, false);

        // Sit just inside the far plane and scale to cover the whole view
        // (with some margin, since XR per-eye FOV can exceed Camera.fieldOfView).
        float depth = targetCamera.farClipPlane * 0.9f;
        float viewHeight = 2f * depth * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.3f;
        float viewWidth = viewHeight * targetCamera.aspect;
        float texAspect = webcam.width > 16 ? (float)webcam.width / webcam.height : 16f / 9f;

        float width = viewWidth;
        float height = viewWidth / texAspect;
        if (height < viewHeight)
        {
            height = viewHeight;
            width = viewHeight * texAspect;
        }

        quad.localPosition = new Vector3(0f, 0f, depth);
        quad.localRotation = Quaternion.identity;
        quad.localScale = new Vector3(
            mirrorHorizontally ? -width : width,
            webcam.videoVerticallyMirrored ? -height : height,
            1f);
    }

    private void OnDestroy()
    {
        if (webcam != null)
        {
            webcam.Stop();
            Destroy(webcam);
        }
        if (quad != null)
            Destroy(quad.gameObject);
        if (material != null)
            Destroy(material);
    }
}
