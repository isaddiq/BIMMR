using System.IO;
using BIMMR.Inspection.Study;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// U1: element-framed evidence screenshot. Instead of relying on manual framing, a
    /// hidden camera is placed relative to the element's bounding volume, looking along the
    /// inspector's viewing direction, and renders an off-screen image. The file is named
    /// by event id so the screenshot, the record and the LLM call share one key.
    /// </summary>
    public class ElementScreenshotCapture : MonoBehaviour
    {
        public Camera viewerCamera;
        public int width = 1280;
        public int height = 960;
        [Tooltip("Margin around the element bounds (1 = tight fit).")]
        public float framingMargin = 1.35f;
        public float fieldOfView = 50f;
        public Color background = new Color(0.12f, 0.12f, 0.14f, 1f);

        private Camera _cam;

        /// <summary>Render the element and return the absolute PNG path, or null on failure.
        /// <paramref name="cameraPose"/> receives "px;py;pz|qx;qy;qz;qw".</summary>
        public string Capture(GameObject element, string fileStem, out string cameraPose)
        {
            cameraPose = "";
            if (element == null) return null;
            var renderers = element.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            EnsureCamera();
            var viewer = viewerCamera != null ? viewerCamera : Camera.main;
            Vector3 dir = viewer != null ? (b.center - viewer.transform.position).normalized : Vector3.forward;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;

            float radius = Mathf.Max(b.extents.magnitude, 0.05f) * framingMargin;
            float dist = radius / Mathf.Sin(Mathf.Deg2Rad * fieldOfView * 0.5f);
            _cam.transform.position = b.center - dir * dist;
            _cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            _cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
            _cam.farClipPlane = dist + radius * 4f;

            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            try
            {
                _cam.targetTexture = rt;
                _cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                Destroy(tex);

                string path = StudySession.PathIn(Path.Combine("screenshots", fileStem + ".png"));
                File.WriteAllBytes(path, png);

                var p = _cam.transform.position; var q = _cam.transform.rotation;
                cameraPose = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F4};{1:F4};{2:F4}|{3:F5};{4:F5};{5:F5};{6:F5}", p.x, p.y, p.z, q.x, q.y, q.z, q.w);
                return path;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BIM Inspection] Screenshot failed: {e.Message}");
                return null;
            }
            finally
            {
                _cam.targetTexture = null;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private void EnsureCamera()
        {
            if (_cam != null) return;
            var go = new GameObject("EvidenceCaptureCamera");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false;               // rendered manually only
            _cam.fieldOfView = fieldOfView;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = background;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
        }
    }
}
