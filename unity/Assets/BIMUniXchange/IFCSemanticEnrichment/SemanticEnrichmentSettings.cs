using System;
using System.Linq;
using UnityEngine;

namespace BIMUniXchange.SemanticEnrichment
{
    /// <summary>
    /// Hierarchy organization options for semantic enrichment
    /// </summary>
    public enum HierarchyMode
    {
        /// <summary>
        /// Maintain original OBJ hierarchy structure
        /// </summary>
        KeepOriginal,

        /// <summary>
        /// Organize by IFC spatial structure (Site > Building > Storey > Space)
        /// </summary>
        IFCSpatialStructure,

        /// <summary>
        /// Organize by discipline (Architectural, Structural, MEP, etc.)
        /// </summary>
        ByDiscipline,

        /// <summary>
        /// Organize by IFC element type (IfcWall, IfcDoor, IfcWindow, etc.)
        /// </summary>
        ByElementType,

        /// <summary>
        /// Organize by building storey only
        /// </summary>
        ByStorey,

        /// <summary>
        /// Organize by category (Walls, Doors, Windows, Floors, etc.)
        /// </summary>
        ByCategory,

        /// <summary>
        /// Flat structure - all elements as children of root
        /// </summary>
        Flat
    }

    /// <summary>
    /// Settings for semantic enrichment process
    /// </summary>
    [Serializable]
    public class SemanticEnrichmentSettings
    {
        [Header("Input")]
        public GameObject SourceObject;
        public string IfcFilePath = "";
        public UnityEngine.Object IfcFileAsset;

        [Header("Unpack Options")]
        [Tooltip("Unpack prefab instance completely before reorganizing hierarchy")]
        public bool UnpackPrefab = true;

        [Header("Hierarchy Options")]
        public HierarchyMode HierarchyMode = HierarchyMode.IFCSpatialStructure;
        public bool CreateEmptyContainers = true;
        public bool SortAlphabetically = true;

        [Header("Element ID Matching")]
        [Tooltip("Separator between element name and ID in OBJ mesh names")]
        public string IdSeparator = "_";

        [Tooltip("Position of Element ID in the mesh name (from end, 0 = last part)")]
        public int IdPositionFromEnd = 0;

        [Tooltip("Try to match by GlobalId if Element ID match fails")]
        public bool FallbackToGlobalId = true;

        [Tooltip("Case-insensitive matching")]
        public bool CaseInsensitiveMatch = true;

        [Header("Metadata Options")]
        public bool ImportPropertySets = true;
        public bool ImportQuantitySets = true;
        public bool ImportMaterials = true;
        public bool ImportClassifications = true;
        public bool ImportRelationships = true;
        public bool TrackDataLoss = true;

        [Header("Material Options")]
        public bool PreserveObjMaterials = true;
        public bool ApplyIfcColors = false;

        [Header("Output")]
        public string OutputFolder = "Assets/EnrichedModels";
        public bool CreatePrefab = true;
        public bool SaveMeshesAsSeparateAssets = false;

        /// <summary>
        /// Extract Element ID from OBJ mesh name based on settings
        /// </summary>
        public string ExtractElementId(string meshName)
        {
            if (string.IsNullOrEmpty(meshName)) return null;

            string[] parts = meshName.Split(new[] { IdSeparator }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0) return null;

            int index = parts.Length - 1 - IdPositionFromEnd;
            if (index < 0 || index >= parts.Length) return null;

            string id = parts[index].Trim();

            // Clean up common suffixes
            if (id.EndsWith("_mesh") || id.EndsWith("_Mesh"))
            {
                id = id.Substring(0, id.Length - 5);
            }

            return CaseInsensitiveMatch ? id.ToLowerInvariant() : id;
        }

        /// <summary>
        /// Get the discipline for an IFC element type
        /// </summary>
        public static string GetDiscipline(string ifcType)
        {
            if (string.IsNullOrEmpty(ifcType)) return "Other";

            string type = ifcType.ToLowerInvariant();

            // Structural
            if (type.Contains("beam") || type.Contains("column") || type.Contains("slab") ||
                type.Contains("footing") || type.Contains("pile") || type.Contains("tendon") ||
                type.Contains("reinforcing"))
                return "Structural";

            // MEP - Mechanical
            if (type.Contains("duct") || type.Contains("airterm") || type.Contains("fan") ||
                type.Contains("coil") || type.Contains("damper") || type.Contains("filter") ||
                type.Contains("humidifier") || type.Contains("chiller") || type.Contains("boiler") ||
                type.Contains("heatexchanger") || type.Contains("unitaryequipment"))
                return "Mechanical";

            // MEP - Electrical
            if (type.Contains("cable") || type.Contains("electric") || type.Contains("light") ||
                type.Contains("outlet") || type.Contains("switch") || type.Contains("junction") ||
                type.Contains("transformer") || type.Contains("motor"))
                return "Electrical";

            // MEP - Plumbing
            if (type.Contains("pipe") || type.Contains("pump") || type.Contains("valve") ||
                type.Contains("tank") || type.Contains("sanitary") || type.Contains("firesuppress") ||
                type.Contains("flowmeter") || type.Contains("interceptor"))
                return "Plumbing";

            // Architectural
            if (type.Contains("wall") || type.Contains("door") || type.Contains("window") ||
                type.Contains("stair") || type.Contains("ramp") || type.Contains("roof") ||
                type.Contains("ceiling") || type.Contains("covering") || type.Contains("curtainwall") ||
                type.Contains("railing") || type.Contains("furniture"))
                return "Architectural";

            // Site
            if (type.Contains("site") || type.Contains("geographic") || type.Contains("building"))
                return "Site";

            return "Other";
        }

        /// <summary>
        /// Get a friendly category name for an IFC element type
        /// </summary>
        public static string GetCategory(string ifcType)
        {
            if (string.IsNullOrEmpty(ifcType)) return "Other";

            // Remove "Ifc" prefix and add spaces before capitals
            string category = ifcType;
            if (category.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase))
            {
                category = category.Substring(3);
            }

            // Common mappings
            switch (category.ToLowerInvariant())
            {
                case "wallstandardcase": return "Walls";
                case "wall": return "Walls";
                case "door": return "Doors";
                case "window": return "Windows";
                case "slab": return "Floors";
                case "roof": return "Roofs";
                case "stair": return "Stairs";
                case "stairflight": return "Stairs";
                case "railing": return "Railings";
                case "column": return "Columns";
                case "beam": return "Beams";
                case "curtainwall": return "Curtain Walls";
                case "covering": return "Coverings";
                case "ceiling": return "Ceilings";
                case "furniture": return "Furniture";
                case "furnishingelement": return "Furniture";
                case "buildingelementproxy": return "Generic Models";
                case "flowsegment": return "Pipes/Ducts";
                case "flowterminal": return "Terminals";
                case "flowfitting": return "Fittings";
                default: return category;
            }
        }
    }

    /// <summary>
    /// Result of the semantic enrichment process
    /// </summary>
    [Serializable]
    public class SemanticEnrichmentResult
    {
        public bool Success;
        public string OutputPath;
        public int TotalObjElements;
        public int TotalIfcElements;
        public int MatchedElements;
        public int UnmatchedObjElements;
        public int TotalPropertiesTransferred;
        public float ProcessingTimeSeconds;
        public string ErrorMessage;

        // Performance Metrics
        public long MemoryAtStart;
        public long MemoryAtEnd;
        public long PeakMemoryUsed;
        public float IfcLoadTimeSeconds;
        public float MatchingTimeSeconds;
        public float HierarchyTimeSeconds;
        public float MetadataTransferTimeSeconds;

        // Detailed Statistics
        public int TotalPropertySetsTransferred;
        public int TotalQuantitySetsTransferred;
        public int TotalMaterialsTransferred;
        public int TotalRelationshipsTransferred;
        public int HierarchyNodesCreated;
        public int ElementsReorganized;

        // IFC Model Statistics
        public int IfcProjectCount;
        public int IfcSiteCount;
        public int IfcBuildingCount;
        public int IfcStoreyCount;
        public int IfcSpaceCount;

        public System.Collections.Generic.List<string> MatchedIds = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> UnmatchedObjIds = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> Warnings = new System.Collections.Generic.List<string>();

        // Category Statistics
        [NonSerialized] public System.Collections.Generic.Dictionary<string, int> ElementsByCategory = new System.Collections.Generic.Dictionary<string, int>();
        [NonSerialized] public System.Collections.Generic.Dictionary<string, int> ElementsByStorey = new System.Collections.Generic.Dictionary<string, int>();

        public float MatchPercentage => TotalObjElements > 0 ? (float)MatchedElements / TotalObjElements * 100f : 0;
        public float DataLossPercentage => TotalObjElements > 0 ? (float)UnmatchedObjElements / TotalObjElements * 100f : 0;
        public float IfcUtilizationPercentage => TotalIfcElements > 0 ? (float)MatchedElements / TotalIfcElements * 100f : 0;
        public float ProcessingSpeed => ProcessingTimeSeconds > 0 ? TotalObjElements / ProcessingTimeSeconds : 0;
        public long NetMemoryChange => MemoryAtEnd - MemoryAtStart;

        /// <summary>
        /// Format memory size to human readable string
        /// </summary>
        public static string FormatMemorySize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = System.Math.Abs(bytes);
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        }

        /// <summary>
        /// Get comprehensive performance summary as formatted string
        /// </summary>
        public string GetPerformanceSummary()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║         IFC SEMANTIC ENRICHMENT SUMMARY                      ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");

            // Processing Statistics
            sb.AppendLine();
            sb.AppendLine("📊 PROCESSING STATISTICS:");
            sb.AppendLine(string.Format("  ├─ Total OBJ Elements: {0:N0}", TotalObjElements));
            sb.AppendLine(string.Format("  ├─ Total IFC Elements: {0:N0}", TotalIfcElements));
            sb.AppendLine(string.Format("  ├─ Successfully Matched: {0:N0} ({1:F2}%)", MatchedElements, MatchPercentage));
            sb.AppendLine(string.Format("  ├─ Unmatched Elements: {0:N0} ({1:F2}%)", UnmatchedObjElements, DataLossPercentage));
            sb.AppendLine(string.Format("  ├─ IFC Utilization: {0:F2}%", IfcUtilizationPercentage));
            sb.AppendLine(string.Format("  └─ Hierarchy Nodes Created: {0:N0}", HierarchyNodesCreated));

            // Data Loss Analysis
            sb.AppendLine();
            sb.AppendLine("⚠️ DATA LOSS ANALYSIS:");
            string dataLossStatus = DataLossPercentage == 0 ? "✓ NO DATA LOSS" :
                                    DataLossPercentage < 5 ? "⚠ MINIMAL DATA LOSS" :
                                    DataLossPercentage < 20 ? "⚠ MODERATE DATA LOSS" :
                                    "✗ SIGNIFICANT DATA LOSS";
            sb.AppendLine(string.Format("  ├─ Status: {0}", dataLossStatus));
            sb.AppendLine(string.Format("  ├─ Elements without IFC match: {0:N0}", UnmatchedObjElements));
            sb.AppendLine(string.Format("  ├─ Data Loss Percentage: {0:F2}%", DataLossPercentage));
            sb.AppendLine(string.Format("  └─ Successfully Enriched: {0:F2}%", 100 - DataLossPercentage));

            // Metadata Transfer Statistics
            sb.AppendLine();
            sb.AppendLine("📦 METADATA TRANSFER:");
            sb.AppendLine(string.Format("  ├─ Total Properties Transferred: {0:N0}", TotalPropertiesTransferred));
            sb.AppendLine(string.Format("  ├─ Property Sets: {0:N0}", TotalPropertySetsTransferred));
            sb.AppendLine(string.Format("  ├─ Quantity Sets: {0:N0}", TotalQuantitySetsTransferred));
            sb.AppendLine(string.Format("  ├─ Materials: {0:N0}", TotalMaterialsTransferred));
            sb.AppendLine(string.Format("  ├─ Relationships: {0:N0}", TotalRelationshipsTransferred));
            sb.AppendLine(string.Format("  └─ Avg Properties/Element: {0:F1}", MatchedElements > 0 ? (float)TotalPropertiesTransferred / MatchedElements : 0));

            // IFC Model Statistics
            sb.AppendLine();
            sb.AppendLine("🏗️ IFC MODEL STRUCTURE:");
            sb.AppendLine(string.Format("  ├─ Projects: {0:N0}", IfcProjectCount));
            sb.AppendLine(string.Format("  ├─ Sites: {0:N0}", IfcSiteCount));
            sb.AppendLine(string.Format("  ├─ Buildings: {0:N0}", IfcBuildingCount));
            sb.AppendLine(string.Format("  ├─ Storeys: {0:N0}", IfcStoreyCount));
            sb.AppendLine(string.Format("  └─ Spaces: {0:N0}", IfcSpaceCount));

            // Performance Metrics
            sb.AppendLine();
            sb.AppendLine("⏱️ PERFORMANCE METRICS:");
            sb.AppendLine(string.Format("  ├─ Total Processing Time: {0:F2} seconds ({1:N0} ms)", ProcessingTimeSeconds, ProcessingTimeSeconds * 1000));
            sb.AppendLine(string.Format("  ├─ IFC Loading Time: {0:F2}s", IfcLoadTimeSeconds));
            sb.AppendLine(string.Format("  ├─ Matching Time: {0:F2}s", MatchingTimeSeconds));
            sb.AppendLine(string.Format("  ├─ Metadata Transfer Time: {0:F2}s", MetadataTransferTimeSeconds));
            sb.AppendLine(string.Format("  ├─ Hierarchy Reorganization Time: {0:F2}s", HierarchyTimeSeconds));
            sb.AppendLine(string.Format("  └─ Processing Speed: {0:F1} elements/second", ProcessingSpeed));

            // Memory Usage
            sb.AppendLine();
            sb.AppendLine("💾 MEMORY USAGE:");
            sb.AppendLine(string.Format("  ├─ Memory at Start: {0}", FormatMemorySize(MemoryAtStart)));
            sb.AppendLine(string.Format("  ├─ Memory at End: {0}", FormatMemorySize(MemoryAtEnd)));
            sb.AppendLine(string.Format("  ├─ Peak Memory Used: {0}", FormatMemorySize(PeakMemoryUsed)));
            sb.AppendLine(string.Format("  ├─ Net Memory Change: {0}{1}", NetMemoryChange >= 0 ? "+" : "", FormatMemorySize(NetMemoryChange)));
            sb.AppendLine(string.Format("  └─ Memory per Element: {0}", MatchedElements > 0 ? FormatMemorySize(System.Math.Abs(NetMemoryChange) / MatchedElements) : "N/A"));

            // Top Categories
            if (ElementsByCategory != null && ElementsByCategory.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("📁 TOP ELEMENT CATEGORIES:");
                int rank = 1;
                foreach (var kvp in ElementsByCategory.OrderByDescending(x => x.Value).Take(10))
                {
                    float categoryPercent = MatchedElements > 0 ? (float)kvp.Value / MatchedElements * 100 : 0;
                    sb.AppendLine(string.Format("  {0,2}. {1}: {2:N0} elements ({3:F1}%)", rank, kvp.Key, kvp.Value, categoryPercent));
                    rank++;
                }
                if (ElementsByCategory.Count > 10)
                    sb.AppendLine(string.Format("  ... and {0} more categories", ElementsByCategory.Count - 10));
            }

            // Recommendations
            if (UnmatchedObjElements > 0)
            {
                sb.AppendLine();
                sb.AppendLine("💡 RECOMMENDATIONS:");
                sb.AppendLine(string.Format("  ⚠ {0:N0} elements could not be matched to IFC data.", UnmatchedObjElements));
                sb.AppendLine("  Possible causes:");
                sb.AppendLine("    • Element ID extraction pattern mismatch");
                sb.AppendLine("    • Missing elements in IFC file");
                sb.AppendLine("    • Tag/GlobalId not present in IFC elements");
                sb.AppendLine("  Suggested actions:");
                sb.AppendLine("    • Check ID separator and position settings");
                sb.AppendLine("    • Enable 'Fallback to GlobalId' option");
                sb.AppendLine("    • Use Preview ID Extraction to verify patterns");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("✓ ALL ELEMENTS SUCCESSFULLY MATCHED WITH IFC DATA!");
            }

            sb.AppendLine();
            sb.AppendLine("════════════════════════════════════════════════════════════════");

            return sb.ToString();
        }
    }
}
