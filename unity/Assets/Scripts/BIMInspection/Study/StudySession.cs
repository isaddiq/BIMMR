using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BIMMR.Inspection.Study
{
    /// <summary>
    /// Single source of truth for everything the study logs share:
    ///   * one session_id and participant_id, written into every log row and report;
    ///   * one clock (UTC, ISO 8601 with milliseconds) so latencies are timestamp differences;
    ///   * event_id generation for confirmed selections ("S07-03");
    ///   * build_version, so results before and after a configuration change can be separated;
    ///   * the experimental switches (U3, U15). Each switch is a serialized bool whose value
    ///     is written into every row through <see cref="Condition"/>.
    ///
    /// Put one <see cref="StudySessionConfig"/> in the scene. Scripts that run without it
    /// still work; they log with session_id "S00" and the default condition.
    /// </summary>
    public static class StudySession
    {
        // ---- identity -------------------------------------------------------
        public static string SessionId { get; private set; } = "S00";
        public static string ParticipantId { get; private set; } = "P00";
        public static string BuildVersion { get; private set; } = "dev";
        public static bool IsActive { get; private set; }

        // ---- switches (defaults = the deployed configuration) ---------------
        public static bool EnableDisambiguation = true;     // U3
        public static bool ModuleScoping = true;            // U15
        public static bool LlmContextEnabled = true;        // U15
        public static bool StructuredSchemaEnabled = true;  // U15
        public static bool BimOnlyViewerMode = false;       // U15 (comparator)
        public static bool RestrictedDataMode = false;      // U14

        /// <summary>Optional free label (for example "setA" or a counterbalancing order).</summary>
        public static string ConditionLabel = "";

        /// <summary>Condition string written into every log row.</summary>
        public static string Condition
        {
            get
            {
                var parts = new List<string> { EnableDisambiguation ? "disamb_on" : "disamb_off" };
                if (!ModuleScoping) parts.Add("noscope");
                if (!LlmContextEnabled) parts.Add("nollmctx");
                if (!StructuredSchemaEnabled) parts.Add("noschema");
                if (BimOnlyViewerMode) parts.Add("bimonly");
                if (RestrictedDataMode) parts.Add("restricted");
                if (!string.IsNullOrEmpty(ConditionLabel)) parts.Add(ConditionLabel);
                return string.Join("+", parts);
            }
        }

        private static int _eventCounter;
        private static string _logRoot;

        // ---- clock ----------------------------------------------------------

        /// <summary>The single clock used by every study log: UTC, ISO 8601, milliseconds.</summary>
        public static DateTime Now => DateTime.UtcNow;

        public static string Iso(DateTime utc) =>
            utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

        public static string NowIso() => Iso(Now);

        // ---- lifecycle ------------------------------------------------------

        /// <summary>Start a session. Called when inspection mode starts.</summary>
        public static void Begin(string sessionId, string participantId, string buildVersion)
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? $"S{DateTime.UtcNow:MMddHHmm}" : sessionId.Trim();
            ParticipantId = string.IsNullOrWhiteSpace(participantId) ? "P00" : participantId.Trim();
            BuildVersion = string.IsNullOrWhiteSpace(buildVersion) ? Application.version : buildVersion.Trim();
            _eventCounter = 0;
            _logRoot = null;
            IsActive = true;
            Directory.CreateDirectory(LogDir);
            SessionEvents.Log("session_start");
        }

        public static void End()
        {
            if (!IsActive) return;
            SessionEvents.Log("session_end");
            IsActive = false;
        }

        /// <summary>Next event id for a confirmed selection, e.g. "S07-03".</summary>
        public static string NextEventId()
        {
            _eventCounter++;
            return $"{SessionId}-{_eventCounter:00}";
        }

        public static int EventCount => _eventCounter;

        /// <summary>Logs/&lt;session_id&gt;/ under persistentDataPath. Created on demand.</summary>
        public static string LogDir
        {
            get
            {
                if (_logRoot == null)
                {
                    _logRoot = Path.Combine(Application.persistentDataPath, "Logs", SessionId);
                    Directory.CreateDirectory(_logRoot);
                }
                return _logRoot;
            }
        }

        public static string PathIn(string relative)
        {
            string p = Path.Combine(LogDir, relative);
            string dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            return p;
        }

        /// <summary>Columns prepended to every CSV row.</summary>
        public static readonly string[] CommonHeader =
            { "session_id", "participant_id", "condition", "build_version" };

        public static string[] CommonValues() =>
            new[] { SessionId, ParticipantId, Condition, BuildVersion };
    }

    /// <summary>
    /// Scene component that configures <see cref="StudySession"/> from the Inspector.
    /// The study operator types the session and participant codes before each session.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class StudySessionConfig : MonoBehaviour
    {
        [Header("Identity (set before each session)")]
        public string sessionId = "S01";
        public string participantId = "P01";
        [Tooltip("Git commit or release tag of this build. Written into every log line.")]
        public string buildVersion = "rev2-u1-u15";

        [Header("U3: disambiguation switch")]
        public bool enableDisambiguation = true;

        [Header("U15: comparison and ablation switches")]
        public bool moduleScoping = true;
        public bool llmContextEnabled = true;
        public bool structuredSchemaEnabled = true;
        [Tooltip("Comparator: non-registered BIM viewer. Registration, LLM and structured " +
                 "evidence are switched off; selections are still logged.")]
        public bool bimOnlyViewerMode = false;

        [Header("U14: restricted-data mode")]
        public bool restrictedDataMode = false;

        [Header("Optional label written into the condition column")]
        public string conditionLabel = "";

        [Tooltip("Start the session when the scene starts, so module loading and positioning " +
                 "are timed from session start (Table 10).")]
        public bool beginOnStart = true;

        private void Awake() => Apply();

        private void Start()
        {
            if (beginOnStart && !StudySession.IsActive) BeginSession();
        }

        private void OnApplicationQuit() => StudySession.End();
        private void OnValidate() { if (Application.isPlaying) Apply(); }

        public void Apply()
        {
            StudySession.EnableDisambiguation = enableDisambiguation;
            StudySession.ModuleScoping = moduleScoping && !bimOnlyViewerMode;
            StudySession.LlmContextEnabled = llmContextEnabled && !bimOnlyViewerMode;
            StudySession.StructuredSchemaEnabled = structuredSchemaEnabled;
            StudySession.BimOnlyViewerMode = bimOnlyViewerMode;
            StudySession.RestrictedDataMode = restrictedDataMode;
            StudySession.ConditionLabel = conditionLabel ?? "";
        }

        /// <summary>Called by the inspection manager when inspection mode starts.</summary>
        public void BeginSession()
        {
            Apply();
            StudySession.Begin(sessionId, participantId, buildVersion);
        }

        /// <summary>UI hook: flip the disambiguation switch between conditions.</summary>
        public void SetDisambiguation(bool on)
        {
            enableDisambiguation = on;
            Apply();
            SessionEvents.Log("condition_change", StudySession.Condition);
        }
    }
}
