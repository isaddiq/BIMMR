using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BIMMR.Inspection.Study
{
    /// <summary>
    /// U14: restricted-data mode. Before any text leaves the device, project, module and
    /// element names are replaced with pseudonymous codes (PRJ-01, MOD-03, EL-0042);
    /// element identifiers are kept so traceability survives. The code-to-name map is kept
    /// on the device only (Logs/&lt;session&gt;/redaction_map.json) and used to restore names in
    /// responses before display.
    /// </summary>
    public static class RestrictedDataFilter
    {
        private static readonly Dictionary<string, string> NameToCode = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> CodeToName = new Dictionary<string, string>(StringComparer.Ordinal);
        private static List<string> _sortedNames = new List<string>();
        private static int _p, _m, _e;

        /// <summary>Minimum length of a name worth redacting (avoids replacing "M1" inside words).</summary>
        public const int MinNameLength = 3;

        public static void RegisterProject(string name) => Register(name, () => $"PRJ-{++_p:00}");
        public static void RegisterModule(string name) => Register(name, () => $"MOD-{++_m:00}");
        public static void RegisterElement(string name) => Register(name, () => $"EL-{++_e:0000}");

        private static void Register(string name, Func<string> code)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Replace("(Clone)", "").Trim();
            if (name.Length < MinNameLength || NameToCode.ContainsKey(name)) return;
            string c = code();
            NameToCode[name] = c;
            CodeToName[c] = name;
            _dirty = true;   // sorted lazily: thousands of element names are registered at once
        }

        private static bool _dirty;
        private static void EnsureSorted()
        {
            if (!_dirty) return;
            _sortedNames = NameToCode.Keys.OrderByDescending(k => k.Length).ToList();
            _dirty = false;
        }

        public static int RegisteredCount => NameToCode.Count;

        /// <summary>True when the text contains any registered project, module or element name.</summary>
        public static bool ContainsSensitiveNames(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            EnsureSorted();
            foreach (var n in _sortedNames) if (text.IndexOf(n, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>Replace registered names with codes (longest first).</summary>
        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            EnsureSorted();
            foreach (var n in _sortedNames)
                if (text.IndexOf(n, StringComparison.Ordinal) >= 0) text = text.Replace(n, NameToCode[n]);
            return text;
        }

        /// <summary>Restore codes to names for on-device display.</summary>
        public static string Restore(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var kv in CodeToName.OrderByDescending(k => k.Key.Length))
                text = text.Replace(kv.Key, kv.Value);
            return text;
        }

        public static void SaveMap()
        {
            try
            {
                File.WriteAllText(StudySession.PathIn("redaction_map.json"),
                    JsonConvert.SerializeObject(CodeToName, Formatting.Indented));
            }
            catch (Exception) { /* local convenience file only */ }
        }
    }

    /// <summary>
    /// U14: outbound_payloads.jsonl, one line per request that leaves the device
    /// (endpoint stt, llm or tts), with its size and whether it contains project names.
    /// </summary>
    public static class OutboundPayloadLog
    {
        private static readonly JsonlLog Log = new JsonlLog("outbound_payloads.jsonl");

        public static void Write(string endpoint, long bytes, bool containsProjectNames)
        {
            Log.Write(new
            {
                session_id = StudySession.SessionId,
                condition = StudySession.Condition,
                build_version = StudySession.BuildVersion,
                request_ts = StudySession.NowIso(),
                endpoint,
                bytes,
                contains_project_names = containsProjectNames,
                redaction_mode = StudySession.RestrictedDataMode
            });
        }
    }

    /// <summary>U4: llm_requests.jsonl, one object per LLM call.</summary>
    public class LlmCallRecord
    {
        public string session_id = StudySession.SessionId;
        public string participant_id = StudySession.ParticipantId;
        public string condition = StudySession.Condition;
        public string build_version = StudySession.BuildVersion;
        public string prompt_version;
        public string event_id;
        public string element_id;
        public string request_ts;
        public string response_ts;
        public string call_type;          // question | report_narrative | benchmark
        public string provider;
        public string model_id;
        public float temperature;
        public int max_tokens;
        public bool json_mode;
        public string system_hash;
        public string context_hash;
        public int context_chars;
        public string context_text;       // full block, or a pointer in restricted-data mode
        public bool context_contains_event_id;
        public bool llm_context_enabled = StudySession.LlmContextEnabled;
        public string user_message;
        public int? prompt_tokens;
        public int? completion_tokens;
        public bool ok;
        public string response_text;
        public List<string> ids_in_response = new List<string>();
        public List<string> numbers_in_response = new List<string>();
        public List<string> unsupported_tokens = new List<string>();
        public bool validator_flag;
        public bool redaction_mode = StudySession.RestrictedDataMode;
    }

    public static class LlmRequestLog
    {
        private static readonly JsonlLog Log = new JsonlLog("llm_requests.jsonl");
        public static void Write(LlmCallRecord r) => Log.Write(r);
    }
}
