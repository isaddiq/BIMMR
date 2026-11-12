// GroundTruthEngine.cs - Computes the expected answer for every benchmark item from the live scene.
//
// Nothing here is question-specific hard-coded data: each BenchmarkItem carries a specification and
// the engine resolves it against the BimSceneIndex. If the metadata a question depends on is absent
// from the loaded model, the expected behaviour becomes "abstain with a reason" rather than a wrong
// or unscorable answer.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AEC.Benchmark
{
    public class BimTriple
    {
        public string ElementId;
        public string Property;
        public string Value;

        public string Canonical => $"{ElementId}|{Property}|{Value}".ToLowerInvariant();
        public override string ToString() => $"({ElementId}, {Property}, {Value})";
    }

    public class GroundTruth
    {
        public string ItemId;
        public string ResolvedPrompt;
        public bool Skip;                    // prompt could not be instantiated in this scene
        public string SkipReason;

        public bool Answerable = true;
        public bool ExpectAbstention;
        public string UnanswerableReason;
        public bool ZeroIsCorrect;           // an empty result set is the correct answer

        public HashSet<string> ElementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<BimTriple> Triples = new List<BimTriple>();
        public double? Numeric;
        public Dictionary<string, double> Groups = new Dictionary<string, double>();
        public string Summary = "";

        public bool ScoreIds;
        public bool ScoreEntities;
        public bool ScoreNumeric;
        public bool ScoreGroups;

        public string AnchorId;
        public string Anchor2Id;

        /// <summary>Elements the context packet must contain for this item to be answerable.</summary>
        public List<BimElementRecord> ScopeElements = new List<BimElementRecord>();
    }

    public class GroundTruthEngine
    {
        private readonly BimSceneIndex _index;

        public GroundTruthEngine(BimSceneIndex index) { _index = index; }

        public GroundTruth Evaluate(BenchmarkItem item)
        {
            var gt = new GroundTruth { ItemId = item.Id, ResolvedPrompt = item.Prompt };

            if (!ResolveAnchors(item, gt)) return gt;

            switch (item.Kind)
            {
                case QueryKind.ListCategoryAttributes: ListCategoryAttributes(item, gt); break;
                case QueryKind.CountCategory: CountCategory(item, gt); break;
                case QueryKind.CountAndSumNumeric: CountAndSumNumeric(item, gt); break;
                case QueryKind.SplitByPropertyValue: SplitByPropertyValue(item, gt); break;
                case QueryKind.SumNumericGroupBy: SumNumericGroupBy(item, gt); break;
                case QueryKind.DistinctPropertyValues: DistinctPropertyValues(item, gt); break;
                case QueryKind.CountAndMode: CountAndMode(item, gt); break;
                case QueryKind.MissingProperty: MissingProperty(item, gt); break;
                case QueryKind.FilterNumericThreshold: FilterNumericThreshold(item, gt); break;
                case QueryKind.FilterNumericThresholdGrouped: FilterNumericThresholdGrouped(item, gt); break;
                case QueryKind.FilterByPropertyValue: FilterByPropertyValue(item, gt); break;
                case QueryKind.GroupCountByProperty: GroupCountByProperty(item, gt); break;
                case QueryKind.CategoryBreakdown: CategoryBreakdown(item, gt); break;
                case QueryKind.DisciplineSummary: DisciplineSummary(item, gt); break;
                case QueryKind.SystemGrouping: SystemGrouping(item, gt); break;
                case QueryKind.QcStatusAggregation: QcStatusAggregation(item, gt); break;
                case QueryKind.PropertyCompleteness: PropertyCompleteness(item, gt); break;
                case QueryKind.DefinedValueSemantics: DefinedValueSemantics(item, gt); break;
                case QueryKind.DuplicateIdentifiers: DuplicateIdentifiers(item, gt); break;
                case QueryKind.MemoryRecall: MemoryRecall(item, gt); break;
                case QueryKind.OutOfScope: OutOfScope(gt); break;
                case QueryKind.UnsupportedRelation: UnsupportedRelation(item, gt); break;
                case QueryKind.UnsupportedCompliance: UnsupportedCompliance(item, gt); break;
                case QueryKind.MetadataOrientation: MetadataOrientation(item, gt); break;
                case QueryKind.SpatialAdjacency: SpatialAdjacency(item, gt); break;
                case QueryKind.SpatialAbove: SpatialAbove(item, gt); break;
                case QueryKind.SpatialWithinDistance: SpatialWithinDistance(item, gt); break;
                case QueryKind.SpatialClash: SpatialClash(item, gt); break;
                case QueryKind.SpatialOrientationGeometry: SpatialOrientationGeometry(item, gt); break;
                case QueryKind.SpatialContainment: SpatialContainment(item, gt); break;
                case QueryKind.SpatialRelativePosition: SpatialRelativePosition(item, gt); break;
                case QueryKind.SpatialClosestPair: SpatialClosestPair(item, gt); break;
                case QueryKind.DisciplineByLevel: DisciplineByLevel(item, gt); break;
                case QueryKind.UnassignedLevel: UnassignedLevel(item, gt); break;
                case QueryKind.CategoryRatio: CategoryRatio(item, gt); break;
                default:
                    gt.Skip = true; gt.SkipReason = "Unhandled query kind";
                    break;
            }

            if (gt.ScopeElements.Count == 0) gt.ScopeElements = Scope(item);
            return gt;
        }

        // ------------------------------------------------------------- helpers

        private List<BimElementRecord> Scope(BenchmarkItem item)
        {
            if (item.Categories != null && item.Categories.Length > 0)
            {
                var all = new List<BimElementRecord>(_index.ByCategory(item.Categories));
                if (item.SecondaryCategories != null && item.SecondaryCategories.Length > 0)
                    all.AddRange(_index.ByCategory(item.SecondaryCategories));
                return all;
            }
            return _index.Elements;
        }

        private bool ResolveAnchors(BenchmarkItem item, GroundTruth gt)
        {
            string prompt = item.Prompt;
            foreach (var token in new[] { "ANCHOR", "ANCHOR2" })
            {
                int start = prompt.IndexOf("{" + token + ":", StringComparison.Ordinal);
                if (start < 0) continue;
                int end = prompt.IndexOf('}', start);
                if (end < 0) continue;

                string category = prompt.Substring(start + token.Length + 2, end - start - token.Length - 2);
                var candidate = _index.ByCategory(category)
                    .Where(e => e.HasBounds)
                    .OrderBy(e => e.ElementId, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (candidate == null)
                {
                    gt.Skip = true;
                    gt.SkipReason = $"No '{category}' element with geometry in this scene to anchor the prompt";
                    return false;
                }

                if (token == "ANCHOR") gt.AnchorId = candidate.ElementId; else gt.Anchor2Id = candidate.ElementId;
                prompt = prompt.Substring(0, start) + candidate.ElementId + prompt.Substring(end + 1);
            }
            gt.ResolvedPrompt = prompt;
            return true;
        }

        private BimElementRecord ById(string id)
        {
            return _index.Elements.FirstOrDefault(e => string.Equals(e.ElementId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static string GroupKeyOf(BimElementRecord e, string groupBy)
        {
            switch ((groupBy ?? "").ToLowerInvariant())
            {
                case "level": return string.IsNullOrEmpty(e.Level) ? "Unknown" : e.Level;
                case "moduleid":
                case "module": return string.IsNullOrEmpty(e.ModuleId) ? "Unknown" : e.ModuleId;
                case "category": return e.Category;
                case "discipline": return e.Discipline;
                default:
                    var v = e.Get(groupBy);
                    return string.IsNullOrEmpty(v) ? "Unknown" : v;
            }
        }

        private static bool Compare(double value, string comparator, double threshold)
        {
            switch (comparator)
            {
                case "<": return value < threshold;
                case "<=": return value <= threshold;
                case ">": return value > threshold;
                case ">=": return value >= threshold;
                default: return Math.Abs(value - threshold) < 1e-9;
            }
        }

        private void MarkUnanswerable(GroundTruth gt, string reason)
        {
            gt.Answerable = false;
            gt.ExpectAbstention = true;
            gt.UnanswerableReason = reason;
            gt.Summary = "Expected behaviour: refuse / state that " + reason;
        }

        private void AddTriples(GroundTruth gt, IEnumerable<BimElementRecord> elements, string key)
        {
            foreach (var e in elements)
            {
                var v = e.Get(key);
                if (v == null) continue;
                gt.Triples.Add(new BimTriple { ElementId = e.ElementId, Property = key, Value = v });
                gt.Entities.Add(v);
            }
        }

        // ----------------------------------------------------------- evaluators

        private void ListCategoryAttributes(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            gt.ScoreIds = true; gt.ScoreEntities = true; gt.ScoreNumeric = true;
            gt.Numeric = elements.Count;

            if (elements.Count == 0)
            {
                gt.ZeroIsCorrect = true;
                gt.Summary = $"No {string.Join("/", item.Categories)} elements exist in this model (count = 0).";
                return;
            }

            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            foreach (var e in elements)
            {
                gt.ElementIds.Add(e.ElementId);
                gt.Entities.Add(e.UnityName);
                if (!string.IsNullOrEmpty(e.ElementType)) gt.Entities.Add(e.ElementType);
            }
            if (key != null) AddTriples(gt, elements, key);

            gt.Summary = key == null
                ? $"{elements.Count} elements; requested property not present in metadata (must be reported as unavailable)."
                : $"{elements.Count} elements; {gt.Triples.Count} of them carry '{key}'.";
        }

        private void CountCategory(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            gt.ScoreNumeric = true; gt.ScoreIds = elements.Count > 0 && elements.Count <= 40;
            gt.Numeric = elements.Count;
            gt.ZeroIsCorrect = elements.Count == 0;
            foreach (var e in elements) gt.ElementIds.Add(e.ElementId);
            gt.Summary = $"Count of {string.Join("/", item.Categories)} = {elements.Count}.";
        }

        private void CountAndSumNumeric(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            gt.ScoreNumeric = true; gt.ScoreIds = elements.Count > 0;
            foreach (var e in elements) gt.ElementIds.Add(e.ElementId);

            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            if (elements.Count == 0)
            {
                gt.Numeric = 0; gt.ZeroIsCorrect = true;
                gt.Summary = $"No {string.Join("/", item.Categories)} elements (count = 0, sum undefined).";
                return;
            }
            if (key == null || _index.NonEmptyCount(key) == 0)
            {
                gt.Numeric = elements.Count;
                gt.Summary = $"Count = {elements.Count}; the summed property is not populated, so the total must be reported as unavailable.";
                gt.ExpectAbstention = false;
                return;
            }

            double sum = 0; int n = 0;
            foreach (var e in elements)
            {
                if (BimSceneIndex.TryGetNumeric(e.Get(key), out var v)) { sum += v; n++; }
            }
            gt.Numeric = elements.Count;
            gt.Groups["count"] = elements.Count;
            gt.Groups["sum"] = sum;
            gt.ScoreGroups = true;
            gt.Summary = $"Count = {elements.Count}; sum of '{key}' over {n} populated elements = {sum:F3}.";
        }

        private void SplitByPropertyValue(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            gt.ScoreGroups = true; gt.ScoreNumeric = true;
            gt.Numeric = elements.Count;

            if (elements.Count == 0) { gt.ZeroIsCorrect = true; gt.Summary = "No walls in model."; return; }
            if (key == null || _index.NonEmptyCount(key) == 0)
            {
                MarkUnanswerable(gt, $"no exterior/interior classification metadata exists for {string.Join("/", item.Categories)}");
                return;
            }

            int match = elements.Count(e => (e.Get(key) ?? "").IndexOf(item.MatchValue, StringComparison.OrdinalIgnoreCase) >= 0);
            gt.Groups["exterior"] = match;
            gt.Groups["interior"] = elements.Count - match;
            gt.Summary = $"Using '{key}': exterior = {match}, interior = {elements.Count - match}.";
        }

        private void SumNumericGroupBy(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            gt.ScoreGroups = true;

            if (elements.Count == 0) { gt.ZeroIsCorrect = true; gt.Numeric = 0; gt.ScoreNumeric = true; gt.Summary = "No matching elements."; return; }
            if (key == null || elements.All(e => !e.HasValue(key)))
            {
                MarkUnanswerable(gt, $"the area/quantity property is not populated for {string.Join("/", item.Categories)}");
                return;
            }

            foreach (var e in elements)
            {
                if (!BimSceneIndex.TryGetNumeric(e.Get(key), out var v)) continue;
                var g = GroupKeyOf(e, item.GroupByProperty);
                gt.Groups[g] = gt.Groups.TryGetValue(g, out var cur) ? cur + v : v;
                gt.ElementIds.Add(e.ElementId);
            }
            gt.Summary = "Sum of '" + key + "' by " + item.GroupByProperty + ": " +
                         string.Join(", ", gt.Groups.Select(kv => $"{kv.Key}={kv.Value:F3}"));
        }

        private void DistinctPropertyValues(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            gt.ScoreEntities = true;

            if (elements.Count == 0) { gt.ZeroIsCorrect = true; gt.Summary = "No elements of that category."; return; }
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            if (key == null || elements.All(e => !e.HasValue(key)))
            {
                MarkUnanswerable(gt, $"the requested property is not populated for {string.Join("/", item.Categories)}");
                return;
            }
            foreach (var e in elements)
            {
                var v = e.Get(key);
                if (v != null) { gt.Entities.Add(v); gt.ElementIds.Add(e.ElementId); }
            }
            gt.Summary = $"Distinct '{key}' values: {string.Join(", ", gt.Entities)}";
        }

        private void CountAndMode(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            gt.ScoreNumeric = true; gt.ScoreEntities = true;
            gt.Numeric = elements.Count;
            if (elements.Count == 0) { gt.ZeroIsCorrect = true; gt.Summary = "Count = 0."; return; }

            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            if (key != null)
            {
                var mode = elements.Where(e => e.HasValue(key))
                    .GroupBy(e => e.Get(key))
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                if (mode != null) gt.Entities.Add(mode.Key);
                gt.Summary = $"Count = {elements.Count}; most frequent '{key}' = {mode?.Key ?? "n/a"}.";
            }
            else gt.Summary = $"Count = {elements.Count}; type property unavailable.";
        }

        private void MissingProperty(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            gt.ScoreIds = true; gt.ScoreNumeric = true;

            if (elements.Count == 0)
            {
                gt.Numeric = 0; gt.ZeroIsCorrect = true;
                gt.Summary = $"No {string.Join("/", item.Categories)} elements exist, so nothing can be missing.";
                return;
            }

            var keys = new List<string>();
            var primary = _index.ResolvePropertyKey(item.PropertyCandidates);
            if (primary != null) keys.Add(primary);
            if (item.SecondaryPropertyCandidates != null && item.SecondaryPropertyCandidates.Length > 0)
            {
                var secondary = _index.ResolvePropertyKey(item.SecondaryPropertyCandidates);
                if (secondary != null) keys.Add(secondary);
            }

            if (keys.Count == 0)
            {
                MarkUnanswerable(gt, "the requested property field does not exist in this model's metadata");
                return;
            }

            var missing = elements.Where(e => keys.Any(k => !e.HasValue(k))).ToList();
            foreach (var e in missing) gt.ElementIds.Add(e.ElementId);
            gt.Numeric = missing.Count;
            gt.ZeroIsCorrect = missing.Count == 0;
            gt.Summary = $"{missing.Count}/{elements.Count} elements lack {string.Join(" or ", keys)}.";
        }

        private void FilterNumericThreshold(BenchmarkItem item, GroundTruth gt)
        {
            var elements = item.Categories != null && item.Categories.Length > 0
                ? _index.ByCategory(item.Categories)
                : _index.Elements;
            gt.ScoreIds = true; gt.ScoreNumeric = true;

            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            if (elements.Count == 0)
            {
                gt.Numeric = 0; gt.ZeroIsCorrect = true;
                gt.Summary = "No candidate elements in this model.";
                return;
            }
            if (key == null || elements.All(e => !e.HasValue(key)))
            {
                MarkUnanswerable(gt, "the property required for the threshold check has no recorded values");
                return;
            }

            var hits = new List<BimElementRecord>();
            foreach (var e in elements)
            {
                if (!BimSceneIndex.TryGetNumeric(e.Get(key), out var v)) continue;
                if (Compare(v, item.Comparator, item.Threshold)) hits.Add(e);
            }
            foreach (var e in hits) { gt.ElementIds.Add(e.ElementId); }
            AddTriples(gt, hits, key);
            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.Summary = $"{hits.Count} elements satisfy '{key}' {item.Comparator} {item.Threshold}.";
        }

        private void FilterNumericThresholdGrouped(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            gt.ScoreGroups = true; gt.ScoreNumeric = true;

            if (elements.Count == 0) { gt.Numeric = 0; gt.ZeroIsCorrect = true; gt.Summary = "No elements of that category."; return; }
            if (key == null || elements.All(e => !e.HasValue(key)))
            {
                MarkUnanswerable(gt, "the fire-rating property has no recorded values, so the per-level check cannot be computed");
                return;
            }

            int total = 0;
            foreach (var e in elements)
            {
                if (!BimSceneIndex.TryGetNumeric(e.Get(key), out var v)) continue;
                if (!Compare(v, item.Comparator, item.Threshold)) continue;
                var g = GroupKeyOf(e, item.GroupByProperty);
                gt.Groups[g] = gt.Groups.TryGetValue(g, out var cur) ? cur + 1 : 1;
                gt.ElementIds.Add(e.ElementId);
                total++;
            }
            gt.Numeric = total;
            gt.ZeroIsCorrect = total == 0;
            gt.Summary = $"{total} elements below threshold, grouped by {item.GroupByProperty}.";
        }

        private void FilterByPropertyValue(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            gt.ScoreIds = true; gt.ScoreNumeric = true;

            if (elements.Count == 0) { MarkUnanswerable(gt, "no room/zone elements exist in this model"); return; }
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            if (key == null || elements.All(e => !e.HasValue(key)))
            {
                MarkUnanswerable(gt, "the finish property is not recorded for rooms/zones");
                return;
            }

            var hits = elements.Where(e => (e.Get(key) ?? "").IndexOf(item.MatchValue, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            foreach (var e in hits) gt.ElementIds.Add(e.ElementId);

            var areaKey = _index.ResolvePropertyKey(item.SecondaryPropertyCandidates);
            if (areaKey != null) AddTriples(gt, hits, areaKey);

            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.Summary = $"{hits.Count} rooms with '{key}' containing '{item.MatchValue}'.";
        }

        private void GroupCountByProperty(BenchmarkItem item, GroundTruth gt)
        {
            var elements = item.Categories != null && item.Categories.Length > 0
                ? _index.ByCategory(item.Categories) : _index.Elements;
            gt.ScoreGroups = true; gt.ScoreNumeric = true;

            if (!string.IsNullOrEmpty(item.MatchValue))
                elements = elements.Where(e => GroupKeyOf(e, item.GroupByProperty)
                    .IndexOf(item.MatchValue, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            foreach (var e in elements)
            {
                var g = GroupKeyOf(e, item.GroupByProperty);
                gt.Groups[g] = gt.Groups.TryGetValue(g, out var cur) ? cur + 1 : 1;
            }
            gt.Numeric = elements.Count;
            gt.ZeroIsCorrect = elements.Count == 0;

            bool allUnknown = gt.Groups.Count == 1 && gt.Groups.ContainsKey("Unknown");
            if (allUnknown) MarkUnanswerable(gt, $"no {item.GroupByProperty} metadata is recorded for these elements");
            else gt.Summary = $"Counts by {item.GroupByProperty}: " + string.Join(", ", gt.Groups.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private void CategoryBreakdown(BenchmarkItem item, GroundTruth gt)
        {
            gt.ScoreGroups = true; gt.ScoreNumeric = true; gt.ScoreEntities = true;
            foreach (var g in _index.Elements.GroupBy(e => e.Category))
            {
                gt.Groups[g.Key] = g.Count();
                gt.Entities.Add(g.Key);
            }
            gt.Numeric = _index.Elements.Count;
            gt.Summary = $"Total {_index.Elements.Count} elements: " + string.Join(", ", gt.Groups.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private void DisciplineSummary(BenchmarkItem item, GroundTruth gt)
        {
            gt.ScoreGroups = true; gt.ScoreEntities = true;
            foreach (var g in _index.Elements.GroupBy(e => e.Discipline))
            {
                gt.Groups[g.Key] = g.Count();
                foreach (var c in g.GroupBy(e => e.Category).OrderByDescending(x => x.Count()).Take(3))
                    gt.Entities.Add(c.Key);
            }
            gt.Summary = "Discipline totals: " + string.Join(", ", gt.Groups.Select(kv => $"{kv.Key}={kv.Value}")) +
                         "; top categories: " + string.Join(", ", gt.Entities);
        }

        private void SystemGrouping(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            gt.ScoreGroups = true; gt.ScoreNumeric = true;

            if (key == null || elements.Count == 0 || elements.All(e => !e.HasValue(key)))
            {
                MarkUnanswerable(gt, "no MEP system-name metadata exists in this model, so system membership cannot be established");
                return;
            }

            var valueKey = _index.ResolvePropertyKey(item.SecondaryPropertyCandidates);
            foreach (var e in elements)
            {
                var system = e.Get(key);
                if (system == null) continue;
                if (!string.IsNullOrEmpty(item.MatchValue) &&
                    system.IndexOf(item.MatchValue, StringComparison.OrdinalIgnoreCase) < 0) continue;

                gt.Groups[system] = gt.Groups.TryGetValue(system, out var cur) ? cur + 1 : 1;
                gt.ElementIds.Add(e.ElementId);
                if (valueKey != null && BimSceneIndex.TryGetNumeric(e.Get(valueKey), out var v))
                    gt.Groups[system + " (sum)"] = gt.Groups.TryGetValue(system + " (sum)", out var s) ? s + v : v;
            }
            gt.Numeric = gt.ElementIds.Count;
            gt.ZeroIsCorrect = gt.ElementIds.Count == 0;
            gt.Summary = "Systems: " + string.Join(", ", gt.Groups.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private void QcStatusAggregation(BenchmarkItem item, GroundTruth gt)
        {
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            gt.ScoreGroups = true; gt.ScoreNumeric = true; gt.ScoreIds = true;

            if (key == null || _index.NonEmptyCount(key) == 0)
            {
                MarkUnanswerable(gt, "no inspection/QC status metadata is recorded in this model");
                return;
            }

            var flagged = _index.Elements.Where(e =>
            {
                var v = e.Get(key);
                if (v == null) return false;
                var lower = v.ToLowerInvariant();
                return lower.Contains("incorrect") || lower.Contains("missing") || lower.Contains("fail");
            }).ToList();

            foreach (var e in flagged)
            {
                gt.ElementIds.Add(e.ElementId);
                gt.Groups[e.Category] = gt.Groups.TryGetValue(e.Category, out var c) ? c + 1 : 1;
            }
            gt.Numeric = flagged.Count;
            gt.ZeroIsCorrect = flagged.Count == 0;
            gt.Summary = $"{flagged.Count} elements flagged via '{key}'.";
        }

        private void PropertyCompleteness(BenchmarkItem item, GroundTruth gt)
        {
            gt.ScoreEntities = true; gt.ScoreNumeric = true;

            // Scored over exactly the subset that the context packet serializes (see
            // ContextPacketBuilder: first 25 property keys, bounded row set).
            var subset = _index.Elements.OrderBy(e => e.ElementId, StringComparer.Ordinal).Take(60).ToList();
            if (subset.Count == 0) { MarkUnanswerable(gt, "the model contains no elements"); return; }
            gt.ScopeElements = subset;

            var keys = _index.PropertyKeys.OrderBy(k => k, StringComparer.Ordinal).Take(25).ToList();
            foreach (var key in keys)
            {
                double ratio = subset.Count(e => e.HasValue(key)) / (double)subset.Count;
                if (ratio < item.Threshold) gt.Entities.Add(key);
            }
            gt.Numeric = gt.Entities.Count;
            gt.Summary = $"{gt.Entities.Count} of {keys.Count} serialized property keys are populated for <{item.Threshold:P0} of the {subset.Count} provided elements.";
        }

        private void DefinedValueSemantics(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            gt.ScoreGroups = true; gt.ScoreNumeric = true;

            if (elements.Count == 0) { gt.ZeroIsCorrect = true; gt.Numeric = 0; gt.Summary = "No walls."; return; }
            if (key == null)
            {
                MarkUnanswerable(gt, "the material property field does not exist in this model");
                return;
            }
            int defined = elements.Count(e => e.HasValue(key));
            gt.Groups["defined"] = defined;
            gt.Groups["undefined"] = elements.Count - defined;
            gt.Numeric = defined;
            gt.Summary = $"'{key}': defined = {defined}, undefined = {elements.Count - defined} (of {elements.Count} walls).";
        }

        private void DuplicateIdentifiers(BenchmarkItem item, GroundTruth gt)
        {
            gt.ScoreNumeric = true;
            var dupes = _index.Elements.GroupBy(e => e.ElementId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).ToList();
            foreach (var g in dupes) gt.ElementIds.Add(g.Key);
            gt.Numeric = dupes.Count;
            gt.ZeroIsCorrect = dupes.Count == 0;
            gt.ScoreIds = dupes.Count > 0;

            var genericShare = _index.Elements.Count == 0 ? 0 :
                _index.Elements.Count(e => e.Category == "Generic") / (double)_index.Elements.Count;
            gt.Summary = dupes.Count == 0
                ? $"No duplicate element IDs. Category field is 'Generic' for {genericShare:P0} of elements, which must be reported as a labelling inconsistency rather than invented duplicates."
                : $"{dupes.Count} duplicated element IDs.";
        }

        private void MemoryRecall(BenchmarkItem item, GroundTruth gt)
        {
            gt.ScoreNumeric = true;
            if (item.Categories != null && item.Categories.Length > 0)
            {
                // H22: recall the count reported for a specific category
                gt.Numeric = _index.ByCategory(item.Categories).Count;
                gt.Groups["half"] = gt.Numeric.Value / 2.0;
                gt.ScoreGroups = true;
                gt.Summary = $"Recalled count should be {gt.Numeric} (half = {gt.Numeric / 2.0}).";
            }
            else
            {
                // Q29: largest category from the earlier breakdown
                var top = _index.Elements.GroupBy(e => e.Category).OrderByDescending(g => g.Count()).FirstOrDefault();
                if (top == null) { MarkUnanswerable(gt, "the model contains no elements"); return; }
                gt.Numeric = top.Count();
                gt.Entities.Add(top.Key);
                gt.ScoreEntities = true;
                gt.Summary = $"Largest category '{top.Key}' with {top.Count()} elements.";
            }
        }

        private void OutOfScope(GroundTruth gt)
        {
            gt.Answerable = false;
            gt.ExpectAbstention = true;
            gt.UnanswerableReason = "the question is outside the BIM scope and no scene data supports an answer";
            gt.Summary = "Expected behaviour: refuse, no element IDs, no fabricated values.";
        }

        private void UnsupportedRelation(BenchmarkItem item, GroundTruth gt)
        {
            // Answerable only if an explicit relation/system property carries the requested value.
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            bool hasEvidence = key != null && _index.NonEmptyCount(key) > 0;

            if (hasEvidence && !string.IsNullOrEmpty(item.MatchValue))
                hasEvidence = _index.Elements.Any(e => (e.Get(key) ?? "")
                    .IndexOf(item.MatchValue, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!hasEvidence)
            {
                MarkUnanswerable(gt, "no relationship/system metadata in the model supports this query (clash or connection facts are not exported)");
                return;
            }

            gt.ScoreIds = true;
            foreach (var e in _index.Elements.Where(e => (e.Get(key) ?? "")
                         .IndexOf(item.MatchValue ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
                gt.ElementIds.Add(e.ElementId);
            gt.Summary = $"{gt.ElementIds.Count} elements reference '{item.MatchValue}' via '{key}'.";
        }

        private void UnsupportedCompliance(BenchmarkItem item, GroundTruth gt)
        {
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            bool hasRatings = key != null && _index.ByCategory(item.Categories).Any(e => e.HasValue(key));
            // A compliance verdict needs both the property values and a code threshold; the prompt supplies neither.
            if (!hasRatings)
            {
                MarkUnanswerable(gt, "fire-rating values are not populated and no code threshold table is provided, so a compliance verdict is not derivable");
                return;
            }
            MarkUnanswerable(gt, "no code threshold table is provided, so a pass/fail compliance verdict is not derivable even though ratings exist");
        }

        private void MetadataOrientation(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            var key = _index.ResolvePropertyKey(item.PropertyCandidates);
            gt.ScoreIds = true; gt.ScoreNumeric = true;

            if (elements.Count == 0) { gt.Numeric = 0; gt.ZeroIsCorrect = true; gt.Summary = "No windows in model."; return; }
            if (key == null || elements.All(e => !e.HasValue(key)))
            {
                MarkUnanswerable(gt, "orientation metadata is not populated for these elements");
                return;
            }

            var values = elements.Where(e => e.HasValue(key)).Select(e => e.Get(key)).Distinct().ToList();
            bool cardinal = values.Any(v => v.IndexOf("north", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            v.IndexOf("south", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            v.IndexOf("east", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            v.IndexOf("west", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!cardinal)
            {
                MarkUnanswerable(gt, $"the orientation field uses non-cardinal values ({string.Join("/", values.Take(4))}), so a north-facing classification is not derivable");
                return;
            }

            var hits = elements.Where(e => (e.Get(key) ?? "").IndexOf("north", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            foreach (var e in hits) gt.ElementIds.Add(e.ElementId);
            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.Summary = $"{hits.Count} north-facing per '{key}'.";
        }

        // ------------------------------------------------------- spatial truths

        private void SpatialAdjacency(BenchmarkItem item, GroundTruth gt)
        {
            var anchor = ById(gt.AnchorId);
            if (anchor == null) { gt.Skip = true; gt.SkipReason = "anchor not resolvable"; return; }

            gt.ScoreIds = true; gt.ScoreNumeric = true;
            var hits = _index.Elements.Where(e => _index.IsAdjacent(anchor, e, item.SpatialTolerance)).ToList();
            foreach (var e in hits) gt.ElementIds.Add(e.ElementId);
            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.ScopeElements = BuildNeighbourhood(anchor, 3.0f);
            gt.Summary = $"{hits.Count} elements within {item.SpatialTolerance} m of {anchor.ElementId}.";
        }

        private void SpatialAbove(BenchmarkItem item, GroundTruth gt)
        {
            var anchor = ById(gt.AnchorId);
            if (anchor == null) { gt.Skip = true; gt.SkipReason = "anchor not resolvable"; return; }

            gt.ScoreIds = true; gt.ScoreNumeric = true;
            var hits = _index.Elements.Where(e => _index.IsAbove(e, anchor, item.SpatialTolerance)).ToList();
            foreach (var e in hits) gt.ElementIds.Add(e.ElementId);
            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.ScopeElements = BuildNeighbourhood(anchor, 6.0f);
            gt.Summary = $"{hits.Count} elements above {anchor.ElementId}.";
        }

        private void SpatialWithinDistance(BenchmarkItem item, GroundTruth gt)
        {
            var anchor = ById(gt.AnchorId);
            if (anchor == null) { gt.Skip = true; gt.SkipReason = "anchor not resolvable"; return; }

            gt.ScoreIds = true; gt.ScoreNumeric = true;
            var hits = _index.Elements
                .Where(e => e != anchor && e.HasBounds && BimSceneIndex.Gap(anchor, e) <= item.Threshold).ToList();
            foreach (var e in hits) gt.ElementIds.Add(e.ElementId);
            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.ScopeElements = BuildNeighbourhood(anchor, (float)item.Threshold * 4f);
            gt.Summary = $"{hits.Count} elements within {item.Threshold} m of {anchor.ElementId}.";
        }

        private void SpatialClash(BenchmarkItem item, GroundTruth gt)
        {
            var left = _index.ByCategory(item.Categories);
            var right = _index.ByCategory(item.SecondaryCategories);
            gt.ScoreNumeric = true; gt.ScoreIds = true;

            if (left.Count == 0 || right.Count == 0)
            {
                gt.Numeric = 0; gt.ZeroIsCorrect = true;
                gt.Summary = "One of the two categories is absent, so no overlaps exist; the correct answer states this.";
                gt.ScopeElements = left.Concat(right).ToList();
                return;
            }

            var pairs = _index.IntersectingPairs(left, right, item.SpatialTolerance);
            foreach (var (a, b) in pairs) { gt.ElementIds.Add(a.ElementId); gt.ElementIds.Add(b.ElementId); }
            gt.Numeric = pairs.Count;
            gt.ZeroIsCorrect = pairs.Count == 0;
            gt.ScopeElements = left.Concat(right).ToList();
            gt.Summary = $"{pairs.Count} overlapping pairs.";
        }

        private void SpatialOrientationGeometry(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories).Where(e => e.HasBounds).ToList();
            gt.ScoreIds = true; gt.ScoreNumeric = true;

            if (elements.Count == 0)
            {
                gt.Numeric = 0; gt.ZeroIsCorrect = true;
                gt.Summary = "No windows with geometry.";
                return;
            }
            var hits = elements.Where(e => string.Equals(_index.CardinalFacing(e), item.MatchValue, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var e in hits) gt.ElementIds.Add(e.ElementId);
            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.ScopeElements = elements;
            gt.Summary = $"{hits.Count} of {elements.Count} windows face {item.MatchValue} (project north = scene +Z).";
        }

        private void SpatialContainment(BenchmarkItem item, GroundTruth gt)
        {
            var container = ById(gt.AnchorId);
            if (container == null) { gt.Skip = true; gt.SkipReason = "no zone anchor"; return; }

            gt.ScoreIds = true; gt.ScoreNumeric = true;
            var hits = _index.Elements.Where(e => _index.Contains(container, e)).ToList();
            foreach (var e in hits) gt.ElementIds.Add(e.ElementId);
            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.ScopeElements = BuildNeighbourhood(container, 1.0f);
            gt.Summary = $"{hits.Count} elements inside {container.ElementId}.";
        }

        private void SpatialRelativePosition(BenchmarkItem item, GroundTruth gt)
        {
            var a = ById(gt.AnchorId); var b = ById(gt.Anchor2Id);
            if (a == null || b == null) { gt.Skip = true; gt.SkipReason = "anchors not resolvable"; return; }

            gt.ScoreEntities = true; gt.ScoreIds = true;
            gt.ElementIds.Add(a.ElementId); gt.ElementIds.Add(b.ElementId);
            string relation = a.Bounds.center.y >= b.Bounds.center.y ? "above" : "below";
            gt.Entities.Add(relation);
            gt.ScopeElements = new List<BimElementRecord> { a, b };
            gt.Summary = $"{a.ElementId} is {relation} {b.ElementId} (centroid Y {a.Bounds.center.y:F2} vs {b.Bounds.center.y:F2}).";
        }

        private void SpatialClosestPair(BenchmarkItem item, GroundTruth gt)
        {
            // Bounded subset keeps the packet small and the pairwise scan cheap.
            var subset = _index.Elements.Where(e => e.HasBounds)
                .OrderBy(e => e.ElementId, StringComparer.Ordinal).Take(25).ToList();
            if (subset.Count < 2) { gt.Skip = true; gt.SkipReason = "fewer than two elements with geometry"; return; }

            gt.ScoreIds = true; gt.ScoreNumeric = true;
            float best = float.MaxValue; BimElementRecord ba = null, bb = null;
            for (int i = 0; i < subset.Count; i++)
                for (int j = i + 1; j < subset.Count; j++)
                {
                    float d = BimSceneIndex.Gap(subset[i], subset[j]);
                    if (d < best) { best = d; ba = subset[i]; bb = subset[j]; }
                }

            gt.ElementIds.Add(ba.ElementId); gt.ElementIds.Add(bb.ElementId);
            gt.Numeric = Math.Round(best, 3);
            gt.ScopeElements = subset;
            gt.Summary = $"Closest pair {ba.ElementId} / {bb.ElementId} at {best:F3} m.";
        }

        private List<BimElementRecord> BuildNeighbourhood(BimElementRecord anchor, float radius)
        {
            var list = _index.Elements
                .Where(e => e.HasBounds && (e == anchor || BimSceneIndex.Gap(anchor, e) <= radius))
                .OrderBy(e => BimSceneIndex.Gap(anchor, e))
                .Take(60).ToList();
            if (!list.Contains(anchor)) list.Insert(0, anchor);
            return list;
        }

        // -------------------------------------------------- cross-discipline

        private void DisciplineByLevel(BenchmarkItem item, GroundTruth gt)
        {
            gt.ScoreGroups = true;
            foreach (var e in _index.Elements)
            {
                var key = $"{(string.IsNullOrEmpty(e.Level) ? "Unknown" : e.Level)}|{e.Discipline}";
                gt.Groups[key] = gt.Groups.TryGetValue(key, out var c) ? c + 1 : 1;
            }
            if (gt.Groups.Keys.All(k => k.EndsWith("|Unknown", StringComparison.OrdinalIgnoreCase)))
            {
                MarkUnanswerable(gt, "no discipline metadata is recorded, so a per-level discipline breakdown is not derivable");
                return;
            }
            gt.Summary = string.Join(", ", gt.Groups.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private void UnassignedLevel(BenchmarkItem item, GroundTruth gt)
        {
            var elements = _index.ByCategory(item.Categories);
            gt.ScoreIds = true; gt.ScoreNumeric = true;

            if (elements.Count == 0)
            {
                gt.Numeric = 0; gt.ZeroIsCorrect = true;
                gt.Summary = "No MEP elements exist in this model; the correct answer states this.";
                return;
            }
            var hits = elements.Where(e => string.IsNullOrEmpty(e.Level) ||
                                           e.Level.Equals("Unknown", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var e in hits) gt.ElementIds.Add(e.ElementId);
            gt.Numeric = hits.Count;
            gt.ZeroIsCorrect = hits.Count == 0;
            gt.Summary = $"{hits.Count} MEP elements without a level assignment.";
        }

        private void CategoryRatio(BenchmarkItem item, GroundTruth gt)
        {
            gt.ScoreGroups = true; gt.ScoreNumeric = true;
            int numerator = _index.ByCategory(item.Categories).Count;
            int denominator = _index.ByCategory(item.SecondaryCategories).Count;
            gt.Groups["openings"] = numerator;
            gt.Groups["walls"] = denominator;
            gt.Numeric = denominator == 0 ? 0 : Math.Round(numerator / (double)denominator, 3);
            gt.ZeroIsCorrect = numerator == 0;
            gt.Summary = denominator == 0
                ? $"{numerator} openings but no walls: the ratio is undefined and must be reported as such."
                : $"{numerator} openings / {denominator} walls = {gt.Numeric:F3}.";
        }
    }
}
