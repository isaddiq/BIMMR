using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Builds and operates the world-space inspection panel.
    ///
    /// The panel is built at runtime out of plain <c>UnityEngine.UI</c>
    /// components so the module has no hard dependency on TextMeshPro or
    /// MRTK. If you prefer designer-authored UI, you can leave the
    /// auto-build off and assign the buttons / text fields manually in the
    /// inspector — the manager hooks them up the same way either way.
    ///
    /// All buttons forward to public methods on <see cref="BIMInspectionManager"/>.
    /// </summary>
    public class BIMInspectionWorldUI : MonoBehaviour
    {
        // -------- Configuration --------------------------------------------

        [Header("References")]
        public BIMInspectionManager manager;
        [Tooltip("Camera the panel positions itself in front of when shown.")]
        public Camera followCamera;

        [Header("Auto-build")]
        [Tooltip("If true, the UI canvas and controls are created in code at Awake.")]
        public bool autoBuildCanvas = true;
        [Tooltip("Distance in metres from the camera where the panel is placed " +
                 "when Show() is called.")]
        public float spawnDistance = 1.25f;
        [Tooltip("World height (Y offset) below the camera forward direction.")]
        public float spawnVerticalOffset = -0.1f;

        [Header("Layout")]
        [Tooltip("Width of the world-space canvas in metres.")]
        public float canvasWorldWidth = 0.7f;
        public Vector2 canvasPixelSize = new Vector2(900f, 1300f);
        public int    baseFontSize = 22;

        // Optional drag-and-drop refs if the user authors the UI manually.
        [Header("Manual Wiring (optional)")]
        public Canvas canvas;
        public Text  selectedNameText;
        public Text  stableIdText;
        public Text  categoryText;
        public Text  levelText;
        public Text  statusText;
        public Text  progressText;
        public InputField issueTypeInput;
        public InputField severityInput;
        public InputField commentInput;

        // -------- Internal -------------------------------------------------

        private RectTransform _panel;

        private void Awake()
        {
            if (manager == null) manager = GetComponentInParent<BIMInspectionManager>();
            if (followCamera == null) followCamera = Camera.main;

            if (autoBuildCanvas && canvas == null)
                BuildCanvas();

            if (manager != null)
            {
                manager.OnElementSelected += HandleElementSelected;
                manager.OnRecordUpdated   += HandleRecordUpdated;
                manager.OnInspectionModeChanged += HandleModeChanged;
            }

            // Visible by default so the user can see the "Start Inspection"
            // button. Inspection MODE is still off until Start is pressed.
            PositionInFrontOfCamera();
            SetVisible(true);
        }

        private void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnElementSelected -= HandleElementSelected;
                manager.OnRecordUpdated   -= HandleRecordUpdated;
                manager.OnInspectionModeChanged -= HandleModeChanged;
            }
        }

        // -------- Public API ----------------------------------------------

        public void Show()
        {
            PositionInFrontOfCamera();
            SetVisible(true);
            RefreshProgress();
        }

        public void Hide() => SetVisible(false);

        // -------- Manager event handlers ----------------------------------

        private void HandleElementSelected(GameObject go, BIMElementInfo info, InspectionRecord rec)
        {
            if (selectedNameText) selectedNameText.text = $"Element: {info?.displayName}";
            if (stableIdText)
            {
                string id = info?.stableId ?? "-";
                string marker = info != null && info.stableIdIsFallback ? "  (fallback)" : "";
                stableIdText.text = $"Stable ID: {id}{marker}";
            }
            if (categoryText) categoryText.text = $"Category: {info?.category ?? "-"}";
            if (levelText)    levelText.text    = $"Level: {info?.level ?? "-"}";
            if (statusText)   statusText.text   = $"Status: {rec?.status}";
        }

        private void HandleRecordUpdated(InspectionRecord rec)
        {
            if (statusText && rec != null) statusText.text = $"Status: {rec.status}";
            RefreshProgress();
        }

        private void HandleModeChanged(bool active)
        {
            // Keep the panel visible regardless of mode; the user toggles
            // inspection mode with the Start/Stop buttons.
        }

        // -------- UI button callbacks --------------------------------------

        public void OnClick_Start()  => manager?.StartInspectionMode();
        public void OnClick_Stop()   => manager?.StopInspectionMode();
        public void OnClick_Pass()   => manager?.MarkSelectedAsPassed();
        public void OnClick_Fail()   => manager?.MarkSelectedAsFailed();
        public void OnClick_Review() => manager?.MarkSelectedAsNeedsReview();
        public void OnClick_Clear()  => manager?.ClearSelection();
        public void OnClick_Json()   => manager?.ExportInspectionDataJson();
        public void OnClick_Csv()    => manager?.ExportInspectionDataCsv();
        public void OnClick_ShowProgress() => RefreshProgress();
        public void OnClick_Undo()   => manager?.UndoLastSelection();
        /// <summary>Ends the inspection pass and writes the paired HTML/JSON evidence report.</summary>
        public void OnClick_EndAndReport() => manager?.EndInspectionAndExport();

        public void OnClick_AddIssue()
        {
            if (manager == null) return;
            string type = issueTypeInput != null ? issueTypeInput.text : "General";
            string sev  = severityInput  != null ? severityInput.text  : "Medium";
            string c    = commentInput   != null ? commentInput.text   : "";
            manager.AddIssueToSelected(type, sev, c);
            if (commentInput) commentInput.text = "";
        }

        public void OnClick_AddComment()
        {
            if (manager == null || commentInput == null) return;
            manager.AddCommentToSelected(commentInput.text);
            commentInput.text = "";
        }

        // -------- UI construction ------------------------------------------

        private void SetVisible(bool visible)
        {
            if (canvas != null) canvas.gameObject.SetActive(visible);
        }

        private void PositionInFrontOfCamera()
        {
            var cam = followCamera != null ? followCamera : Camera.main;
            if (cam == null || canvas == null) return;
            var t = canvas.transform;
            t.position = cam.transform.position + cam.transform.forward * spawnDistance
                         + cam.transform.up * spawnVerticalOffset;
            t.rotation = Quaternion.LookRotation(t.position - cam.transform.position);
        }

        private void RefreshProgress()
        {
            if (progressText == null || manager == null) return;
            var p = manager.GetInspectionProgress();
            progressText.text =
                $"Progress: {p.inspectedElements}/{p.totalElements} " +
                $"({p.inspectedPercentage:F0}%)\n" +
                $"Pass: {p.passedElements}   Fail: {p.failedElements}   " +
                $"Review: {p.needsReviewElements}";
        }

        /// <summary>
        /// Build a default world-space canvas with all required buttons and
        /// text labels. Layout is intentionally generous (large buttons, big
        /// fonts) so it's readable in MR at ~1–2 m.
        /// </summary>
        private void BuildCanvas()
        {
            // Canvas root.
            var canvasGo = new GameObject("BIMInspectionCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = (RectTransform)canvas.transform;
            rt.sizeDelta = canvasPixelSize;
            // World scale so the canvas is canvasWorldWidth metres wide.
            float scale = canvasWorldWidth / canvasPixelSize.x;
            canvas.transform.localScale = Vector3.one * scale;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            scaler.referencePixelsPerUnit = 100f;

            // Background panel.
            _panel = NewImage("Panel", rt, new Color(0.07f, 0.07f, 0.1f, 0.88f));
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.one;
            _panel.offsetMin = Vector2.zero;
            _panel.offsetMax = Vector2.zero;

            // Title.
            NewText("Title", _panel, "BIM Inspection",
                    baseFontSize + 14, TextAnchor.UpperCenter,
                    new Vector2(0, 0.92f), new Vector2(1, 1));

            // Element info block.
            float y0 = 0.92f;
            float lineH = 0.045f;
            selectedNameText = NewText("Selected", _panel, "Element: -", baseFontSize,
                                       TextAnchor.UpperLeft,
                                       new Vector2(0.04f, y0 - lineH),
                                       new Vector2(0.96f, y0));
            stableIdText     = NewText("StableId", _panel, "Stable ID: -", baseFontSize,
                                       TextAnchor.UpperLeft,
                                       new Vector2(0.04f, y0 - 2 * lineH),
                                       new Vector2(0.96f, y0 - lineH));
            categoryText     = NewText("Category", _panel, "Category: -", baseFontSize,
                                       TextAnchor.UpperLeft,
                                       new Vector2(0.04f, y0 - 3 * lineH),
                                       new Vector2(0.96f, y0 - 2 * lineH));
            levelText        = NewText("Level", _panel, "Level: -", baseFontSize,
                                       TextAnchor.UpperLeft,
                                       new Vector2(0.04f, y0 - 4 * lineH),
                                       new Vector2(0.96f, y0 - 3 * lineH));
            statusText       = NewText("Status", _panel, "Status: -", baseFontSize,
                                       TextAnchor.UpperLeft,
                                       new Vector2(0.04f, y0 - 5 * lineH),
                                       new Vector2(0.96f, y0 - 4 * lineH));

            // Button grid (3 columns).
            var buttons = new List<(string label, UnityEngine.Events.UnityAction onClick)>
            {
                ("Start",            OnClick_Start),
                ("Stop",             OnClick_Stop),
                ("Clear Selection",  OnClick_Clear),
                ("Mark Pass",        OnClick_Pass),
                ("Mark Fail",        OnClick_Fail),
                ("Needs Review",     OnClick_Review),
                ("Add Issue",        OnClick_AddIssue),
                ("Add Comment",      OnClick_AddComment),
                ("Show Progress",    OnClick_ShowProgress),
                ("Export JSON",      OnClick_Json),
                ("Export CSV",       OnClick_Csv),
                ("Undo Last",        OnClick_Undo),
                ("End + Report",     OnClick_EndAndReport),
            };

            float gridTop = 0.66f;
            float gridBottom = 0.28f;
            int cols = 3;
            int rows = Mathf.CeilToInt(buttons.Count / (float)cols);
            float cellW = (1f - 0.08f) / cols;
            float cellH = (gridTop - gridBottom) / rows;
            for (int i = 0; i < buttons.Count; i++)
            {
                int c = i % cols;
                int r = i / cols;
                float xMin = 0.04f + c * cellW;
                float yMax = gridTop - r * cellH;
                NewButton(buttons[i].label, _panel, buttons[i].label,
                          new Vector2(xMin + 0.005f, yMax - cellH + 0.01f),
                          new Vector2(xMin + cellW - 0.005f, yMax - 0.01f),
                          buttons[i].onClick);
            }

            // Input fields.
            float inputTop = 0.26f;
            float inputH = 0.05f;
            issueTypeInput = NewInputField("IssueType", _panel, "Issue type (e.g. Damage)",
                                           new Vector2(0.04f, inputTop - inputH),
                                           new Vector2(0.49f, inputTop));
            severityInput  = NewInputField("Severity", _panel, "Severity (Low/Med/High/Critical)",
                                           new Vector2(0.51f, inputTop - inputH),
                                           new Vector2(0.96f, inputTop));
            commentInput   = NewInputField("Comment", _panel, "Comment",
                                           new Vector2(0.04f, inputTop - 2 * inputH - 0.01f),
                                           new Vector2(0.96f, inputTop - inputH - 0.01f));

            // Progress block.
            progressText = NewText("Progress", _panel, "Progress: 0/0 (0%)",
                                   baseFontSize,
                                   TextAnchor.LowerLeft,
                                   new Vector2(0.04f, 0.02f),
                                   new Vector2(0.96f, 0.12f));
        }

        // ---- UI builders --------------------------------------------------

        private RectTransform NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private Text NewText(string name, Transform parent, string content, int fontSize,
                             TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.text = content;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        private Button NewButton(string name, Transform parent, string label,
                                 Vector2 anchorMin, Vector2 anchorMax,
                                 UnityEngine.Events.UnityAction onClick)
        {
            var rt = NewImage(name, parent, new Color(0.18f, 0.36f, 0.6f, 1f));
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = rt.GetComponent<Image>();

            NewText(name + "_Label", rt, label, baseFontSize, TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one);

            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        private InputField NewInputField(string name, Transform parent, string placeholder,
                                         Vector2 anchorMin, Vector2 anchorMax)
        {
            var rt = NewImage(name, parent, new Color(1f, 1f, 1f, 0.1f));
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(rt, worldPositionStays: false);
            var tr = (RectTransform)textGo.transform;
            tr.anchorMin = new Vector2(0.02f, 0f);
            tr.anchorMax = new Vector2(0.98f, 1f);
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = baseFontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(rt, worldPositionStays: false);
            var pr = (RectTransform)phGo.transform;
            pr.anchorMin = tr.anchorMin; pr.anchorMax = tr.anchorMax;
            pr.offsetMin = pr.offsetMax = Vector2.zero;
            var ph = phGo.GetComponent<Text>();
            ph.font = text.font;
            ph.fontSize = baseFontSize;
            ph.color = new Color(1, 1, 1, 0.45f);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.fontStyle = FontStyle.Italic;
            ph.text = placeholder;

            var input = rt.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            return input;
        }
    }
}
