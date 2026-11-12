// ResponseScorer.cs - Deterministic scoring of a model response against computed ground truth.
//
// Rules that fix the defects of the previous evaluator:
//  * A metric that does not apply to a question is recorded as NaN and excluded from aggregation.
//    Nothing is ever silently credited with 1.0 because ground truth was missing.
//  * Element IDs are matched against real BIM identifiers from the scene index.
//  * Abstention is scored in both directions: correct refusal AND over-refusal on answerable items.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AEC.Benchmark
{
    public class ScoredResponse
    {
        public bool ParsedAsJson;
        public double FormatAdherence;        // strict binary: valid JSON with all required keys and types
        public double FormatFieldRatio;       // fraction of required keys present and well typed

        public double EntityF1 = double.NaN;
        public double GroundingOverlap = double.NaN;
        public double NumericRelativeError = double.NaN;
        public double NumericSuccess1 = double.NaN;
        public double NumericSuccess5 = double.NaN;
        public double GroupAccuracy = double.NaN;
        public double Coverage = double.NaN;

        public double IdHallucinationRate;    // share of cited IDs that do not exist in the scene
        public int InventedIdCount;
        public bool Abstained;
        public double SafeRefusal = double.NaN;
        public double OverAbstention = double.NaN;

        public bool TaskSuccess;
        public double TaskScore0To100;

        public List<string> PredictedIds = new List<string>();
        public List<string> PredictedEntities = new List<string>();
        public string AnswerText = "";
        public string ReasonCode = "";
        public string FailureNote = "";
    }

    public static class ResponseScorer
    {
        private static readonly string[] RequiredKeys =
            { "answer", "entities", "element_ids", "confidence", "hallucination_flag" };

        private static readonly string[] RefusalMarkers =
        {
            "not available in bim metadata", "cannot be determined", "cannot determine", "not derivable",
            "no information", "not present in the provided", "not provided", "unable to", "cannot answer",
            "insufficient metadata", "no metadata", "out of scope", "outside the scope", "not recorded",
            "cannot be answered", "no such data", "does not contain", "not populated", "i don't have"
        };

        private static readonly Regex NumberRegex =
            new Regex(@"-?\d{1,3}(?:,\d{3})+(?:\.\d+)?|-?\d+(?:\.\d+)?", RegexOptions.Compiled);

        public static ScoredResponse Score(string rawResponse, GroundTruth gt, BimSceneIndex index)
        {
            var s = new ScoredResponse();
            if (string.IsNullOrWhiteSpace(rawResponse)) rawResponse = "";

            JObject json = TryParseJson(rawResponse, out bool strictJson);
            s.ParsedAsJson = json != null;

            int present = 0;
            if (json != null)
            {
                foreach (var key in RequiredKeys) if (HasWellTypedKey(json, key)) present++;
            }
            s.FormatFieldRatio = present / (double)RequiredKeys.Length;
            s.FormatAdherence = (strictJson && present == RequiredKeys.Length) ? 1.0 : 0.0;

            ExtractContent(rawResponse, json, s);

            // ---- grounding hygiene (always computed)
            s.InventedIdCount = s.PredictedIds.Count(id => !index.ValidElementIds.Contains(id));
            s.IdHallucinationRate = s.PredictedIds.Count == 0 ? 0.0 : s.InventedIdCount / (double)s.PredictedIds.Count;

            s.Abstained = DetectAbstention(json, s);

            // ---- abstention items
            if (gt.ExpectAbstention)
            {
                bool clean = s.Abstained && s.PredictedIds.Count == 0 && s.InventedIdCount == 0;
                s.SafeRefusal = clean ? 1.0 : 0.0;
                s.TaskSuccess = clean;
                s.TaskScore0To100 = clean ? 100 : 0;
                if (!clean) s.FailureNote = s.Abstained ? "refused but still cited element IDs" : "answered a question the metadata cannot support";
                return s;
            }

            s.OverAbstention = s.Abstained ? 1.0 : 0.0;

            // ---- content metrics
            if (gt.ScoreEntities) s.EntityF1 = EntityF1(s.PredictedEntities, gt.Entities, s.AnswerText);
            if (gt.ScoreIds) s.GroundingOverlap = Jaccard(s.PredictedIds, gt.ElementIds);
            if (gt.ScoreNumeric && gt.Numeric.HasValue) ScoreNumeric(s, gt.Numeric.Value);
            if (gt.ScoreGroups && gt.Groups.Count > 0) s.GroupAccuracy = GroupAccuracy(s.AnswerText, gt.Groups);
            if (gt.Triples.Count > 0) s.Coverage = TripleCoverage(rawResponse, gt.Triples);

            // ---- composite gate
            var gates = new List<bool> { s.FormatAdherence >= 1.0, s.InventedIdCount == 0, !s.Abstained };
            if (!double.IsNaN(s.EntityF1)) gates.Add(s.EntityF1 >= 0.8);
            if (!double.IsNaN(s.GroundingOverlap)) gates.Add(s.GroundingOverlap >= 0.7);
            if (!double.IsNaN(s.NumericSuccess1)) gates.Add(s.NumericSuccess1 >= 1.0);
            if (!double.IsNaN(s.GroupAccuracy)) gates.Add(s.GroupAccuracy >= 0.8);
            if (!double.IsNaN(s.Coverage)) gates.Add(s.Coverage >= 0.8);

            s.TaskSuccess = gates.All(g => g);
            s.TaskScore0To100 = 100.0 * gates.Count(g => g) / Math.Max(1, gates.Count);

            if (!s.TaskSuccess)
            {
                var failed = new List<string>();
                if (s.FormatAdherence < 1) failed.Add("schema");
                if (s.InventedIdCount > 0) failed.Add($"{s.InventedIdCount} invented IDs");
                if (s.Abstained) failed.Add("over-abstained");
                if (!double.IsNaN(s.EntityF1) && s.EntityF1 < 0.8) failed.Add($"entityF1={s.EntityF1:F2}");
                if (!double.IsNaN(s.GroundingOverlap) && s.GroundingOverlap < 0.7) failed.Add($"grounding={s.GroundingOverlap:F2}");
                if (!double.IsNaN(s.NumericSuccess1) && s.NumericSuccess1 < 1) failed.Add($"numeric(err={s.NumericRelativeError:F3})");
                if (!double.IsNaN(s.GroupAccuracy) && s.GroupAccuracy < 0.8) failed.Add($"groups={s.GroupAccuracy:F2}");
                if (!double.IsNaN(s.Coverage) && s.Coverage < 0.8) failed.Add($"coverage={s.Coverage:F2}");
                s.FailureNote = string.Join("; ", failed);
            }

            return s;
        }

        // ------------------------------------------------------------ parsing

        private static JObject TryParseJson(string raw, out bool strict)
        {
            strict = false;
            var trimmed = raw.Trim();

            // Strip markdown fences without treating the result as strictly compliant.
            bool fenced = trimmed.StartsWith("```", StringComparison.Ordinal);
            if (fenced)
            {
                int first = trimmed.IndexOf('\n');
                int last = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (first > 0 && last > first) trimmed = trimmed.Substring(first + 1, last - first - 1).Trim();
            }

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    var obj = JObject.Parse(trimmed);
                    strict = !fenced;
                    return obj;
                }
                catch { /* fall through to embedded object search */ }
            }

            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try { return JObject.Parse(trimmed.Substring(start, end - start + 1)); }
                catch { return null; }
            }
            return null;
        }

        private static bool HasWellTypedKey(JObject json, string key)
        {
            var token = json[key];
            if (token == null) return false;
            switch (key)
            {
                case "answer": return token.Type == JTokenType.String;
                case "entities":
                case "element_ids": return token.Type == JTokenType.Array;
                case "confidence": return token.Type == JTokenType.Float || token.Type == JTokenType.Integer;
                case "hallucination_flag": return token.Type == JTokenType.Boolean;
                default: return true;
            }
        }

        private static void ExtractContent(string raw, JObject json, ScoredResponse s)
        {
            if (json != null)
            {
                s.AnswerText = json["answer"]?.ToString() ?? "";
                s.ReasonCode = json["reason_code"]?.ToString() ?? "";

                if (json["element_ids"] is JArray ids)
                    s.PredictedIds = ids.Select(t => Normalize(t.ToString())).Where(v => v.Length > 0).Distinct().ToList();
                if (json["entities"] is JArray entities)
                    s.PredictedEntities = entities.Select(t => Normalize(t.ToString())).Where(v => v.Length > 0).Distinct().ToList();

                // A model may put the substance in the answer string only; keep the full text for
                // numeric/group scoring so prose answers are not unfairly penalized on content.
                if (string.IsNullOrEmpty(s.AnswerText)) s.AnswerText = raw;
            }
            else
            {
                s.AnswerText = raw;
            }
        }

        private static bool DetectAbstention(JObject json, ScoredResponse s)
        {
            if (json != null)
            {
                var answerable = json["answerable"];
                if (answerable != null && answerable.Type == JTokenType.Boolean && !answerable.Value<bool>())
                    return true;
            }
            var lower = (s.AnswerText ?? "").ToLowerInvariant();
            return RefusalMarkers.Any(m => lower.Contains(m));
        }

        // ------------------------------------------------------------ metrics

        private static string Normalize(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            var t = v.Trim().Trim('"', '\'', '.', ',', ';', ':').Replace('–', '-').Replace('—', '-');
            return Regex.Replace(t, @"\s+", " ").ToLowerInvariant();
        }

        private static double EntityF1(List<string> predicted, HashSet<string> gold, string answerText)
        {
            var goldNorm = new HashSet<string>(gold.Select(Normalize).Where(v => v.Length > 0));
            if (goldNorm.Count == 0) return double.NaN;

            var predNorm = new HashSet<string>(predicted.Select(Normalize).Where(v => v.Length > 0));

            // Fall back to the answer prose when the entities array was not used.
            if (predNorm.Count == 0 && !string.IsNullOrEmpty(answerText))
            {
                var lower = answerText.ToLowerInvariant();
                foreach (var g in goldNorm) if (lower.Contains(g)) predNorm.Add(g);
                if (predNorm.Count == 0) return 0.0;
            }
            if (predNorm.Count == 0) return 0.0;

            int hit = predNorm.Count(p => goldNorm.Contains(p));
            double precision = hit / (double)predNorm.Count;
            double recall = hit / (double)goldNorm.Count;
            return (precision + recall) <= 0 ? 0.0 : 2 * precision * recall / (precision + recall);
        }

        private static double Jaccard(List<string> predicted, HashSet<string> gold)
        {
            var p = new HashSet<string>(predicted.Select(Normalize).Where(v => v.Length > 0));
            var g = new HashSet<string>(gold.Select(Normalize).Where(v => v.Length > 0));

            if (g.Count == 0) return p.Count == 0 ? 1.0 : 0.0;   // empty truth: citing IDs is wrong
            if (p.Count == 0) return 0.0;

            int inter = p.Count(x => g.Contains(x));
            int union = p.Union(g).Count();
            return union == 0 ? 0.0 : inter / (double)union;
        }

        private static void ScoreNumeric(ScoredResponse s, double expected)
        {
            var numbers = ExtractNumbers(s.AnswerText);
            if (numbers.Count == 0)
            {
                s.NumericRelativeError = double.PositiveInfinity;
                s.NumericSuccess1 = 0; s.NumericSuccess5 = 0;
                return;
            }

            double first = numbers[0];
            double err = Math.Abs(first - expected) / (Math.Abs(expected) + 1e-6);
            s.NumericRelativeError = err;
            s.NumericSuccess1 = err <= 0.01 ? 1 : 0;
            s.NumericSuccess5 = err <= 0.05 ? 1 : 0;

            // Credit the correct value appearing anywhere in a longer structured answer,
            // but only after the leading number failed, and never for the 1% gate.
            if (s.NumericSuccess5 < 1 && numbers.Any(n => Math.Abs(n - expected) / (Math.Abs(expected) + 1e-6) <= 0.05))
                s.NumericSuccess5 = 1;
        }

        /// <summary>Answer field of a JSON response, or the raw text when it is not JSON.</summary>
        public static string ExtractAnswerText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var json = TryParseJson(raw, out _);
            var answer = json?["answer"]?.ToString();
            return string.IsNullOrEmpty(answer) ? raw : answer;
        }

        /// <summary>
        /// True when the leading numeric claim of two answers matches within 1%. Used to score
        /// multi-turn recall as self-consistency rather than against the scene a second time.
        /// </summary>
        public static bool NumbersAgree(string earlierAnswer, string laterAnswer)
        {
            var a = ExtractNumbers(earlierAnswer ?? "");
            var b = ExtractNumbers(laterAnswer ?? "");
            if (a.Count == 0 || b.Count == 0) return false;
            double expected = a[0];
            return b.Any(v => Math.Abs(v - expected) <= Math.Max(0.01, Math.Abs(expected) * 0.01));
        }

        private static List<double> ExtractNumbers(string text)
        {
            var list = new List<double>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (Match m in NumberRegex.Matches(text))
            {
                if (double.TryParse(m.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
            }
            return list;
        }

        private static double GroupAccuracy(string answerText, Dictionary<string, double> groups)
        {
            if (groups.Count == 0) return double.NaN;
            if (string.IsNullOrEmpty(answerText)) return 0.0;

            var lower = answerText.ToLowerInvariant();
            int correct = 0;
            foreach (var kv in groups)
            {
                var key = Normalize(kv.Key);
                var keyTokens = key.Split('|');
                bool keyPresent = keyTokens.All(t => t.Length == 0 || lower.Contains(t));
                if (!keyPresent) continue;

                // The expected value must appear somewhere in the answer.
                bool valuePresent = ExtractNumbers(answerText)
                    .Any(n => Math.Abs(n - kv.Value) <= Math.Max(0.01, Math.Abs(kv.Value) * 0.01));
                if (valuePresent) correct++;
            }
            return correct / (double)groups.Count;
        }

        private static double TripleCoverage(string raw, List<BimTriple> required)
        {
            if (required.Count == 0) return double.NaN;
            var lower = (raw ?? "").ToLowerInvariant();
            int hit = required.Count(t =>
                lower.Contains(t.ElementId.ToLowerInvariant()) &&
                lower.Contains((t.Value ?? "").ToLowerInvariant()));
            return hit / (double)required.Count;
        }
    }
}
