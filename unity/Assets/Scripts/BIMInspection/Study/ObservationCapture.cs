using System;
using System.Collections;
using System.IO;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace BIMMR.Inspection
{
    /// <summary>
    /// U11: independent observation record for link L1 (physical-to-scene correspondence).
    /// At each confirmed selection, one passthrough camera frame is saved with the ray end
    /// point, so two raters can later judge from the frame whether the confirmed element is
    /// the physical component the inspector was looking at.
    ///
    /// On Quest 3 the passthrough cameras are exposed as Android cameras through
    /// WebCamTexture once the headset-camera permission is granted (Horizon OS v74+,
    /// Passthrough Camera API). In the Editor any webcam is used, which is enough to test
    /// the pipeline.
    ///
    /// Writes l1_frames/&lt;event_id&gt;.jpg and l1_index.csv. The rater columns are left empty
    /// and filled after the session.
    /// </summary>
    public class ObservationCapture : MonoBehaviour
    {
        public bool captureEnabled = true;
        [Tooltip("Preferred device name fragment; empty = first camera.")]
        public string deviceNameContains = "";
        public int requestedWidth = 1280;
        public int requestedHeight = 960;
        [Range(10, 100)] public int jpgQuality = 85;

        private const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        private WebCamTexture _cam;

        private static readonly Study.CsvLog Index = new Study.CsvLog("l1_index.csv", true,
            "event_id", "frame_path", "ray_hit_x", "ray_hit_y", "ray_hit_z", "confirm_ts",
            "rater1_match", "rater2_match", "final_match");

        private IEnumerator Start()
        {
            if (!captureEnabled) yield break;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                Permission.RequestUserPermission(HeadsetCameraPermission);
                float t = 0f;
                while (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) && t < 10f)
                { t += Time.unscaledDeltaTime; yield return null; }
                if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
                {
                    Debug.LogWarning("[L1 capture] Headset camera permission denied; L1 frames will be missing (counted as fail).");
                    yield break;
                }
            }
#endif
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogWarning("[L1 capture] No camera device available.");
                yield break;
            }
            string name = devices[0].name;
            foreach (var d in devices)
                if (!string.IsNullOrEmpty(deviceNameContains) &&
                    d.name.IndexOf(deviceNameContains, StringComparison.OrdinalIgnoreCase) >= 0) { name = d.name; break; }

            _cam = new WebCamTexture(name, requestedWidth, requestedHeight);
            _cam.Play();
        }

        /// <summary>Save the current frame for this event. A missing frame is logged with an
        /// empty path; the L1 protocol scores it as a fail (Table 7).</summary>
        public void Capture(string eventId, Vector3 rayHit, DateTime confirmUtc)
        {
            string path = "";
            if (captureEnabled && _cam != null && _cam.isPlaying && _cam.width > 16)
            {
                try
                {
                    var tex = new Texture2D(_cam.width, _cam.height, TextureFormat.RGB24, false);
                    tex.SetPixels32(_cam.GetPixels32());
                    tex.Apply();
                    path = Study.StudySession.PathIn(Path.Combine("l1_frames", eventId + ".jpg"));
                    File.WriteAllBytes(path, tex.EncodeToJPG(jpgQuality));
                    Destroy(tex);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[L1 capture] Frame not saved for {eventId}: {e.Message}");
                    path = "";
                }
            }
            Index.Write(eventId, path, rayHit.x, rayHit.y, rayHit.z, Study.StudySession.Iso(confirmUtc), "", "", "");
        }

        private void OnDestroy()
        {
            if (_cam != null && _cam.isPlaying) _cam.Stop();
        }
    }
}
