using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Exports inspection data to disk. JSON is written as a single document
    /// containing every record, every issue, and the progress summary. CSV is
    /// flat — one row per issue (or one row per record if no issues exist).
    ///
    /// Files are written under <see cref="Application.persistentDataPath"/> in
    /// a <c>BIMInspectionExports</c> folder. Filenames include a UTC timestamp.
    ///
    /// TODO(cloud-sync): swap or extend these methods to push to Firebase /
    /// REST / BCF without changing callers in <see cref="BIMInspectionManager"/>.
    /// </summary>
    public class BIMInspectionExporter : MonoBehaviour
    {
        [Tooltip("Subdirectory under persistentDataPath where exports are written.")]
        public string exportFolderName = "BIMInspectionExports";

        [Tooltip("If true, also include the progress summary in JSON exports.")]
        public bool includeProgressInJson = true;

        // Callback used to inject extra "header" info (model name, project,
        // inspector) into the JSON payload. Optional.
        public Func<Dictionary<string, string>> headerProvider;
        // Optional progress source, so JSON exports include the latest summary.
        public Func<InspectionProgressSummary> progressProvider;

        /// <summary>Write all records to a timestamped JSON file. Returns the path.</summary>
        public string ExportToJson(Dictionary<string, InspectionRecord> records)
        {
            string dir = EnsureExportDir();
            string path = Path.Combine(dir, $"inspection_{Timestamp()}.json");

            var payload = new ExportPayload
            {
                exportedAt = DateTime.UtcNow.ToString("o"),
                header = headerProvider != null ? headerProvider() : null,
                records = records != null
                              ? new List<InspectionRecord>(records.Values)
                              : new List<InspectionRecord>(),
                progress = includeProgressInJson && progressProvider != null
                                ? progressProvider() : null
            };

            // JsonUtility cannot serialise Dictionary<string, string>; convert.
            var json = JsonUtility.ToJson(SerializableExport.From(payload), prettyPrint: true);
            File.WriteAllText(path, json);
            Debug.Log($"[BIM Inspection] JSON exported -> {path}");
            return path;
        }

        /// <summary>Write all records to a timestamped CSV file. One row per
        /// issue (or one row per record when there are no issues).</summary>
        public string ExportToCsv(Dictionary<string, InspectionRecord> records)
        {
            string dir = EnsureExportDir();
            string path = Path.Combine(dir, $"inspection_{Timestamp()}.csv");

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", new[]
            {
                "RecordId","ElementStableId","ElementName","Category","Level","Status",
                "LastUpdated","Inspector",
                "IssueId","IssueType","Severity","IssueComment","IssueResolved",
                "IssueX","IssueY","IssueZ","IssueCreatedAt","ScreenshotPath",
                "RecordComments"
            }));

            if (records != null)
            {
                foreach (var r in records.Values)
                {
                    if (r == null) continue;
                    string comments = r.comments != null
                        ? string.Join(" | ", r.comments)
                        : string.Empty;

                    if (r.issues == null || r.issues.Count == 0)
                    {
                        sb.AppendLine(string.Join(",", new[]
                        {
                            Csv(r.recordId), Csv(r.elementStableId), Csv(r.elementName),
                            Csv(r.category), Csv(r.level), Csv(r.status.ToString()),
                            Csv(r.lastUpdated), Csv(r.inspectorName),
                            "","","","","","","","","","",
                            Csv(comments)
                        }));
                    }
                    else
                    {
                        foreach (var i in r.issues)
                        {
                            sb.AppendLine(string.Join(",", new[]
                            {
                                Csv(r.recordId), Csv(r.elementStableId), Csv(r.elementName),
                                Csv(r.category), Csv(r.level), Csv(r.status.ToString()),
                                Csv(r.lastUpdated), Csv(r.inspectorName),
                                Csv(i.issueId), Csv(i.issueType), Csv(i.severity),
                                Csv(i.comment), Csv(i.resolved.ToString()),
                                Csv(i.worldPosition.x.ToString("R")),
                                Csv(i.worldPosition.y.ToString("R")),
                                Csv(i.worldPosition.z.ToString("R")),
                                Csv(i.createdAt), Csv(i.screenshotPath),
                                Csv(comments)
                            }));
                        }
                    }
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[BIM Inspection] CSV exported -> {path}");
            return path;
        }

        // --- Evidence report (U1, U8, U10, U15) --------------------------------

        /// <summary>
        /// Write the paired evidence report for one session:
        ///   report/inspection_&lt;session&gt;.json  machine-readable records, each with json_sha256
        ///   report/inspection_&lt;session&gt;.html  human-readable report with embedded images; each
        ///                                    record carries its id, timestamp, screenshot path
        ///                                    and SHA-256 as data attributes
        ///   export_log.csv                    session-log line per record (id, ts, path, hash)
        ///   field_presence.csv                0/1 per required field per record (U10)
        ///   consistency_report.csv            cross-check of the three artifacts (U8)
        /// With the structured schema switched off (U15 ablation) only a free-text note file
        /// is written. Returns a short summary for the session log.
        /// </summary>
        public string ExportEvidenceReport(IReadOnlyList<EvidenceRecord> evidence)
        {
            var records = new List<EvidenceRecord>();
            if (evidence != null) foreach (var e in evidence) if (e != null && !e.superseded) records.Add(e);
            string session = Study.StudySession.SessionId;

            if (!Study.StudySession.StructuredSchemaEnabled)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Inspection notes, session {session}, {Study.StudySession.NowIso()}");
                foreach (var r in records)
                    sb.AppendLine($"- {r.elementName}: {r.status}. {r.notes}");
                string notesPath = Study.StudySession.PathIn(Path.Combine("report", $"notes_{session}.txt"));
                File.WriteAllText(notesPath, sb.ToString(), new UTF8Encoding(false));
                return $"free-text notes only ({records.Count} entries)";
            }

            // 1. Hash every record over its canonical JSON (hash field removed).
            foreach (var r in records) r.jsonSha256 = CanonicalSha256(r);

            // 2. JSON.
            string jsonPath = Study.StudySession.PathIn(Path.Combine("report", $"inspection_{session}.json"));
            var doc = new Newtonsoft.Json.Linq.JObject
            {
                ["session_id"] = session,
                ["participant_id"] = Study.StudySession.ParticipantId,
                ["condition"] = Study.StudySession.Condition,
                ["build_version"] = Study.StudySession.BuildVersion,
                ["exported_at"] = Study.StudySession.NowIso(),
                ["records"] = Newtonsoft.Json.Linq.JArray.FromObject(records)
            };
            File.WriteAllText(jsonPath, doc.ToString(Newtonsoft.Json.Formatting.Indented), new UTF8Encoding(false));

            // 3. HTML.
            string htmlPath = Path.ChangeExtension(jsonPath, ".html");
            File.WriteAllText(htmlPath, BuildHtml(records, session), new UTF8Encoding(false));

            // 4. Session-log line per record.
            var exportLog = new Study.CsvLog("export_log.csv", true,
                "event_id", "element_id", "timestamp", "screenshot_path", "json_sha256");
            foreach (var r in records)
                exportLog.Write(r.eventId, r.elementId, r.timestamp, r.screenshotPath, r.jsonSha256);

            // 5. Field presence (U10).
            var cols = new List<string> { "event_id" };
            cols.AddRange(EvidenceRecord.RequiredFields);
            cols.Add("n_required"); cols.Add("n_present");
            var presence = new Study.CsvLog("field_presence.csv", true, cols.ToArray());
            foreach (var r in records)
            {
                var row = new List<object> { r.eventId };
                int present = 0;
                foreach (var f in EvidenceRecord.RequiredFields)
                {
                    bool ok = !string.IsNullOrWhiteSpace(r.Field(f));
                    if (ok) present++;
                    row.Add(ok ? 1 : 0);
                }
                row.Add(EvidenceRecord.RequiredFields.Length);
                row.Add(present);
                presence.Write(row.ToArray());
            }

            // 6. Consistency check (U8).
            var (consistent, total) = ExportConsistencyChecker.Run(jsonPath, htmlPath, exportLog.FilePath);
            Debug.Log($"[BIM Inspection] Evidence report -> {htmlPath} ({consistent}/{total} records consistent)");
            return $"{records.Count} records; {consistent}/{total} consistent";
        }

        /// <summary>SHA-256 over the record's JSON with json_sha256 removed (Formatting.None).
        /// The consistency checker recomputes it the same way from the file.</summary>
        public static string CanonicalSha256(EvidenceRecord r)
        {
            var jo = Newtonsoft.Json.Linq.JObject.FromObject(r);
            return CanonicalSha256(jo);
        }

        public static string CanonicalSha256(Newtonsoft.Json.Linq.JObject jo)
        {
            var copy = (Newtonsoft.Json.Linq.JObject)jo.DeepClone();
            copy.Remove("json_sha256");
            return Study.Hashing.Sha256(copy.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static string BuildHtml(List<EvidenceRecord> records, string session)
        {
            string H(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine($"<title>Inspection report {H(session)}</title>");
            sb.AppendLine($"<meta name=\"session-id\" content=\"{H(session)}\">");
            sb.AppendLine("<style>body{font-family:sans-serif;margin:24px;max-width:960px}" +
                          "section{border:1px solid #ccc;border-radius:6px;padding:12px;margin:12px 0}" +
                          "img{max-width:100%}td{padding:2px 8px;vertical-align:top}" +
                          ".narr{background:#f4f6f8;padding:8px}.flag{color:#a33}</style></head><body>");
            sb.AppendLine($"<h1>Inspection report</h1><p>Session {H(session)} · participant {H(Study.StudySession.ParticipantId)} · " +
                          $"condition {H(Study.StudySession.Condition)} · build {H(Study.StudySession.BuildVersion)} · " +
                          $"exported {H(Study.StudySession.NowIso())}</p>");
            foreach (var r in records)
            {
                sb.AppendLine($"<section class=\"record\" data-event-id=\"{H(r.eventId)}\" data-element-id=\"{H(r.elementId)}\" " +
                              $"data-timestamp=\"{H(r.timestamp)}\" data-screenshot=\"{H(r.screenshotPath)}\" " +
                              $"data-sha256=\"{H(r.jsonSha256)}\">");
                sb.AppendLine($"<h2>{H(r.elementName)} <small>({H(r.eventId)})</small></h2><table>");
                void Row(string k, string v) => sb.AppendLine($"<tr><td><b>{H(k)}</b></td><td>{H(v)}</td></tr>");
                Row("Element ID", r.elementId); Row("Timestamp", r.timestamp); Row("Module", r.moduleId);
                Row("Category", r.category); Row("Type", r.elementType); Row("Status", r.status);
                Row("Geometry", r.geometricDescriptors); Row("Notes", r.notes); Row("Report status", r.reportStatus);
                Row("Record SHA-256", r.jsonSha256);
                sb.AppendLine("</table>");
                if (!string.IsNullOrEmpty(r.screenshotPath) && File.Exists(r.screenshotPath))
                {
                    string b64 = Convert.ToBase64String(File.ReadAllBytes(r.screenshotPath));
                    sb.AppendLine($"<img alt=\"evidence {H(r.eventId)}\" src=\"data:image/png;base64,{b64}\">");
                }
                if (!string.IsNullOrWhiteSpace(r.llmNarrative))
                    sb.AppendLine($"<p class=\"narr\"><b>LLM narrative (review before use)</b>" +
                                  $"{(r.llmNarrativeFlagged ? " <span class=\"flag\">[validator: unsupported content]</span>" : "")}" +
                                  $"<br>{H(r.llmNarrative)}</p>");
                sb.AppendLine("</section>");
            }
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        // --- helpers --------------------------------------------------------

        private string EnsureExportDir()
        {
            string dir = Path.Combine(Application.persistentDataPath, exportFolderName);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Timestamp()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        }

        /// <summary>Safely escape a CSV field per RFC 4180.</summary>
        private static string Csv(string v)
        {
            if (v == null) return "";
            bool mustQuote = v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            v = v.Replace("\"", "\"\"");
            return mustQuote ? $"\"{v}\"" : v;
        }

        // ---- JSON serialisation helpers -----------------------------------

        // Intermediate payload before serialisation.
        private class ExportPayload
        {
            public string exportedAt;
            public Dictionary<string, string> header;
            public List<InspectionRecord> records;
            public InspectionProgressSummary progress;
        }

        // JsonUtility can't serialise Dictionary<,> directly, so we flatten
        // headers / progress maps into lists.
        [Serializable]
        private class SerializableExport
        {
            public string exportedAt;
            public List<KeyValueEntry> header;
            public List<InspectionRecord> records;
            public List<CategoryProgress> progressByCategory;
            public List<LevelProgress> progressByLevel;
            public int totalElements;
            public int inspectedElements;
            public int passedElements;
            public int failedElements;
            public int needsReviewElements;
            public int notInspectedElements;
            public float inspectedPercentage;
            public float passedPercentage;

            public static SerializableExport From(ExportPayload p)
            {
                var s = new SerializableExport
                {
                    exportedAt = p.exportedAt,
                    records = p.records ?? new List<InspectionRecord>(),
                    header = new List<KeyValueEntry>()
                };
                if (p.header != null)
                {
                    foreach (var kv in p.header)
                        s.header.Add(new KeyValueEntry { key = kv.Key, value = kv.Value });
                }
                if (p.progress != null)
                {
                    s.totalElements        = p.progress.totalElements;
                    s.inspectedElements    = p.progress.inspectedElements;
                    s.passedElements       = p.progress.passedElements;
                    s.failedElements       = p.progress.failedElements;
                    s.needsReviewElements  = p.progress.needsReviewElements;
                    s.notInspectedElements = p.progress.notInspectedElements;
                    s.inspectedPercentage  = p.progress.inspectedPercentage;
                    s.passedPercentage     = p.progress.passedPercentage;
                    s.progressByCategory = new List<CategoryProgress>(p.progress.progressByCategory.Values);
                    s.progressByLevel    = new List<LevelProgress>(p.progress.progressByLevel.Values);
                }
                return s;
            }
        }

        [Serializable]
        private class KeyValueEntry
        {
            public string key;
            public string value;
        }
    }
}
