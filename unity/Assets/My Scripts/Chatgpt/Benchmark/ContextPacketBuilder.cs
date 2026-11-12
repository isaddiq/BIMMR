// ContextPacketBuilder.cs - Deterministic serialization of the BIM scene into the prompt.
//
// Design notes that matter for validity:
//  * The packet never contains pre-computed aggregates (no per-category counts). Aggregation
//    questions must be answered by reasoning over the serialized rows, otherwise the benchmark
//    measures the serializer rather than the model.
//  * Only the columns a question needs are serialized. This keeps whole-scene packets affordable
//    and matches the "relevant subset" context packet described in the methods section.
//  * Geometry columns are emitted only for spatial items, and always in metres.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AEC.Benchmark
{
    public class ContextPacket
    {
        public string Text;
        public int RowCount;
        public bool Truncated;
        public List<string> Columns = new List<string>();
    }

    public class ContextPacketBuilder
    {
        private readonly BimSceneIndex _index;
        private readonly int _maxRows;

        public ContextPacketBuilder(BimSceneIndex index, int maxRows = 500)
        {
            _index = index;
            _maxRows = maxRows;
        }

        public ContextPacket Build(BenchmarkItem item, GroundTruth gt, PerturbationSettings perturbation)
        {
            var records = SelectRecords(item, gt);
            var columns = RequiredColumns(item);
            var packet = new ContextPacket { Columns = columns, RowCount = records.Count };

            var sb = new StringBuilder();
            sb.AppendLine("BIM SCENE CONTEXT (serialized from the live Unity scene)");
            sb.AppendLine($"scene: {_index.Profile.SceneName} | scene_hash: {_index.Profile.SceneHash}");
            sb.AppendLine($"elements_in_model: {_index.Profile.TotalElements}");
            sb.AppendLine("units: lengths in metres, areas in square metres, power in watts unless a unit suffix is shown");
            sb.AppendLine("project_north: scene +Z axis");
            sb.AppendLine();

            if (records.Count == 0)
            {
                sb.AppendLine("ELEMENTS: (no elements of the requested kind are present in this model)");
                packet.Text = sb.ToString();
                return packet;
            }

            if (records.Count > _maxRows)
            {
                records = records.Take(_maxRows).ToList();
                packet.Truncated = true;
                packet.RowCount = records.Count;
            }

            // Rows first: perturbation may rename column headers, which must stay consistent with the data.
            var perturbed = perturbation != null && perturbation.Enabled
                ? PerturbationEngine.Apply(records, columns, perturbation, item.Id)
                : records.Select(r => PerturbationEngine.ToRow(r, columns)).ToList();

            sb.AppendLine($"ELEMENTS ({records.Count} rows, pipe-separated, header first):");
            sb.AppendLine(string.Join(" | ", columns));
            foreach (var row in perturbed) sb.AppendLine(row);

            if (packet.Truncated)
                sb.AppendLine($"[context truncated to {_maxRows} rows]");

            packet.Text = sb.ToString();
            return packet;
        }

        private List<BimElementRecord> SelectRecords(BenchmarkItem item, GroundTruth gt)
        {
            if (item.ContextCategories != null && item.ContextCategories.Length == 0)
                return new List<BimElementRecord>();                      // deliberately context-free (OOD, recall)

            if (gt != null && gt.ScopeElements != null && gt.ScopeElements.Count > 0 &&
                gt.ScopeElements.Count < _index.Elements.Count)
                return gt.ScopeElements;

            if (item.ContextCategories != null && item.ContextCategories.Length > 0)
                return _index.ByCategory(item.ContextCategories);

            return _index.Elements;
        }

        /// <summary>Column set per question kind: the minimum needed to answer, nothing pre-aggregated.</summary>
        private List<string> RequiredColumns(BenchmarkItem item)
        {
            var cols = new List<string> { "element_id", "category" };

            switch (item.Kind)
            {
                case QueryKind.CategoryBreakdown:
                    break;
                case QueryKind.DisciplineSummary:
                    cols.Add("discipline");
                    break;
                case QueryKind.DisciplineByLevel:
                    cols.Add("discipline"); cols.Add("level");
                    break;
                case QueryKind.UnassignedLevel:
                    cols.Add("level");
                    break;
                case QueryKind.GroupCountByProperty:
                    cols.Add(NormalizeColumn(item.GroupByProperty));
                    break;
                default:
                    cols.Add("name");
                    cols.Add("type");
                    break;
            }

            if (item.Kind == QueryKind.SumNumericGroupBy || item.Kind == QueryKind.FilterNumericThresholdGrouped)
                cols.Add(NormalizeColumn(item.GroupByProperty));

            foreach (var candidate in EnumerateRequestedKeys(item))
            {
                var resolved = _index.ResolvePropertyKey(candidate);
                if (resolved != null && !cols.Contains(resolved)) cols.Add(resolved);
            }

            if (item.Kind == QueryKind.PropertyCompleteness)
            {
                // Completeness reasoning needs the raw property matrix for the provided subset.
                foreach (var key in _index.PropertyKeys.OrderBy(k => k, StringComparer.Ordinal).Take(25))
                    if (!cols.Contains(key)) cols.Add(key);
            }

            if (item.Kind == QueryKind.QcStatusAggregation || item.Kind == QueryKind.DuplicateIdentifiers)
                if (!cols.Contains("level")) cols.Add("level");

            if (item.IncludeGeometryInContext)
            {
                cols.Add("centroid_xyz");
                cols.Add("size_xyz");
            }

            return cols;
        }

        private IEnumerable<string[]> EnumerateRequestedKeys(BenchmarkItem item)
        {
            if (item.PropertyCandidates != null && item.PropertyCandidates.Length > 0)
                yield return item.PropertyCandidates;
            if (item.SecondaryPropertyCandidates != null && item.SecondaryPropertyCandidates.Length > 0)
                yield return item.SecondaryPropertyCandidates;
        }

        private static string NormalizeColumn(string groupBy)
        {
            if (string.IsNullOrEmpty(groupBy)) return "level";
            switch (groupBy.ToLowerInvariant())
            {
                case "level": return "level";
                case "moduleid":
                case "module": return "module";
                case "category": return "category";
                case "discipline": return "discipline";
                default: return groupBy;
            }
        }
    }

    /// <summary>Controlled metadata degradation used for the noisy-data experiment.</summary>
    [Serializable]
    public class PerturbationSettings
    {
        public bool Enabled;
        public float Rate = 0f;              // fraction of rows affected per operator
        public int Seed = 20260730;
        public bool KeyRenaming = true;      // authoring-tool synonym drift
        public bool ValueDropout = true;     // properties become <undefined>
        public bool UnitShift = true;        // metres silently recorded as millimetres
        public bool DuplicateIds = true;     // repeated element identifiers
        public bool CategoryMislabel = true; // wrong category label

        public PerturbationSettings Clone(float rate)
        {
            return new PerturbationSettings
            {
                Enabled = rate > 0f,
                Rate = rate,
                Seed = Seed,
                KeyRenaming = KeyRenaming,
                ValueDropout = ValueDropout,
                UnitShift = UnitShift,
                DuplicateIds = DuplicateIds,
                CategoryMislabel = CategoryMislabel
            };
        }

        public string Describe() => Enabled ? $"rate={Rate:P0};seed={Seed}" : "clean";
    }

    /// <summary>
    /// Applies seeded, reproducible corruption to the serialized rows only. Ground truth is always
    /// computed from the clean index, so the score measures robustness rather than a moved target.
    /// </summary>
    public static class PerturbationEngine
    {
        private static readonly Dictionary<string, string> KeySynonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Fire Rating", "FireResistanceClass" },
            { "Building Material Name", "MatlName" },
            { "Material", "Mat" },
            { "Thickness", "Dicke" },
            { "Height", "Hgt" },
            { "Width", "Br" },
            { "Area", "Flaeche" },
            { "Level", "Storey" },
            { "Power", "kW_Rating" },
            { "Element Type", "TypeCode" },
        };

        public static string ToRow(BimElementRecord r, List<string> columns)
        {
            var values = columns.Select(c => Cell(r, c));
            return string.Join(" | ", values);
        }

        public static List<string> Apply(List<BimElementRecord> records, List<string> columns,
                                         PerturbationSettings settings, string itemId)
        {
            var rng = new System.Random(settings.Seed ^ itemId.GetHashCode());
            var rows = new List<string>(records.Count);

            foreach (var r in records)
            {
                var cells = columns.Select(c => Cell(r, c)).ToList();

                for (int i = 0; i < columns.Count; i++)
                {
                    string col = columns[i];
                    if (col == "element_id") continue;

                    if (settings.ValueDropout && rng.NextDouble() < settings.Rate)
                    {
                        cells[i] = "<undefined>";
                        continue;
                    }
                    if (settings.UnitShift && rng.NextDouble() < settings.Rate &&
                        double.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    {
                        cells[i] = (num * 1000.0).ToString("0.##", CultureInfo.InvariantCulture); // metres silently written as mm
                        continue;
                    }
                    if (settings.CategoryMislabel && col == "category" && rng.NextDouble() < settings.Rate)
                    {
                        cells[i] = "Generic";
                    }
                }

                if (settings.DuplicateIds && rng.NextDouble() < settings.Rate * 0.25 && rows.Count > 0)
                {
                    int idIdx = columns.IndexOf("element_id");
                    if (idIdx >= 0)
                    {
                        var previous = rows[rng.Next(rows.Count)].Split('|')[idIdx].Trim();
                        cells[idIdx] = previous;
                    }
                }

                rows.Add(string.Join(" | ", cells));
            }

            if (settings.KeyRenaming)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    if (KeySynonyms.TryGetValue(columns[i], out var alias) && rng.NextDouble() < settings.Rate)
                        columns[i] = alias;
                }
            }

            return rows;
        }

        private static string Cell(BimElementRecord r, string column)
        {
            switch (column)
            {
                case "element_id": return r.ElementId;
                case "category": return r.Category;
                case "name": return Clean(r.UnityName);
                case "type": return Clean(r.ElementType);
                case "level": return Clean(r.Level);
                case "module": return Clean(r.ModuleId);
                case "discipline": return Clean(r.Discipline);
                case "centroid_xyz":
                    return r.HasBounds
                        ? $"{r.Bounds.center.x:F2},{r.Bounds.center.y:F2},{r.Bounds.center.z:F2}" : "n/a";
                case "size_xyz":
                    return r.HasBounds
                        ? $"{r.Bounds.size.x:F2},{r.Bounds.size.y:F2},{r.Bounds.size.z:F2}" : "n/a";
                default:
                    var v = r.Get(column);
                    return string.IsNullOrEmpty(v) ? "<undefined>" : Clean(v);
            }
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "<undefined>";
            return s.Replace("|", "/").Replace("\n", " ").Trim();
        }
    }
}
