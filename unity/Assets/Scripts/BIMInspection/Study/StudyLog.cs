using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace BIMMR.Inspection.Study
{
    /// <summary>
    /// Append-only CSV log in Logs/&lt;session_id&gt;/. The four common columns
    /// (session_id, participant_id, condition, build_version) are prepended to every row,
    /// so files can be joined on session_id and event_id without extra bookkeeping.
    /// </summary>
    public sealed class CsvLog
    {
        private readonly string _fileName;
        private readonly string[] _columns;
        private readonly bool _withCommon;

        public CsvLog(string fileName, bool withCommonColumns, params string[] columns)
        {
            _fileName = fileName;
            _columns = columns;
            _withCommon = withCommonColumns;
        }

        public string FilePath => StudySession.PathIn(_fileName);

        public void Write(params object[] values)
        {
            if (values.Length != _columns.Length)
            {
                Debug.LogError($"[StudyLog] {_fileName}: expected {_columns.Length} values, got {values.Length}.");
                return;
            }
            try
            {
                string path = FilePath;
                bool newFile = !File.Exists(path);
                var sb = new StringBuilder();
                if (newFile)
                {
                    var header = new List<string>();
                    if (_withCommon) header.AddRange(StudySession.CommonHeader);
                    header.AddRange(_columns);
                    sb.AppendLine(string.Join(",", header));
                }
                var row = new List<string>();
                if (_withCommon) foreach (var v in StudySession.CommonValues()) row.Add(Escape(v));
                foreach (var v in values) row.Add(Escape(Format(v)));
                sb.AppendLine(string.Join(",", row));
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError($"[StudyLog] Could not write {_fileName}: {e.Message}");
            }
        }

        public static string Format(object v)
        {
            switch (v)
            {
                case null: return "";
                case bool b: return b ? "true" : "false";
                case float f: return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                case Vector3 p: return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F4};{1:F4};{2:F4}", p.x, p.y, p.z);
                case IFormattable fm: return fm.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        public static string Escape(string v)
        {
            if (v == null) return "";
            bool quote = v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            v = v.Replace("\"", "\"\"");
            return quote ? $"\"{v}\"" : v;
        }
    }

    /// <summary>Append-only JSON Lines log (one object per line).</summary>
    public sealed class JsonlLog
    {
        private readonly string _fileName;
        public JsonlLog(string fileName) { _fileName = fileName; }
        public string FilePath => StudySession.PathIn(_fileName);

        public void Write(object record)
        {
            try
            {
                string line = JsonConvert.SerializeObject(record, Formatting.None);
                File.AppendAllText(FilePath, line + "\n", new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError($"[StudyLog] Could not write {_fileName}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// session_events.csv: one row per milestone (session_start, module_loaded,
    /// module_pose_confirmed, inspection_start, evidence_captured, inspection_end,
    /// report_export_done, re_registration, ...). Task-completion and reporting times
    /// in Table 10 are differences between these timestamps.
    /// </summary>
    public static class SessionEvents
    {
        private static readonly CsvLog Log_ = new CsvLog("session_events.csv", true,
            "ts", "event", "event_id", "detail");

        public static void Log(string evt, string detail = "", string eventId = "")
        {
            Log_.Write(StudySession.NowIso(), evt, eventId, detail);
        }
    }

    public static class Hashing
    {
        public static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
