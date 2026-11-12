// BimSceneIndex.cs - Deterministic, scene-independent index of BIM elements for benchmarking.
//
// Everything the benchmark scores against is derived from this index at runtime, so the same
// question suite can be executed against any loaded BIM model (different modules, different
// projects, IFC imports) without editing question-specific ground truth.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AEC.Benchmark
{
    /// <summary>
    /// One BIM element as seen by the benchmark: stable identifier, normalized classification,
    /// raw property bag, and world-space geometry recovered from the Unity scene.
    /// </summary>
    public class BimElementRecord
    {
        public string ElementId;        // BIM identifier (never a Unity instance id)
        public string UnityName;
        public string Category;         // normalized: Wall, Column, Beam, Door, ...
        public string ElementType;      // authoring-tool type / family string
        public string Discipline;       // Structure | Architecture | MEP | Unknown
        public string Level;
        public string ModuleId;
        public Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool HasBounds;
        public Bounds Bounds;
        public Vector3 Centroid => HasBounds ? Bounds.center : Vector3.zero;

        public bool HasValue(string key)
        {
            return Properties.TryGetValue(key, out var v) && !BimSceneIndex.IsUndefined(v);
        }

        public string Get(string key)
        {
            return Properties.TryGetValue(key, out var v) && !BimSceneIndex.IsUndefined(v) ? v : null;
        }

        /// <summary>First non-empty value among candidate keys (exact match, then contains).</summary>
        public string GetAny(params string[] keyCandidates)
        {
            foreach (var k in keyCandidates)
            {
                var v = Get(k);
                if (v != null) return v;
            }
            foreach (var k in keyCandidates)
            {
                var hit = Properties.FirstOrDefault(p =>
                    p.Key.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 && !BimSceneIndex.IsUndefined(p.Value));
                if (!string.IsNullOrEmpty(hit.Key)) return hit.Value;
            }
            return null;
        }
    }

    /// <summary>Summary of what a scene actually contains; written next to every result set.</summary>
    [Serializable]
    public class SceneProfile
    {
        public string SceneName;
        public string CapturedAtUtc;
        public int TotalGameObjects;
        public int TotalElements;
        public int ElementsWithBounds;
        public int DistinctPropertyKeys;
        public int DuplicateElementIds;
        public List<string> CategoryCounts = new List<string>();
        public List<string> DisciplineCounts = new List<string>();
        public List<string> PropertyAvailability = new List<string>(); // "key: nonEmpty/total"
        public string SceneHash;
    }

    /// <summary>
    /// Queryable index over all <see cref="Metadata"/>-bearing objects in the active scene.
    /// Provides the category/discipline normalization, property lookup and spatial predicates
    /// that <see cref="GroundTruthEngine"/> uses to compute expected answers.
    /// </summary>
    public class BimSceneIndex
    {
        public List<BimElementRecord> Elements { get; private set; } = new List<BimElementRecord>();
        public SceneProfile Profile { get; private set; } = new SceneProfile();
        public HashSet<string> PropertyKeys { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ValidElementIds { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>World axis treated as project north when answering orientation questions.</summary>
        public Vector3 NorthAxis = Vector3.forward;

        private static readonly string[] IdKeys =
        {
            "Element ID", "ElementId", "Element Id", "GlobalId", "Global Id", "GUID", "Guid", "IfcGUID", "Id"
        };

        private static readonly string[] LevelKeys =
        {
            "Level", "Base Level", "Home Story", "Storey", "Story", "Floor Level", "Related Level"
        };

        private static readonly string[] ModuleKeys =
        {
            "ModuleID", "Module ID", "Module", "ModuleName", "Module Name", "Unit ID", "Assembly"
        };

        private static readonly string[] TypeKeys =
        {
            "Element Type", "ElementType", "Type", "Type Name", "TypeName", "Family", "FamilyName",
            "Family Name", "IfcClass", "Ifc Class", "Object Type"
        };

        // ---------------------------------------------------------------- build

        public static BimSceneIndex Build(Vector3 northAxis)
        {
            var index = new BimSceneIndex { NorthAxis = northAxis.sqrMagnitude > 0 ? northAxis.normalized : Vector3.forward };
            index.Capture();
            return index;
        }

        private void Capture()
        {
            var metadataComponents = UnityEngine.Object.FindObjectsByType<Metadata>();
            var seenIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int duplicates = 0;

            foreach (var metadata in metadataComponents)
            {
                if (metadata == null || metadata.gameObject == null) continue;
                if (metadata.Properties == null || metadata.Properties.Count == 0) continue;

                var go = metadata.gameObject;
                var record = new BimElementRecord { UnityName = go.name };

                foreach (var p in metadata.Properties)
                {
                    if (p == null || string.IsNullOrEmpty(p.Key)) continue;
                    record.Properties[p.Key.Trim()] = p.Value?.Trim() ?? "";
                    PropertyKeys.Add(p.Key.Trim());
                }

                record.ElementId = ResolveElementId(record, go);
                record.ElementType = record.GetAny(TypeKeys) ?? "";
                record.Category = NormalizeCategory(record, go.name);
                record.Discipline = MapDiscipline(record.Category, record.ElementType, record.Properties);
                record.Level = record.GetAny(LevelKeys) ?? InferLevelFromHeight(go);
                record.ModuleId = record.GetAny(ModuleKeys) ?? InferModuleFromHierarchy(go);

                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers != null && renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    record.Bounds = b;
                    record.HasBounds = b.size.sqrMagnitude > 0f;
                }

                if (seenIds.ContainsKey(record.ElementId))
                {
                    duplicates++;
                    seenIds[record.ElementId]++;
                }
                else
                {
                    seenIds[record.ElementId] = 1;
                }

                ValidElementIds.Add(record.ElementId);
                Elements.Add(record);
            }

            BuildProfile(metadataComponents?.Length ?? 0, duplicates);
        }

        private string ResolveElementId(BimElementRecord record, GameObject go)
        {
            var id = record.GetAny(IdKeys);
            if (!string.IsNullOrEmpty(id)) return id;

            // OBJ pipeline encodes the id in the object name (e.g. Wall_12345)
            var m = Regex.Match(go.name, @"[_\-]([A-Za-z0-9\-]{4,})$");
            if (m.Success) return m.Groups[1].Value;
            return go.name;
        }

        private static string InferLevelFromHeight(GameObject go)
        {
            float y = go.transform.position.y;
            if (y < 3f) return "Level 1";
            if (y < 6f) return "Level 2";
            if (y < 9f) return "Level 3";
            return "Unknown";
        }

        private static string InferModuleFromHierarchy(GameObject go)
        {
            var t = go.transform.parent;
            int guard = 0;
            while (t != null && guard++ < 8)
            {
                if (t.name.IndexOf("module", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.name.IndexOf("unit", StringComparison.OrdinalIgnoreCase) >= 0)
                    return t.name;
                t = t.parent;
            }
            return "Unknown";
        }

        private void BuildProfile(int gameObjectCount, int duplicates)
        {
            Profile = new SceneProfile
            {
                SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                CapturedAtUtc = DateTime.UtcNow.ToString("o"),
                TotalGameObjects = gameObjectCount,
                TotalElements = Elements.Count,
                ElementsWithBounds = Elements.Count(e => e.HasBounds),
                DistinctPropertyKeys = PropertyKeys.Count,
                DuplicateElementIds = duplicates
            };

            Profile.CategoryCounts = Elements.GroupBy(e => e.Category)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}: {g.Count()}").ToList();

            Profile.DisciplineCounts = Elements.GroupBy(e => e.Discipline)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}: {g.Count()}").ToList();

            Profile.PropertyAvailability = PropertyKeys
                .Select(k => new { k, n = Elements.Count(e => e.HasValue(k)) })
                .OrderByDescending(x => x.n)
                .Select(x => $"{x.k}: {x.n}/{Elements.Count}")
                .ToList();

            unchecked
            {
                int hash = 17;
                foreach (var e in Elements.OrderBy(e => e.ElementId, StringComparer.Ordinal))
                {
                    hash = hash * 31 + e.ElementId.GetHashCode();
                    hash = hash * 31 + e.Category.GetHashCode();
                    hash = hash * 31 + e.Properties.Count;
                }
                Profile.SceneHash = hash.ToString("X8");
            }
        }

        // ------------------------------------------------------- classification

        public static bool IsUndefined(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            var v = value.Trim();
            return v.Equals("<undefined>", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("undefined", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                   v == "-";
        }

        /// <summary>Category synonyms; first match wins. Covers Revit, ArchiCAD and IFC vocabularies.</summary>
        private static readonly (string canonical, string[] tokens)[] CategoryRules =
        {
            ("Column",     new[]{ "column", "ifccolumn", "pillar", "기둥" }),
            ("Beam",       new[]{ "beam", "ifcbeam", "girder", "보" }),
            ("Connection", new[]{ "connection", "joint", "ifcfastener", "ifcmechanicalfastener" }),
            ("Stair",      new[]{ "stair", "ifcstair", "step", "계단" }),
            ("Stud",       new[]{ "stud", "framing member", "ifcmember" }),
            ("Curtain Wall", new[]{ "curtain wall", "curtainwall", "ifccurtainwall" }),
            ("Wall",       new[]{ "wall", "ifcwall", "ifcwallstandardcase", "벽" }),
            ("Floor",      new[]{ "floor", "slab", "ifcslab", "바닥" }),
            ("Ceiling",    new[]{ "ceiling", "ifccovering", "천장" }),
            ("Roof",       new[]{ "roof", "ifcroof", "지붕" }),
            ("Door",       new[]{ "door", "ifcdoor", "문" }),
            ("Window",     new[]{ "window", "ifcwindow", "glazing panel", "창" }),
            ("Furniture",  new[]{ "furniture", "ifcfurnishingelement", "furnishing" }),
            ("Railing",    new[]{ "railing", "ifcrailing", "handrail" }),
            ("Duct",       new[]{ "duct", "ifcductsegment", "ifcductfitting", "air terminal" }),
            ("Pipe",       new[]{ "pipe", "ifcpipesegment", "ifcpipefitting", "plumbing" }),
            ("Lighting",   new[]{ "lighting", "light fixture", "luminaire", "ifclightfixture" }),
            ("Electric",   new[]{ "electric", "outlet", "socket", "switchboard", "ifcelectric", "ifcoutlet" }),
            ("HVAC",       new[]{ "hvac", "ahu", "air handling", "fan coil", "ifcairterminal", "ifcunitaryequipment" }),
            ("Zone",       new[]{ "zone", "room", "space", "ifcspace", "ifczone" }),
        };

        public static string NormalizeCategory(BimElementRecord record, string objectName)
        {
            string[] haystack =
            {
                record.Get("Category"),
                record.Get("Element Type"),
                record.Get("ElementType"),
                record.Get("Type"),
                record.Get("Family"),
                record.Get("FamilyName"),
                record.Get("IfcClass"),
                record.Get("Object Type"),
                objectName
            };

            foreach (var source in haystack)
            {
                if (string.IsNullOrEmpty(source)) continue;
                var lower = source.ToLowerInvariant();
                foreach (var rule in CategoryRules)
                {
                    if (rule.tokens.Any(t => lower.Contains(t)))
                        return rule.canonical;
                }
            }
            return "Generic";
        }

        private static readonly Dictionary<string, string> DisciplineByCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Column", "Structure" }, { "Beam", "Structure" }, { "Connection", "Structure" },
            { "Floor", "Structure" }, { "Stair", "Structure" },
            { "Wall", "Architecture" }, { "Curtain Wall", "Architecture" }, { "Door", "Architecture" },
            { "Window", "Architecture" }, { "Ceiling", "Architecture" }, { "Roof", "Architecture" },
            { "Furniture", "Architecture" }, { "Stud", "Architecture" }, { "Railing", "Architecture" },
            { "Zone", "Architecture" },
            { "Duct", "MEP" }, { "Pipe", "MEP" }, { "Lighting", "MEP" }, { "Electric", "MEP" }, { "HVAC", "MEP" },
        };

        public static string MapDiscipline(string category, string elementType, Dictionary<string, string> props)
        {
            if (props != null)
            {
                foreach (var key in new[] { "Discipline", "System Discipline", "Trade" })
                {
                    if (props.TryGetValue(key, out var v) && !IsUndefined(v)) return v;
                }
            }
            if (DisciplineByCategory.TryGetValue(category ?? "", out var d)) return d;
            return "Unknown";
        }

        // ------------------------------------------------------------- queries

        /// <summary>Elements whose normalized category matches any of the supplied canonical names.</summary>
        public List<BimElementRecord> ByCategory(params string[] categories)
        {
            var set = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
            return Elements.Where(e => set.Contains(e.Category)).ToList();
        }

        public List<BimElementRecord> ByDiscipline(string discipline)
        {
            return Elements.Where(e => string.Equals(e.Discipline, discipline, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>Property key actually present in this scene matching any candidate (exact, then substring).</summary>
        public string ResolvePropertyKey(params string[] candidates)
        {
            foreach (var c in candidates)
            {
                var exact = PropertyKeys.FirstOrDefault(k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }
            foreach (var c in candidates)
            {
                var partial = PropertyKeys.FirstOrDefault(k => k.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
                if (partial != null) return partial;
            }
            return null;
        }

        public int NonEmptyCount(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            return Elements.Count(e => e.HasValue(key));
        }

        private static readonly Regex NumberPattern = new Regex(@"-?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        /// <summary>Parses a numeric property, normalizing mm/cm/m and W/kW to base units (m, W).</summary>
        public static bool TryGetNumeric(string raw, out double value)
        {
            value = 0;
            if (IsUndefined(raw)) return false;

            var match = NumberPattern.Match(raw.Replace(",", ""));
            if (!match.Success) return false;
            if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;

            var lower = raw.ToLowerInvariant();
            if (lower.Contains("mm")) value /= 1000.0;
            else if (lower.Contains("cm")) value /= 100.0;
            else if (lower.Contains("kw")) value *= 1000.0;
            return true;
        }

        // ---------------------------------------------------------- spatial ops

        public bool Intersects(BimElementRecord a, BimElementRecord b, float tolerance = 0f)
        {
            if (!a.HasBounds || !b.HasBounds) return false;
            var ba = a.Bounds; var bb = b.Bounds;
            ba.Expand(tolerance);
            return ba.Intersects(bb);
        }

        /// <summary>Gap between two axis-aligned bounds (0 when they touch or overlap).</summary>
        public static float Gap(BimElementRecord a, BimElementRecord b)
        {
            if (!a.HasBounds || !b.HasBounds) return float.MaxValue;
            float dx = Mathf.Max(0, Mathf.Max(a.Bounds.min.x - b.Bounds.max.x, b.Bounds.min.x - a.Bounds.max.x));
            float dy = Mathf.Max(0, Mathf.Max(a.Bounds.min.y - b.Bounds.max.y, b.Bounds.min.y - a.Bounds.max.y));
            float dz = Mathf.Max(0, Mathf.Max(a.Bounds.min.z - b.Bounds.max.z, b.Bounds.min.z - a.Bounds.max.z));
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public bool IsAdjacent(BimElementRecord a, BimElementRecord b, float tolerance)
        {
            if (a == b || !a.HasBounds || !b.HasBounds) return false;
            return Gap(a, b) <= tolerance;
        }

        /// <summary>True when a sits above b with horizontal footprint overlap.</summary>
        public bool IsAbove(BimElementRecord a, BimElementRecord b, float tolerance)
        {
            if (a == b || !a.HasBounds || !b.HasBounds) return false;
            bool overlapXZ = a.Bounds.min.x <= b.Bounds.max.x && b.Bounds.min.x <= a.Bounds.max.x &&
                             a.Bounds.min.z <= b.Bounds.max.z && b.Bounds.min.z <= a.Bounds.max.z;
            return overlapXZ && a.Bounds.min.y >= b.Bounds.max.y - tolerance;
        }

        public bool Contains(BimElementRecord container, BimElementRecord candidate)
        {
            if (container == candidate || !container.HasBounds || !candidate.HasBounds) return false;
            return container.Bounds.Contains(candidate.Bounds.center);
        }

        /// <summary>Cardinal direction of an element's facing normal relative to <see cref="NorthAxis"/>.</summary>
        public string CardinalFacing(BimElementRecord e)
        {
            if (!e.HasBounds) return null;
            var size = e.Bounds.size;
            // Thin axis of a planar element is its normal.
            Vector3 normal = size.x <= size.z ? Vector3.right : Vector3.forward;
            Vector3 north = new Vector3(NorthAxis.x, 0, NorthAxis.z).normalized;
            Vector3 east = new Vector3(north.z, 0, -north.x);

            float n = Vector3.Dot(normal, north);
            float t = Vector3.Dot(normal, east);
            if (Mathf.Abs(n) >= Mathf.Abs(t)) return n >= 0 ? "North" : "South";
            return t >= 0 ? "East" : "West";
        }

        /// <summary>Pairs of elements from two categories whose bounds overlap (clash candidates).</summary>
        public List<(BimElementRecord a, BimElementRecord b)> IntersectingPairs(
            IEnumerable<BimElementRecord> left, IEnumerable<BimElementRecord> right, float tolerance)
        {
            var result = new List<(BimElementRecord, BimElementRecord)>();
            var rightList = right.Where(r => r.HasBounds).ToList();
            foreach (var a in left.Where(l => l.HasBounds))
            {
                foreach (var b in rightList)
                {
                    if (ReferenceEquals(a, b)) continue;
                    if (Intersects(a, b, tolerance)) result.Add((a, b));
                }
            }
            return result;
        }
    }
}
