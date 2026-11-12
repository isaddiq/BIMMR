using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BIMMR.Inspection
{
    /// <summary>A candidate element shown in the n-best popup.</summary>
    public class SelectionCandidate
    {
        public GameObject element;
        public BIMElementInfo info;
        public float rayDistance;   // distance along the ray from its origin (m)
        public Vector3 hitPoint;
    }

    /// <summary>
    /// U1: n-best disambiguation popup. When more than one element lies under or next to the
    /// ray, the candidates are listed in order of ray distance with their identifier,
    /// category and parent module, and the operator confirms the intended one. The chosen
    /// rank is reported back so it can be logged (selection_events.csv).
    /// Built at runtime from UnityEngine.UI so it has no TextMeshPro dependency.
    /// </summary>
    public class DisambiguationPopup : MonoBehaviour
    {
        public Camera followCamera;
        public float spawnDistance = 0.9f;
        public float canvasWorldWidth = 0.5f;
        public int maxCandidates = 5;
        public int fontSize = 22;

        public bool IsOpen => _canvas != null && _canvas.gameObject.activeSelf;

        private Canvas _canvas;
        private RectTransform _list;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private Action<int> _onChosen;   // 1-based rank, or 0 when cancelled
        private Text _title;

        private void Awake()
        {
            if (followCamera == null) followCamera = Camera.main;
            Build();
            _canvas.gameObject.SetActive(false);
        }

        /// <summary>Show the candidates (already ranked). The callback receives the 1-based
        /// rank of the chosen candidate, or 0 if the operator cancelled.</summary>
        public void Show(IList<SelectionCandidate> ranked, Action<int> onChosen)
        {
            _onChosen = onChosen;
            foreach (var r in _rows) Destroy(r);
            _rows.Clear();

            int n = Mathf.Min(ranked.Count, maxCandidates);
            _title.text = $"Which element? ({n} candidates, nearest first)";
            float rowH = 1f / (n + 1);
            for (int i = 0; i < n; i++)
            {
                var c = ranked[i];
                int rank = i + 1;
                string label = $"{rank}. {c.info?.displayName ?? c.element.name}\n" +
                               $"   ID {c.info?.stableId ?? "-"}  |  {c.info?.category ?? "-"}  |  {c.rayDistance:F2} m";
                _rows.Add(MakeButton(_list, label, 1f - (i + 1) * rowH, 1f - i * rowH, () => Choose(rank)));
            }
            _rows.Add(MakeButton(_list, "Cancel", 0f, rowH, () => Choose(0)));

            Place();
            _canvas.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        private void Choose(int rank)
        {
            Hide();
            var cb = _onChosen;
            _onChosen = null;
            cb?.Invoke(rank);
        }

        private void Place()
        {
            var cam = followCamera != null ? followCamera : Camera.main;
            if (cam == null) return;
            var t = _canvas.transform;
            t.position = cam.transform.position + cam.transform.forward * spawnDistance;
            t.rotation = Quaternion.LookRotation(t.position - cam.transform.position);
        }

        // ---- UI construction -----------------------------------------------

        private void Build()
        {
            var go = new GameObject("DisambiguationCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)_canvas.transform;
            rt.sizeDelta = new Vector2(800, 700);
            _canvas.transform.localScale = Vector3.one * (canvasWorldWidth / 800f);
            TryAddXrRaycaster(go);

            var bg = Panel("Bg", rt, new Color(0.05f, 0.05f, 0.08f, 0.92f), Vector2.zero, Vector2.one);
            _title = Label("Title", bg, "", fontSize + 4, TextAnchor.MiddleCenter, new Vector2(0, 0.88f), Vector2.one);
            _list = Panel("List", bg, new Color(0, 0, 0, 0), new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.87f));
        }

        // The XRI UI raycaster lets the controller ray press the buttons. Added by reflection
        // so this file compiles without a hard XRI reference.
        private static void TryAddXrRaycaster(GameObject go)
        {
            var t = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            if (t != null && go.GetComponent(t) == null) go.AddComponent(t);
        }

        private GameObject MakeButton(RectTransform parent, string text, float yMin, float yMax, Action onClick)
        {
            var rt = Panel("Candidate", parent, new Color(0.18f, 0.36f, 0.6f, 1f),
                           new Vector2(0f, yMin + 0.01f), new Vector2(1f, yMax - 0.01f));
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = rt.GetComponent<Image>();
            btn.onClick.AddListener(() => onClick());
            Label("Text", rt, text, fontSize, TextAnchor.MiddleLeft, new Vector2(0.02f, 0), new Vector2(0.98f, 1));
            return rt.gameObject;
        }

        private static RectTransform Panel(string name, Transform parent, Color c, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Text Label(string name, Transform parent, string s, int size, TextAnchor a, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.text = s;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = a;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }
    }
}
