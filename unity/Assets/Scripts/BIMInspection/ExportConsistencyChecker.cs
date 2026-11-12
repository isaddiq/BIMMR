using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BIMMR.Inspection.Study;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// U8: export consistency check. For every exported record, compares the JSON record
    /// (hash recomputed from the file), the HTML section (hash and fields embedded as data
    /// attributes) and the session-log line, and writes consistency_report.csv with one row
    /// per record:
    ///   json_sha256, html_sha256_embedded, log_sha256,
    ///   id_match, timestamp_match, screenshot_path_match, screenshot_exists, all_consistent.
    /// </summary>
    public static class ExportConsistencyChecker
    {
        private static readonly Regex SectionRx = new Regex(
            "<section class=\"record\"([^>]*)>", RegexOptions.Compiled);
        private static readonly Regex AttrRx = new Regex(
            "data-([a-z0-9-]+)=\"([^\"]*)\"", RegexOptions.Compiled);

        public static (int consistent, int total) Run(string jsonPath, string htmlPath, string exportLogPath)
        {
            var log = new CsvLog("consistency_report.csv", true,
                "event_id", "json_sha256", "html_sha256_embedded", "log_sha256",
                "id_match", "timestamp_match", "screenshot_path_match", "screenshot_exists", "all_consistent");

            var html = ReadHtmlSections(htmlPath);
            var logRows = ReadExportLog(exportLogPath);
            int ok = 0, total = 0;

            JArray records;
            try
            {
                // Dates must stay strings: re-serialising a parsed DateTime can change its text
                // (e.g. trailing zeros in milliseconds) and would break the hash comparison.
                using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(File.ReadAllText(jsonPath)))
                       { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                    records = (JArray)JObject.Load(reader)["records"];
            }
            catch (Exception e)
            {
                Debug.LogError($"[BIM Inspection] Consistency check could not read JSON: {e.Message}");
                return (0, 0);
            }

            foreach (JObject rec in records)
            {
                total++;
                string eventId = (string)rec["event_id"] ?? "";
                string id = (string)rec["element_id"] ?? "";
                string ts = (string)rec["timestamp"] ?? "";
                string shot = (string)rec["screenshot_path"] ?? "";
                string recomputed = BIMInspectionExporter.CanonicalSha256(rec);
                string stored = (string)rec["json_sha256"] ?? "";

                html.TryGetValue(eventId, out var h);
                logRows.TryGetValue(eventId, out var l);
                string hHash = h != null && h.TryGetValue("sha256", out var hv) ? hv : "";
                string lHash = l != null && l.TryGetValue("json_sha256", out var lv) ? lv : "";

                bool idMatch = h != null && l != null && Eq(h, "element-id", id) && Eq(l, "element_id", id);
                bool tsMatch = h != null && l != null && Eq(h, "timestamp", ts) && Eq(l, "timestamp", ts);
                bool pathMatch = h != null && l != null && Eq(h, "screenshot", shot) && Eq(l, "screenshot_path", shot);
                bool exists = !string.IsNullOrEmpty(shot) && File.Exists(shot);
                bool hashes = stored == recomputed && hHash == recomputed && lHash == recomputed;
                bool all = idMatch && tsMatch && pathMatch && exists && hashes;
                if (all) ok++;

                log.Write(eventId, recomputed, hHash, lHash, idMatch, tsMatch, pathMatch, exists, all);
            }
            return (ok, total);
        }

        private static bool Eq(Dictionary<string, string> d, string key, string expected) =>
            d.TryGetValue(key, out var v) && v == expected;

        private static Dictionary<string, Dictionary<string, string>> ReadHtmlSections(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            if (!File.Exists(path)) return result;
            foreach (Match m in SectionRx.Matches(File.ReadAllText(path)))
            {
                var attrs = new Dictionary<string, string>();
                foreach (Match a in AttrRx.Matches(m.Groups[1].Value))
                    attrs[a.Groups[1].Value] = System.Net.WebUtility.HtmlDecode(a.Groups[2].Value);
                if (attrs.TryGetValue("event-id", out var id)) result[id] = attrs;
            }
            return result;
        }

        private static Dictionary<string, Dictionary<string, string>> ReadExportLog(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            if (!File.Exists(path)) return result;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return result;
            var header = SplitCsv(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                var vals = SplitCsv(lines[i]);
                var row = new Dictionary<string, string>();
                for (int j = 0; j < header.Count && j < vals.Count; j++) row[header[j]] = vals[j];
                if (row.TryGetValue("event_id", out var id)) result[id] = row;   // last export wins
            }
            return result;
        }

        private static List<string> SplitCsv(string line)
        {
            var res = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { if (q && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else q = !q; }
                else if (c == ',' && !q) { res.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            res.Add(cur.ToString());
            return res;
        }
    }
}
