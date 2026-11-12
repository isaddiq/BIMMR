using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BIMMR.Inspection.Study
{
    /// <summary>
    /// U6: post-generation check of an LLM answer against the context block that was
    /// injected with the request (link L5).
    ///
    /// Matching rule (stated in Section 3.2.3):
    ///   * an element identifier is supported when it appears verbatim in the context;
    ///   * a number is supported when it matches a context value within 1 % after unit
    ///     normalisation (mm/cm/m, mm²/m², W/kW, minutes/hours); unit-less context values
    ///     are compared both in the raw and in the normalised unit.
    /// Every unsupported token is returned, not only the flag.
    /// </summary>
    public static class LLMResponseValidator
    {
        public class Result
        {
            public List<string> idsInResponse = new List<string>();
            public List<string> numbersInResponse = new List<string>();
            public List<string> unsupportedTokens = new List<string>();
            public bool Flag => unsupportedTokens.Count > 0;
        }

        public const double RelativeTolerance = 0.01;

        // Identifiers seen in the two datasets and in IFC exports:
        //   GUID (8-4-4-4-12), IFC GlobalId (22 chars), ArchiCAD-style codes (W-001, D-104,
        //   M3-1, ST-12), and Revit element ids (6-9 digit integers).
        private static readonly Regex GuidRx = new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?:-[0-9a-fA-F]{8})?\b", RegexOptions.Compiled);
        private static readonly Regex IfcGuidRx = new Regex(@"(?<![0-9A-Za-z_$])[0-9A-Za-z_$]{22}(?![0-9A-Za-z_$])", RegexOptions.Compiled);
        private static readonly Regex CodeRx = new Regex(@"\b[A-Z][A-Z0-9]{0,5}-\d{1,5}(?:-\d{1,4})?\b", RegexOptions.Compiled);
        private static readonly Regex RevitIdRx = new Regex(@"(?<![\d.,])\d{6,9}(?![\d.,])", RegexOptions.Compiled);

        // Number with optional unit. Thousands separators are accepted ("2,730").
        private static readonly Regex NumberRx = new Regex(
            @"(?<![\w.])(-?\d{1,3}(?:,\d{3})+(?:\.\d+)?|-?\d+(?:\.\d+)?)\s*" +
            @"(mm²|mm2|m²|m2|sqm|square met(?:er|re)s?|mm|cm|millimet(?:er|re)s?|centimet(?:er|re)s?|met(?:er|re)s?|m\b|kw|kilowatts?|w\b|watts?|minutes?|mins?\b|hours?|h\b|%|percent)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static Result Validate(string response, string context)
        {
            var r = new Result();
            if (string.IsNullOrWhiteSpace(response)) return r;
            context = context ?? "";

            // 1. Identifiers.
            var idSpans = new List<(int start, int len)>();
            void CollectIds(Regex rx, Func<string, bool> keep)
            {
                foreach (Match m in rx.Matches(response))
                {
                    if (idSpans.Any(s => m.Index < s.start + s.len && s.start < m.Index + m.Length)) continue;
                    if (!keep(m.Value)) continue;
                    idSpans.Add((m.Index, m.Length));
                    if (!r.idsInResponse.Contains(m.Value)) r.idsInResponse.Add(m.Value);
                }
            }
            CollectIds(GuidRx, _ => true);
            CollectIds(IfcGuidRx, v => v.Any(char.IsDigit) && v.Any(char.IsLetter));
            CollectIds(CodeRx, _ => true);
            CollectIds(RevitIdRx, _ => true);

            foreach (var id in r.idsInResponse)
                if (context.IndexOf(id, StringComparison.Ordinal) < 0)
                    r.unsupportedTokens.Add("id:" + id);

            // 2. Numbers (outside identifiers).
            var contextValues = ExtractNumbers(context, null);
            foreach (var n in ExtractNumbers(response, idSpans))
            {
                r.numbersInResponse.Add(n.text);
                if (!contextValues.Any(c => Matches(n, c)))
                    r.unsupportedTokens.Add("num:" + n.text);
            }
            return r;
        }

        private struct Num
        {
            public string text;
            public double raw;
            public double normalized;
            public string dim;   // "", "len", "area", "power", "time", "pct"
        }

        private static List<Num> ExtractNumbers(string text, List<(int start, int len)> skip)
        {
            var list = new List<Num>();
            foreach (Match m in NumberRx.Matches(text))
            {
                var g = m.Groups[1];
                if (skip != null && skip.Any(s => g.Index < s.start + s.len && s.start < g.Index + g.Length)) continue;
                if (!double.TryParse(g.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
                string unit = m.Groups[2].Success ? m.Groups[2].Value.ToLowerInvariant() : "";
                Normalize(v, unit, out double norm, out string dim);
                list.Add(new Num { text = m.Value.Trim(), raw = v, normalized = norm, dim = dim });
            }
            return list;
        }

        private static void Normalize(double v, string u, out double norm, out string dim)
        {
            norm = v; dim = "";
            if (u.Length == 0) return;
            if (u == "mm²" || u == "mm2") { norm = v / 1e6; dim = "area"; }
            else if (u == "m²" || u == "m2" || u == "sqm" || u.StartsWith("square")) { norm = v; dim = "area"; }
            else if (u == "mm" || u.StartsWith("millimet")) { norm = v / 1000.0; dim = "len"; }
            else if (u == "cm" || u.StartsWith("centimet")) { norm = v / 100.0; dim = "len"; }
            else if (u == "m" || u.StartsWith("met")) { norm = v; dim = "len"; }
            else if (u == "kw" || u.StartsWith("kilowatt")) { norm = v * 1000.0; dim = "power"; }
            else if (u == "w" || u.StartsWith("watt")) { norm = v; dim = "power"; }
            else if (u.StartsWith("min")) { norm = v; dim = "time"; }
            else if (u.StartsWith("hour") || u == "h") { norm = v * 60.0; dim = "time"; }
            else if (u == "%" || u == "percent") { norm = v; dim = "pct"; }
        }

        private static bool Close(double a, double b)
        {
            if (a == b) return true;
            double scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return scale > 0 && Math.Abs(a - b) <= RelativeTolerance * scale;
        }

        private static bool Matches(Num resp, Num ctx)
        {
            // Same dimension: compare normalised values.
            if (resp.dim.Length > 0 && resp.dim == ctx.dim) return Close(resp.normalized, ctx.normalized);
            // Context value without unit (CSV fields carry bare numbers): compare raw and
            // normalised, so "42 square meters" matches "Area: 42" and "2.4 m" matches "2400".
            if (ctx.dim.Length == 0)
                return Close(resp.raw, ctx.raw) || Close(resp.normalized, ctx.raw) ||
                       (resp.dim == "len" && Close(resp.normalized * 1000.0, ctx.raw));
            // Response without unit: compare raw against both.
            if (resp.dim.Length == 0) return Close(resp.raw, ctx.raw) || Close(resp.raw, ctx.normalized);
            return false;
        }
    }
}
