// BenchmarkSuite.cs - Question catalogue for the BIM-MR conversational benchmark.
//
// Three blocks:
//   Core-30 (Q1-Q30)  verbatim from the manuscript's Table 4 (metadata retrieval / aggregation)
//   R1-R8             verbatim from the manuscript's Table 5 (robustness probes, now executed)
//   H1-H22            new hard families added in response to review: spatial/topological,
//                     cross-discipline, ambiguous language, semantic interpretation, multi-turn
//
// No item carries a hard-coded answer. Each carries a *specification* that GroundTruthEngine
// evaluates against whatever BIM model is loaded, so the suite transfers to other scenes/modules.
using System.Collections.Generic;

namespace AEC.Benchmark
{
    public enum QueryKind
    {
        ListCategoryAttributes,
        CountCategory,
        CountAndSumNumeric,
        SplitByPropertyValue,
        SumNumericGroupBy,
        DistinctPropertyValues,
        CountAndMode,
        MissingProperty,
        FilterNumericThreshold,
        FilterNumericThresholdGrouped,
        FilterByPropertyValue,
        GroupCountByProperty,
        CategoryBreakdown,
        DisciplineSummary,
        SystemGrouping,
        QcStatusAggregation,
        PropertyCompleteness,
        DefinedValueSemantics,
        DuplicateIdentifiers,
        MemoryRecall,
        OutOfScope,
        UnsupportedRelation,
        UnsupportedCompliance,
        MetadataOrientation,
        // geometry-derived
        SpatialAdjacency,
        SpatialAbove,
        SpatialWithinDistance,
        SpatialClash,
        SpatialOrientationGeometry,
        SpatialContainment,
        SpatialRelativePosition,
        SpatialClosestPair,
        // cross-discipline
        DisciplineByLevel,
        UnassignedLevel,
        CategoryRatio
    }

    public class BenchmarkItem
    {
        public string Id;
        public string Family;
        public string Tier;
        public string Prompt;
        public QueryKind Kind;

        public string[] Categories = new string[0];
        public string[] SecondaryCategories = new string[0];
        public string[] PropertyCandidates = new string[0];
        public string[] SecondaryPropertyCandidates = new string[0];

        public double Threshold;
        public string Comparator = "<";
        public string MatchValue;
        public string GroupByProperty;
        public float SpatialTolerance = 0.15f;

        /// <summary>Item needs the transcript of earlier turns (multi-turn recall).</summary>
        public bool RequiresHistory;

        /// <summary>
        /// Id of the earlier item whose answer must be recalled. The runner replays exactly that
        /// exchange (plus the immediately preceding one) instead of the whole transcript, so recall
        /// is genuinely tested without paying to resend the entire dialogue.
        /// </summary>
        public string HistoryAnchorItemId;

        /// <summary>Categories serialized into the context packet; null/empty = whole scene digest.</summary>
        public string[] ContextCategories;

        /// <summary>Serialize geometry (centroid/size) into the packet for this item.</summary>
        public bool IncludeGeometryInContext;

        public override string ToString() => $"{Id} [{Family}]";
    }

    public static class BenchmarkSuite
    {
        public const string SchemaKeysDescription =
            "{ \"answer\": string, \"entities\": [string], \"element_ids\": [string], " +
            "\"confidence\": number 0-1, \"hallucination_flag\": boolean, \"answerable\": boolean, " +
            "\"reason_code\": string }";

        public static List<BenchmarkItem> BuildDefault(bool includeCore, bool includeRobustness, bool includeHard)
        {
            var items = new List<BenchmarkItem>();
            if (includeCore) items.AddRange(Core30());
            if (includeRobustness) items.AddRange(Robustness8());
            if (includeHard) items.AddRange(Hard22());
            return items;
        }

        // ------------------------------------------------------------ Core-30
        // Prompts are verbatim from Table 4 of the manuscript.

        private static IEnumerable<BenchmarkItem> Core30()
        {
            yield return new BenchmarkItem
            {
                Id = "Q1", Family = "Core-Structure", Tier = "T1",
                Prompt = "Can you tell me the name and type of each column in this structure?",
                Kind = QueryKind.ListCategoryAttributes,
                Categories = new[] { "Column" },
                PropertyCandidates = new[] { "Name", "Element Type", "Type" },
                ContextCategories = new[] { "Column" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q2", Family = "Core-Structure", Tier = "T1",
                Prompt = "Could you check if any beams are missing their material information?",
                Kind = QueryKind.MissingProperty,
                Categories = new[] { "Beam" },
                PropertyCandidates = new[] { "Building Material Name", "Material", "Structural Material" },
                ContextCategories = new[] { "Beam" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q3", Family = "Core-Structure", Tier = "T1",
                Prompt = "What is the total number of connections in this structure?",
                Kind = QueryKind.CountCategory,
                Categories = new[] { "Connection" },
                ContextCategories = new[] { "Connection" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q4", Family = "Core-Structure", Tier = "T2",
                Prompt = "Can you tell me how many stairs are included in the structure category, and their total height?",
                Kind = QueryKind.CountAndSumNumeric,
                Categories = new[] { "Stair" },
                PropertyCandidates = new[] { "Height", "Total Height", "Overall Height" },
                ContextCategories = new[] { "Stair" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q5", Family = "Core-Architecture", Tier = "T1",
                Prompt = "Can you tell me how many studs are used in this model?",
                Kind = QueryKind.CountCategory,
                Categories = new[] { "Stud" },
                ContextCategories = new[] { "Stud" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q6", Family = "Core-Architecture", Tier = "T2",
                Prompt = "Could you separate exterior and interior walls for me?",
                Kind = QueryKind.SplitByPropertyValue,
                Categories = new[] { "Wall" },
                PropertyCandidates = new[] { "Position", "Function", "Location", "Exterior", "Is External" },
                MatchValue = "exterior",
                ContextCategories = new[] { "Wall" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q7", Family = "Core-Architecture", Tier = "T1",
                Prompt = "Are there any walls missing their fire rating values?",
                Kind = QueryKind.MissingProperty,
                Categories = new[] { "Wall" },
                PropertyCandidates = new[] { "Fire Rating", "FireRating", "Fire Resistance Rating" },
                ContextCategories = new[] { "Wall" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q8", Family = "Core-Architecture", Tier = "T2",
                Prompt = "What is the total floor area for each level in this model?",
                Kind = QueryKind.SumNumericGroupBy,
                Categories = new[] { "Floor" },
                PropertyCandidates = new[] { "Area", "Gross Area", "Net Area", "Floor Area" },
                GroupByProperty = "Level",
                ContextCategories = new[] { "Floor" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q9", Family = "Core-Architecture", Tier = "T1",
                Prompt = "What materials are used in the ceilings?",
                Kind = QueryKind.DistinctPropertyValues,
                Categories = new[] { "Ceiling" },
                PropertyCandidates = new[] { "Building Material Name", "Material", "Surface Material" },
                ContextCategories = new[] { "Ceiling" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q10", Family = "Core-Envelope", Tier = "T1",
                Prompt = "Can you tell me the type and slope of each roof?",
                Kind = QueryKind.ListCategoryAttributes,
                Categories = new[] { "Roof" },
                PropertyCandidates = new[] { "Element Type", "Type", "Slope", "Pitch" },
                ContextCategories = new[] { "Roof" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q11", Family = "Core-Envelope", Tier = "T1",
                Prompt = "Are there any windows without a glazing type or material defined?",
                Kind = QueryKind.MissingProperty,
                Categories = new[] { "Window" },
                PropertyCandidates = new[] { "Glazing", "Glazing Type", "Building Material Name", "Material" },
                ContextCategories = new[] { "Window" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q12", Family = "Core-Architecture", Tier = "T1",
                Prompt = "Could you list all doors along with their fire ratings?",
                Kind = QueryKind.ListCategoryAttributes,
                Categories = new[] { "Door" },
                PropertyCandidates = new[] { "Fire Rating", "FireRating" },
                ContextCategories = new[] { "Door" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q13", Family = "Core-Architecture", Tier = "T2",
                Prompt = "How many stairs are there in the architecture category, and what is their total riser count?",
                Kind = QueryKind.CountAndSumNumeric,
                Categories = new[] { "Stair" },
                PropertyCandidates = new[] { "Riser Count", "RiserCount", "Number of Risers", "Risers" },
                ContextCategories = new[] { "Stair" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q14", Family = "Core-Architecture", Tier = "T1",
                Prompt = "Can you tell me what kind of furniture is placed in this model?",
                Kind = QueryKind.DistinctPropertyValues,
                Categories = new[] { "Furniture" },
                PropertyCandidates = new[] { "Element Type", "Type", "Category" },
                ContextCategories = new[] { "Furniture" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q15", Family = "Core-MEP", Tier = "T2",
                Prompt = "Can you tell me how many electric fixtures are in this model, and which type appears most often?",
                Kind = QueryKind.CountAndMode,
                Categories = new[] { "Electric" },
                PropertyCandidates = new[] { "Element Type", "Type" },
                ContextCategories = new[] { "Electric" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q16", Family = "Core-OOD", Tier = "T1",
                Prompt = "How is the weather today?",
                Kind = QueryKind.OutOfScope,
                ContextCategories = new string[0]
            };
            yield return new BenchmarkItem
            {
                Id = "Q17", Family = "Core-MEP", Tier = "T1",
                Prompt = "Are there any plumbing components missing diameter or flow rate data?",
                Kind = QueryKind.MissingProperty,
                Categories = new[] { "Pipe" },
                PropertyCandidates = new[] { "Diameter", "Nominal Diameter" },
                SecondaryPropertyCandidates = new[] { "Flow Rate", "FlowRate" },
                ContextCategories = new[] { "Pipe" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q18", Family = "Core-MEP", Tier = "T1",
                Prompt = "Can you list all lighting fixtures and their power ratings?",
                Kind = QueryKind.ListCategoryAttributes,
                Categories = new[] { "Lighting" },
                PropertyCandidates = new[] { "Power", "Power Rating", "Wattage" },
                ContextCategories = new[] { "Lighting" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q19", Family = "Core-MEP", Tier = "T2",
                Prompt = "Which MEP systems are currently connected to the HVAC network?",
                Kind = QueryKind.SystemGrouping,
                Categories = new[] { "HVAC", "Duct", "Pipe", "Electric", "Lighting" },
                PropertyCandidates = new[] { "System Name", "SystemName", "System Type", "Network" },
                MatchValue = "hvac",
                ContextCategories = new[] { "HVAC", "Duct", "Pipe", "Electric", "Lighting" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q20", Family = "Core-Reporting", Tier = "T3",
                Prompt = "Can you give me a summary of all components in the currently loaded BIM model, grouped by category?",
                Kind = QueryKind.CategoryBreakdown
            };
            yield return new BenchmarkItem
            {
                Id = "Q21", Family = "Core-Reporting", Tier = "T2",
                Prompt = "Using only the BIM metadata (not visual inspection), list all element categories in this model " +
                         "(e.g., Walls, Floors, Doors, Columns, etc.) and report how many elements belong to each category.",
                Kind = QueryKind.CategoryBreakdown
            };
            yield return new BenchmarkItem
            {
                Id = "Q22", Family = "Core-QC", Tier = "T2",
                Prompt = "Using the Level and FireRating metadata, calculate for each level how many door elements " +
                         "have a fire rating value lower than 60 minutes.",
                Kind = QueryKind.FilterNumericThresholdGrouped,
                Categories = new[] { "Door" },
                PropertyCandidates = new[] { "Fire Rating", "FireRating" },
                Threshold = 60, Comparator = "<", GroupByProperty = "Level",
                ContextCategories = new[] { "Door" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q23", Family = "Core-QC", Tier = "T2",
                Prompt = "Using the WallType or Thickness metadata, identify all wall elements whose recorded thickness " +
                         "is less than 100 mm and list their element IDs.",
                Kind = QueryKind.FilterNumericThreshold,
                Categories = new[] { "Wall" },
                PropertyCandidates = new[] { "Thickness", "Width", "Wall Thickness" },
                Threshold = 0.1, Comparator = "<",     // metres after unit normalization
                ContextCategories = new[] { "Wall" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q24", Family = "Core-Module", Tier = "T2",
                Prompt = "Using the ModuleID (or equivalent module metadata) and Category metadata, calculate for each " +
                         "module how many structural elements (columns, beams, slabs) are assigned to that module.",
                Kind = QueryKind.GroupCountByProperty,
                Categories = new[] { "Column", "Beam", "Floor" },
                GroupByProperty = "ModuleID",
                ContextCategories = new[] { "Column", "Beam", "Floor" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q25", Family = "Core-Architecture", Tier = "T2",
                Prompt = "Using Room, Floor Finish, and Area metadata, list all rooms whose floor finish is recorded as " +
                         "\"tile\" and report the floor area value stored in the metadata for each room.",
                Kind = QueryKind.FilterByPropertyValue,
                Categories = new[] { "Zone" },
                PropertyCandidates = new[] { "Floor Finish", "Finish", "Floor Covering" },
                SecondaryPropertyCandidates = new[] { "Area", "Net Area" },
                MatchValue = "tile",
                ContextCategories = new[] { "Zone" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q26", Family = "Core-MEP", Tier = "T2",
                Prompt = "Using LightingFixture metadata (e.g., Power, Wattage), determine whether any lighting fixtures " +
                         "have a power rating greater than 200 W. If they exist, list their element IDs together with the " +
                         "power rating recorded in the metadata.",
                Kind = QueryKind.FilterNumericThreshold,
                Categories = new[] { "Lighting" },
                PropertyCandidates = new[] { "Power", "Wattage", "Power Rating" },
                Threshold = 200, Comparator = ">",
                ContextCategories = new[] { "Lighting" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q27", Family = "Core-QC", Tier = "T2",
                Prompt = "Using inspection-related metadata (e.g., InspectionStatus or QCStatus), identify all elements " +
                         "tagged as \"incorrect\" or \"missing\" and report the total number of such elements, as well as a " +
                         "count per category.",
                Kind = QueryKind.QcStatusAggregation,
                PropertyCandidates = new[] { "QC_Status", "QCStatus", "Inspection Status", "InspectionStatus", "Status" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q28", Family = "Core-MEP", Tier = "T3",
                Prompt = "Using MEP system metadata (e.g., SystemName, SystemType, Power/FlowRate), calculate for each MEP " +
                         "system: (1) the total number of connected components, and (2) the sum of their power or flow rating " +
                         "values stored in the metadata, where such values are available.",
                Kind = QueryKind.SystemGrouping,
                Categories = new[] { "HVAC", "Duct", "Pipe", "Electric", "Lighting" },
                PropertyCandidates = new[] { "System Name", "SystemName", "System Type", "SystemType" },
                SecondaryPropertyCandidates = new[] { "Power", "Wattage", "Flow Rate", "FlowRate" },
                ContextCategories = new[] { "HVAC", "Duct", "Pipe", "Electric", "Lighting" }
            };
            yield return new BenchmarkItem
            {
                Id = "Q29", Family = "Core-Memory", Tier = "T3",
                Prompt = "Based on the category-level element counts you previously reported from the BIM metadata, identify " +
                         "which category had the highest number of elements and state that count again, without re-scanning the model.",
                Kind = QueryKind.MemoryRecall,
                RequiresHistory = true,
                HistoryAnchorItemId = "Q21",
                ContextCategories = new string[0]
            };
            yield return new BenchmarkItem
            {
                Id = "Q30", Family = "Core-Reporting", Tier = "T3",
                Prompt = "Using Discipline (or equivalent) metadata and Category metadata, return a JSON report where each key " +
                         "is a discipline (\"Structure\", \"Architecture\", \"MEP\") and each value contains: (1) the total number " +
                         "of elements for that discipline, and (2) the three most frequent categories.",
                Kind = QueryKind.DisciplineSummary
            };
        }

        // -------------------------------------------------------- Robustness 8
        // Prompts verbatim from Table 5. Expected behaviour is derived from the scene, not assumed.

        private static IEnumerable<BenchmarkItem> Robustness8()
        {
            yield return new BenchmarkItem
            {
                Id = "R1", Family = "Robustness-Ambiguity", Tier = "T2",
                Prompt = "Which openings do not have glass or glazing information?",
                Kind = QueryKind.MissingProperty,
                Categories = new[] { "Window", "Door" },
                PropertyCandidates = new[] { "Glazing", "Glazing Type", "Glass" },
                ContextCategories = new[] { "Window", "Door" }
            };
            yield return new BenchmarkItem
            {
                Id = "R2", Family = "Robustness-Threshold", Tier = "T2",
                Prompt = "Are there any fire-safety doors below one hour rating?",
                Kind = QueryKind.FilterNumericThreshold,
                Categories = new[] { "Door" },
                PropertyCandidates = new[] { "Fire Rating", "FireRating" },
                Threshold = 60, Comparator = "<",
                ContextCategories = new[] { "Door" }
            };
            yield return new BenchmarkItem
            {
                Id = "R3", Family = "Robustness-MissingMetadata", Tier = "T2",
                Prompt = "Which rooms or zones have unknown finishes?",
                Kind = QueryKind.MissingProperty,
                Categories = new[] { "Zone" },
                PropertyCandidates = new[] { "Floor Finish", "Finish", "Covering" },
                ContextCategories = new[] { "Zone" }
            };
            yield return new BenchmarkItem
            {
                Id = "R4", Family = "Robustness-Orientation", Tier = "T2",
                Prompt = "Which windows face north?",
                Kind = QueryKind.MetadataOrientation,
                Categories = new[] { "Window" },
                PropertyCandidates = new[] { "Orientation", "W/D Orientation", "Facing" },
                MatchValue = "north",
                ContextCategories = new[] { "Window" }
            };
            yield return new BenchmarkItem
            {
                Id = "R5", Family = "Robustness-Topology", Tier = "T3",
                Prompt = "Which ducts clash with beams?",
                Kind = QueryKind.UnsupportedRelation,
                Categories = new[] { "Duct" },
                SecondaryCategories = new[] { "Beam" },
                PropertyCandidates = new[] { "Clash", "Interference", "Collision" },
                ContextCategories = new[] { "Duct", "Beam" }
            };
            yield return new BenchmarkItem
            {
                Id = "R6", Family = "Robustness-Fabrication", Tier = "T2",
                Prompt = "List components connected to AHU-3.",
                Kind = QueryKind.UnsupportedRelation,
                PropertyCandidates = new[] { "System Name", "SystemName", "Connected To" },
                MatchValue = "AHU-3",
                ContextCategories = new[] { "HVAC", "Duct", "Pipe" }
            };
            yield return new BenchmarkItem
            {
                Id = "R7", Family = "Robustness-Compliance", Tier = "T3",
                Prompt = "Are all fire doors compliant with Korean code?",
                Kind = QueryKind.UnsupportedCompliance,
                Categories = new[] { "Door" },
                PropertyCandidates = new[] { "Fire Rating", "FireRating" },
                ContextCategories = new[] { "Door" }
            };
            yield return new BenchmarkItem
            {
                Id = "R8", Family = "Robustness-Identifiers", Tier = "T2",
                Prompt = "Summarize elements with repeated IDs or conflicting category labels.",
                Kind = QueryKind.DuplicateIdentifiers
            };
        }

        // ------------------------------------------------------------- Hard 22
        // {ANCHOR:Category} / {ANCHOR2:Category} are replaced at run time with concrete element IDs
        // from the loaded scene (lexicographically first in that category), so prompts stay portable.

        private static IEnumerable<BenchmarkItem> Hard22()
        {
            // --- spatial / topological (ground truth from Unity geometry) ---
            yield return new BenchmarkItem
            {
                Id = "H1", Family = "Spatial-Adjacency", Tier = "T4",
                Prompt = "Which elements are directly adjacent to (touching or within 15 cm of) element {ANCHOR:Column}? " +
                         "List their element IDs.",
                Kind = QueryKind.SpatialAdjacency,
                Categories = new[] { "Column" },
                SpatialTolerance = 0.15f,
                IncludeGeometryInContext = true
            };
            yield return new BenchmarkItem
            {
                Id = "H2", Family = "Spatial-Vertical", Tier = "T4",
                Prompt = "Which elements are located directly above element {ANCHOR:Floor}? List their element IDs.",
                Kind = QueryKind.SpatialAbove,
                Categories = new[] { "Floor" },
                SpatialTolerance = 0.2f,
                IncludeGeometryInContext = true
            };
            yield return new BenchmarkItem
            {
                Id = "H3", Family = "Spatial-Proximity", Tier = "T4",
                Prompt = "How many elements lie within 1 metre of element {ANCHOR:Column}? Report the count and the element IDs.",
                Kind = QueryKind.SpatialWithinDistance,
                Categories = new[] { "Column" },
                Threshold = 1.0,
                IncludeGeometryInContext = true
            };
            yield return new BenchmarkItem
            {
                Id = "H4", Family = "Spatial-Clash", Tier = "T4",
                Prompt = "Do any duct or pipe elements geometrically overlap structural beams? If yes, list the overlapping " +
                         "element ID pairs; if no overlap exists, say so explicitly.",
                Kind = QueryKind.SpatialClash,
                Categories = new[] { "Duct", "Pipe" },
                SecondaryCategories = new[] { "Beam" },
                SpatialTolerance = 0f,
                IncludeGeometryInContext = true
            };
            yield return new BenchmarkItem
            {
                Id = "H5", Family = "Spatial-Orientation", Tier = "T4",
                Prompt = "Using the geometric information provided, which windows face north? List their element IDs.",
                Kind = QueryKind.SpatialOrientationGeometry,
                Categories = new[] { "Window" },
                MatchValue = "North",
                IncludeGeometryInContext = true
            };
            yield return new BenchmarkItem
            {
                Id = "H6", Family = "Spatial-Containment", Tier = "T4",
                Prompt = "Which elements are located inside the volume of zone {ANCHOR:Zone}? List their element IDs.",
                Kind = QueryKind.SpatialContainment,
                Categories = new[] { "Zone" },
                IncludeGeometryInContext = true
            };
            yield return new BenchmarkItem
            {
                Id = "H7", Family = "Spatial-Relative", Tier = "T4",
                Prompt = "Is element {ANCHOR:Column} positioned above or below element {ANCHOR2:Floor}? " +
                         "Answer with one word and cite both element IDs.",
                Kind = QueryKind.SpatialRelativePosition,
                IncludeGeometryInContext = true
            };
            yield return new BenchmarkItem
            {
                Id = "H8", Family = "Spatial-Proximity", Tier = "T4",
                Prompt = "Which two elements in the provided set are closest to each other, and what is the distance in metres?",
                Kind = QueryKind.SpatialClosestPair,
                IncludeGeometryInContext = true
            };

            // --- cross-discipline coordination ---
            yield return new BenchmarkItem
            {
                Id = "H9", Family = "CrossDiscipline", Tier = "T4",
                Prompt = "For each level, report how many Structure, Architecture and MEP elements are present.",
                Kind = QueryKind.DisciplineByLevel
            };
            yield return new BenchmarkItem
            {
                Id = "H10", Family = "CrossDiscipline", Tier = "T4",
                Prompt = "Are there MEP elements that are not assigned to any level? Report the count and list their element IDs.",
                Kind = QueryKind.UnassignedLevel,
                Categories = new[] { "Duct", "Pipe", "Lighting", "Electric", "HVAC" },
                ContextCategories = new[] { "Duct", "Pipe", "Lighting", "Electric", "HVAC" }
            };
            yield return new BenchmarkItem
            {
                Id = "H11", Family = "CrossDiscipline", Tier = "T4",
                Prompt = "Which levels contain both structural and MEP elements? For each such level report the count of each discipline.",
                Kind = QueryKind.DisciplineByLevel
            };
            yield return new BenchmarkItem
            {
                Id = "H12", Family = "CrossDiscipline", Tier = "T4",
                Prompt = "Compare the number of architectural openings (doors plus windows) with the number of walls and state " +
                         "the ratio of openings to walls.",
                Kind = QueryKind.CategoryRatio,
                Categories = new[] { "Door", "Window" },
                SecondaryCategories = new[] { "Wall" },
                ContextCategories = new[] { "Door", "Window", "Wall" }
            };

            // --- ambiguous / paraphrased natural language ---
            yield return new BenchmarkItem
            {
                Id = "H13", Family = "Ambiguity-Synonym", Tier = "T4",
                Prompt = "How many uprights are there in this model?",
                Kind = QueryKind.CountCategory,
                Categories = new[] { "Column" },
                ContextCategories = new[] { "Column" }
            };
            yield return new BenchmarkItem
            {
                Id = "H14", Family = "Ambiguity-Vague", Tier = "T4",
                Prompt = "Give me the vertical elements that separate spaces, and how many there are.",
                Kind = QueryKind.CountCategory,
                Categories = new[] { "Wall" },
                ContextCategories = new[] { "Wall" }
            };
            yield return new BenchmarkItem
            {
                Id = "H15", Family = "Ambiguity-Colloquial", Tier = "T4",
                Prompt = "What's the tally of things on the second floor?",
                Kind = QueryKind.GroupCountByProperty,
                GroupByProperty = "Level",
                MatchValue = "2"
            };
            yield return new BenchmarkItem
            {
                Id = "H16", Family = "Ambiguity-Underspecified", Tier = "T4",
                Prompt = "I need the openings info - anything missing there?",
                Kind = QueryKind.MissingProperty,
                Categories = new[] { "Door", "Window" },
                PropertyCandidates = new[] { "Fire Rating", "Glazing", "Building Material Name", "Material" },
                ContextCategories = new[] { "Door", "Window" }
            };

            // --- semantic interpretation / units / null semantics ---
            yield return new BenchmarkItem
            {
                Id = "H17", Family = "Semantic-Units", Tier = "T4",
                Prompt = "Which elements are taller than 2.5 metres? Use the recorded dimension metadata and convert units " +
                         "where the values are not in metres. List element IDs with their height in metres.",
                Kind = QueryKind.FilterNumericThreshold,
                PropertyCandidates = new[] { "Height", "Overall Height", "Z Size" },
                Threshold = 2.5, Comparator = ">"
            };
            yield return new BenchmarkItem
            {
                Id = "H18", Family = "Semantic-Compliance", Tier = "T4",
                Prompt = "Applying a code threshold of 900 mm minimum clear width for egress doors, which doors fail the check? " +
                         "If the required width metadata is not available, state that the check cannot be performed.",
                Kind = QueryKind.FilterNumericThreshold,
                Categories = new[] { "Door" },
                PropertyCandidates = new[] { "Width", "Clear Width", "Nominal Width" },
                Threshold = 0.9, Comparator = "<",
                ContextCategories = new[] { "Door" }
            };
            yield return new BenchmarkItem
            {
                Id = "H19", Family = "Semantic-MetaReasoning", Tier = "T4",
                Prompt = "Assess metadata completeness: which property fields are populated for fewer than 25% of the elements " +
                         "in the provided set? List the field names.",
                Kind = QueryKind.PropertyCompleteness,
                Threshold = 0.25
            };
            yield return new BenchmarkItem
            {
                Id = "H20", Family = "Semantic-NullHandling", Tier = "T4",
                Prompt = "A property recorded as \"<undefined>\" is not a real value. Applying that rule, how many walls have a " +
                         "defined building material, and how many do not?",
                Kind = QueryKind.DefinedValueSemantics,
                Categories = new[] { "Wall" },
                PropertyCandidates = new[] { "Building Material Name", "Material" },
                ContextCategories = new[] { "Wall" }
            };

            // --- multi-turn recall ---
            yield return new BenchmarkItem
            {
                Id = "H21", Family = "Memory-Setup", Tier = "T4",
                Prompt = "How many wall elements are in this model? Give the number and remember it for the next question.",
                Kind = QueryKind.CountCategory,
                Categories = new[] { "Wall" },
                ContextCategories = new[] { "Wall" }
            };
            yield return new BenchmarkItem
            {
                Id = "H22", Family = "Memory-Recall", Tier = "T4",
                Prompt = "Without re-scanning the model, what wall count did you report a moment ago, and what is half of that number?",
                Kind = QueryKind.MemoryRecall,
                Categories = new[] { "Wall" },
                RequiresHistory = true,
                HistoryAnchorItemId = "H21",
                ContextCategories = new string[0]
            };
        }
    }
}
