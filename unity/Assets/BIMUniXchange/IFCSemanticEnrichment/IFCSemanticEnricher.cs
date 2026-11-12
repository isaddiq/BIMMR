using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BIMUniXchange.SemanticEnrichment
{
    /// <summary>
    /// Main class for semantic enrichment - matches OBJ mesh elements with IFC metadata
    /// </summary>
    public class IFCSemanticEnricher
    {
        private SemanticEnrichmentSettings _settings;
        private IfcModelNavigator _ifcNavigator;
        private Dictionary<string, IfcElementData> _ifcElementsByTag;
        private Dictionary<string, IfcElementData> _ifcElementsByGlobalId;
        private Dictionary<string, IfcElementData> _ifcElementsByName;
        private Dictionary<string, SpatialNode> _storeyByProduct;
        private Dictionary<string, string> _spatialStructure;

        // Full IFC schema structure
        private Dictionary<string, SpatialNode> _allSpatialElements;
        private Dictionary<string, Transform> _spatialContainers;
        private Dictionary<string, List<string>> _spatialContainment; // StoreyGlobalId -> List of element GlobalIds

        // IFC Relationship caches for hierarchy building
        private Dictionary<string, string> _openingToHost;  // Opening GlobalId -> Host Element GlobalId
        private Dictionary<string, string> _fillingToOpening;  // Door/Window GlobalId -> Opening GlobalId
        private Dictionary<string, List<string>> _hostToOpenings;  // Host Element GlobalId -> List of Opening GlobalIds
        private Dictionary<string, List<string>> _elementConnections;  // Element GlobalId -> Connected Element GlobalIds

        public IFCSemanticEnricher(SemanticEnrichmentSettings settings)
        {
            _settings = settings;
            _ifcElementsByTag = new Dictionary<string, IfcElementData>(StringComparer.OrdinalIgnoreCase);
            _ifcElementsByGlobalId = new Dictionary<string, IfcElementData>(StringComparer.OrdinalIgnoreCase);
            _ifcElementsByName = new Dictionary<string, IfcElementData>(StringComparer.OrdinalIgnoreCase);
            _storeyByProduct = new Dictionary<string, SpatialNode>(StringComparer.OrdinalIgnoreCase);
            _spatialStructure = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _allSpatialElements = new Dictionary<string, SpatialNode>(StringComparer.OrdinalIgnoreCase);
            _spatialContainers = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            _spatialContainment = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _openingToHost = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _fillingToOpening = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _hostToOpenings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _elementConnections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Process the semantic enrichment (metadata + hierarchy)
        /// </summary>
        public SemanticEnrichmentResult Process()
        {
            var result = new SemanticEnrichmentResult();
            var startTime = DateTime.Now;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Initialize performance tracking
            result.MemoryAtStart = GC.GetTotalMemory(false);
            result.PeakMemoryUsed = result.MemoryAtStart;

            try
            {
                Debug.Log("╔══════════════════════════════════════════════════════════════╗");
                Debug.Log("║         IFC SEMANTIC ENRICHMENT START                        ║");
                Debug.Log("╚══════════════════════════════════════════════════════════════╝");
                Debug.Log(string.Format("Start Time: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));
                Debug.Log(string.Format("Initial Memory: {0}", SemanticEnrichmentResult.FormatMemorySize(result.MemoryAtStart)));

                // Validate inputs
                if (!ValidateInputs(result)) return result;

                // Unpack prefab if needed
                UnpackSourceObject();
                UpdatePeakMemory(result);

                // Load IFC model and build element dictionary
                var ifcLoadStopwatch = System.Diagnostics.Stopwatch.StartNew();
                if (!LoadIfcModel(result)) return result;
                ifcLoadStopwatch.Stop();
                result.IfcLoadTimeSeconds = (float)ifcLoadStopwatch.Elapsed.TotalSeconds;
                Debug.Log(string.Format("IFC Loading Time: {0:F2}s", result.IfcLoadTimeSeconds));
                UpdatePeakMemory(result);

                // Process source object and match elements
                var matchStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var enrichedRoot = ProcessObjAndMatch(result);
                matchStopwatch.Stop();
                result.MatchingTimeSeconds = (float)matchStopwatch.Elapsed.TotalSeconds;
                Debug.Log(string.Format("Matching Time: {0:F2}s", result.MatchingTimeSeconds));

                if (enrichedRoot == null) return result;
                UpdatePeakMemory(result);

                // Reorganize hierarchy if needed
                var hierarchyStopwatch = System.Diagnostics.Stopwatch.StartNew();
                ReorganizeHierarchy(enrichedRoot, result);
                hierarchyStopwatch.Stop();
                result.HierarchyTimeSeconds = (float)hierarchyStopwatch.Elapsed.TotalSeconds;
                Debug.Log(string.Format("Hierarchy Reorganization Time: {0:F2}s", result.HierarchyTimeSeconds));
                UpdatePeakMemory(result);

                // Save output
                SaveOutput(enrichedRoot, result);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = "Error during enrichment: " + ex.Message + "\n" + ex.StackTrace;
                Debug.LogError(result.ErrorMessage);
            }
            finally
            {
                if (_ifcNavigator != null)
                {
                    _ifcNavigator.Dispose();
                    _ifcNavigator = null;
                }

                stopwatch.Stop();
                result.ProcessingTimeSeconds = (float)stopwatch.Elapsed.TotalSeconds;
                result.MemoryAtEnd = GC.GetTotalMemory(false);
                UpdatePeakMemory(result);

                // Log comprehensive summary
                Debug.Log(result.GetPerformanceSummary());
            }

            return result;
        }

        /// <summary>
        /// Update peak memory tracking
        /// </summary>
        private void UpdatePeakMemory(SemanticEnrichmentResult result)
        {
            long currentMemory = GC.GetTotalMemory(false);
            if (currentMemory > result.PeakMemoryUsed)
            {
                result.PeakMemoryUsed = currentMemory;
            }
        }

        /// <summary>
        /// Assign metadata only (no hierarchy reorganization)
        /// </summary>
        public SemanticEnrichmentResult AssignMetadataOnly()
        {
            var result = new SemanticEnrichmentResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Initialize performance tracking
            result.MemoryAtStart = GC.GetTotalMemory(false);
            result.PeakMemoryUsed = result.MemoryAtStart;

            try
            {
                Debug.Log("╔══════════════════════════════════════════════════════════════╗");
                Debug.Log("║         IFC METADATA ASSIGNMENT START                        ║");
                Debug.Log("╚══════════════════════════════════════════════════════════════╝");
                Debug.Log(string.Format("Start Time: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));
                Debug.Log(string.Format("Initial Memory: {0}", SemanticEnrichmentResult.FormatMemorySize(result.MemoryAtStart)));

                // Validate inputs
                if (!ValidateInputs(result)) return result;

                // Load IFC model and build element dictionary
                var ifcLoadStopwatch = System.Diagnostics.Stopwatch.StartNew();
                if (!LoadIfcModel(result)) return result;
                ifcLoadStopwatch.Stop();
                result.IfcLoadTimeSeconds = (float)ifcLoadStopwatch.Elapsed.TotalSeconds;
                Debug.Log(string.Format("IFC Loading Time: {0:F2}s", result.IfcLoadTimeSeconds));
                UpdatePeakMemory(result);

                // Process source object and match elements (assigns metadata)
                var matchStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var enrichedRoot = ProcessObjAndMatch(result);
                matchStopwatch.Stop();
                result.MatchingTimeSeconds = (float)matchStopwatch.Elapsed.TotalSeconds;
                result.MetadataTransferTimeSeconds = result.MatchingTimeSeconds; // Same as matching in this case
                Debug.Log(string.Format("Matching & Metadata Transfer Time: {0:F2}s", result.MatchingTimeSeconds));

                if (enrichedRoot == null) return result;
                UpdatePeakMemory(result);

                result.Success = true;
                result.OutputPath = "Metadata assigned to: " + _settings.SourceObject.name;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = "Error during metadata assignment: " + ex.Message + "\n" + ex.StackTrace;
                Debug.LogError(result.ErrorMessage);
            }
            finally
            {
                if (_ifcNavigator != null)
                {
                    _ifcNavigator.Dispose();
                    _ifcNavigator = null;
                }

                stopwatch.Stop();
                result.ProcessingTimeSeconds = (float)stopwatch.Elapsed.TotalSeconds;
                result.MemoryAtEnd = GC.GetTotalMemory(false);
                UpdatePeakMemory(result);

                // Log comprehensive summary
                Debug.Log(result.GetPerformanceSummary());
            }

            return result;
        }

        /// <summary>
        /// Reorganize hierarchy only (requires IFC metadata to already be assigned)
        /// </summary>
        public SemanticEnrichmentResult ReorganizeHierarchyOnly()
        {
            var result = new SemanticEnrichmentResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Initialize performance tracking
            result.MemoryAtStart = GC.GetTotalMemory(false);
            result.PeakMemoryUsed = result.MemoryAtStart;

            try
            {
                Debug.Log("╔══════════════════════════════════════════════════════════════╗");
                Debug.Log("║         IFC HIERARCHY REORGANIZATION START                   ║");
                Debug.Log("╚══════════════════════════════════════════════════════════════╝");
                Debug.Log(string.Format("Start Time: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));
                Debug.Log(string.Format("Initial Memory: {0}", SemanticEnrichmentResult.FormatMemorySize(result.MemoryAtStart)));

                if (_settings.SourceObject == null)
                {
                    result.ErrorMessage = "Source Object is not assigned";
                    return result;
                }

                // Unpack prefab if needed
                UnpackSourceObject();
                UpdatePeakMemory(result);

                // Count elements
                var meshRenderers = _settings.SourceObject.GetComponentsInChildren<MeshRenderer>(true);
                result.TotalObjElements = meshRenderers.Length;

                // Count matched (those with IFC metadata)
                result.MatchedElements = meshRenderers.Count(r => r.GetComponent<IFCMetadata>() != null);
                result.UnmatchedObjElements = result.TotalObjElements - result.MatchedElements;

                Debug.Log(string.Format("Found {0} elements, {1} with IFC metadata", result.TotalObjElements, result.MatchedElements));

                // Reorganize hierarchy
                var hierarchyStopwatch = System.Diagnostics.Stopwatch.StartNew();
                ReorganizeHierarchy(_settings.SourceObject, result);
                hierarchyStopwatch.Stop();
                result.HierarchyTimeSeconds = (float)hierarchyStopwatch.Elapsed.TotalSeconds;
                Debug.Log(string.Format("Hierarchy Reorganization Time: {0:F2}s", result.HierarchyTimeSeconds));
                UpdatePeakMemory(result);

                // Save output if needed
                SaveOutput(_settings.SourceObject, result);

                result.Success = true;
                result.OutputPath = "Hierarchy reorganized: " + _settings.SourceObject.name;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = "Error during hierarchy reorganization: " + ex.Message + "\n" + ex.StackTrace;
                Debug.LogError(result.ErrorMessage);
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTimeSeconds = (float)stopwatch.Elapsed.TotalSeconds;
                result.MemoryAtEnd = GC.GetTotalMemory(false);
                UpdatePeakMemory(result);

                // Log comprehensive summary
                Debug.Log(result.GetPerformanceSummary());
            }

            return result;
        }

        /// <summary>
        /// Unpack the source object if it's a prefab instance
        /// </summary>
        private void UnpackSourceObject()
        {
#if UNITY_EDITOR
            if (_settings.UnpackPrefab && _settings.SourceObject != null)
            {
                if (UnityEditor.PrefabUtility.IsPartOfAnyPrefab(_settings.SourceObject))
                {
                    Debug.Log("Unpacking prefab: " + _settings.SourceObject.name);

                    // Unpack completely so all children can be reorganized
                    UnityEditor.PrefabUtility.UnpackPrefabInstance(
                        _settings.SourceObject,
                        UnityEditor.PrefabUnpackMode.Completely,
                        UnityEditor.InteractionMode.AutomatedAction);

                    Debug.Log("Prefab unpacked successfully");
                }
            }
#endif
        }

        private bool ValidateInputs(SemanticEnrichmentResult result)
        {
            if (_settings.SourceObject == null)
            {
                result.ErrorMessage = "Source Object is not assigned";
                return false;
            }

            if (string.IsNullOrEmpty(_settings.IfcFilePath) || !File.Exists(_settings.IfcFilePath))
            {
                result.ErrorMessage = "IFC file not found: " + _settings.IfcFilePath;
                return false;
            }

            return true;
        }

        private bool LoadIfcModel(SemanticEnrichmentResult result)
        {
            try
            {
                Debug.Log(string.Format("Loading IFC file: {0}", _settings.IfcFilePath));

                // Initialize the IFC Navigator with Python bridge
                _ifcNavigator = new IfcModelNavigator();

                if (!_ifcNavigator.LoadModel(_settings.IfcFilePath))
                {
                    result.ErrorMessage = "Failed to load IFC model through Python bridge";
                    return false;
                }

                // Log IFC schema version for debugging
                string schemaVersion = _ifcNavigator.GetSchemaVersion();
                Debug.Log(string.Format("IFC Schema Version: {0}", schemaVersion));

                // Collect IFC model statistics for reporting
                var sites = _ifcNavigator.GetSites();
                var buildings = _ifcNavigator.GetBuildings();
                var storeys = _ifcNavigator.GetStoreys();
                var spaces = _ifcNavigator.GetSpaces();

                result.IfcProjectCount = 1; // Always 1 project
                result.IfcSiteCount = sites.Count;
                result.IfcBuildingCount = buildings.Count;
                result.IfcStoreyCount = storeys.Count;
                result.IfcSpaceCount = spaces.Count;

                Debug.Log(string.Format("IFC Structure: {0} Project(s), {1} Site(s), {2} Building(s), {3} Storey(s), {4} Space(s)",
                    result.IfcProjectCount, result.IfcSiteCount, result.IfcBuildingCount, result.IfcStoreyCount, result.IfcSpaceCount));

                // Build element dictionaries and count by category
                var allElements = _ifcNavigator.GetAllElements().ToList();
                int totalProducts = 0;
                var categoryCount = new Dictionary<string, int>();

                foreach (var elementInfo in allElements)
                {
                    totalProducts++;
                    var element = elementInfo.ElementData;

                    // Track elements by IFC type (category)
                    string ifcTypeName = element.ifcClass;
                    if (categoryCount.ContainsKey(ifcTypeName))
                        categoryCount[ifcTypeName]++;
                    else
                        categoryCount[ifcTypeName] = 1;

                    // Index by Tag (Element ID)
                    string tag = element.tag;
                    if (!string.IsNullOrEmpty(tag))
                    {
                        string key = _settings.CaseInsensitiveMatch ? tag.ToLowerInvariant() : tag;
                        if (!_ifcElementsByTag.ContainsKey(key))
                        {
                            _ifcElementsByTag[key] = element;
                        }
                    }

                    // Index by GlobalId
                    string globalId = element.globalId;
                    if (!string.IsNullOrEmpty(globalId))
                    {
                        string key = _settings.CaseInsensitiveMatch ? globalId.ToLowerInvariant() : globalId;
                        _ifcElementsByGlobalId[key] = element;
                    }

                    // Index by Name (may have collisions, last wins)
                    string name = element.name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        string key = _settings.CaseInsensitiveMatch ? name.ToLowerInvariant() : name;
                        _ifcElementsByName[key] = element;
                    }
                }

                // Store category breakdown for reporting
                result.ElementsByCategory = categoryCount;

                // Build spatial structure mapping
                BuildSpatialStructureMapping();

                // Note: Relationship caches are now handled by Python bridge
                result.TotalRelationshipsTransferred = 0; // Will be counted during metadata transfer

                result.TotalIfcElements = totalProducts;
                Debug.Log(string.Format("Loaded {0} total IFC products: {1} with Tags, {2} with GlobalIds, {3} by Name",
                    totalProducts, _ifcElementsByTag.Count, _ifcElementsByGlobalId.Count, _ifcElementsByName.Count));

                // Log top categories
                var topCategories = categoryCount.OrderByDescending(kv => kv.Value).Take(10);
                Debug.Log("Top IFC Categories: " + string.Join(", ", topCategories.Select(kv => string.Format("{0}: {1}", kv.Key, kv.Value))));

                return true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("Failed to load IFC file: {0}", ex.Message);
                Debug.LogError(result.ErrorMessage);
                return false;
            }
        }

        /// <summary>
        /// Build relationship caches - now handled by Python bridge
        /// </summary>
        private void BuildRelationshipCaches()
        {
            // Relationships are now handled by the Python bridge during property extraction
            // The Python script builds opening/filling/connection caches automatically
            Debug.Log("Relationship caches now handled by Python bridge");
        }

        private string GetProductTag(IfcElementData element)
        {
            // Return the tag directly from IfcElementData
            if (element == null) return null;
            return !string.IsNullOrEmpty(element.tag) ? element.tag : null;
        }

        /// <summary>
        /// Get the IFC schema version from the loaded model
        /// </summary>
        private string GetIfcSchemaVersion()
        {
            try
            {
                if (_ifcNavigator == null)
                    return "Unknown";

                return _ifcNavigator.GetSchemaVersion();
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Safely get a property value handling version differences
        /// </summary>
        private string SafeGetPropertyValue(object obj, string propertyName)
        {
            if (obj == null)
                return "";

            try
            {
                var property = obj.GetType().GetProperty(propertyName);
                if (property != null)
                {
                    var value = property.GetValue(obj);
                    return value?.ToString() ?? "";
                }
            }
            catch
            {
                // Property doesn't exist in this IFC version
            }

            return "";
        }

        private void BuildSpatialStructureMapping()
        {
            // Spatial structure is now built by IfcModelNavigator through Python
            var spatialTree = _ifcNavigator.GetSpatialTree();
            if (spatialTree == null)
            {
                Debug.LogWarning("No spatial tree found");
                return;
            }

            // Build flat lookup dictionary from the tree
            BuildSpatialLookup(spatialTree, "");

            Debug.Log($"Built spatial structure mapping with {_allSpatialElements.Count} spatial elements");
        }

        private void BuildSpatialLookup(SpatialNode node, string parentPath)
        {
            if (node == null) return;

            // Build the path for this node
            string nodeName = !string.IsNullOrEmpty(node.Name) ? node.Name : node.GlobalId;
            string currentPath = string.IsNullOrEmpty(parentPath) ? nodeName : $"{parentPath}/{nodeName}";

            // Add to lookup dictionaries
            _allSpatialElements[node.GlobalId] = node;
            _spatialStructure[node.GlobalId] = currentPath;

            // Map contained elements to this spatial container
            foreach (var elementGlobalId in node.ContainedElementGlobalIds)
            {
                if (node.IfcClass.Contains("Storey", StringComparison.OrdinalIgnoreCase))
                {
                    _storeyByProduct[elementGlobalId] = node;
                }
            }

            if (node.ContainedElementGlobalIds.Count > 0)
            {
                _spatialContainment[node.GlobalId] = node.ContainedElementGlobalIds;
            }

            // Recursively process children
            foreach (var child in node.Children)
            {
                BuildSpatialLookup(child, currentPath);
            }
        }

        private GameObject ProcessObjAndMatch(SemanticEnrichmentResult result)
        {
            Debug.Log("Processing source object: " + _settings.SourceObject.name);

            // Use the source object directly (it's already in the scene)
            var sourceRoot = _settings.SourceObject;

            // Process all mesh renderers in the source object
            var meshRenderers = sourceRoot.GetComponentsInChildren<MeshRenderer>(true);
            result.TotalObjElements = meshRenderers.Length;

            Debug.Log("Found " + meshRenderers.Length + " mesh elements in source object");

            foreach (var renderer in meshRenderers)
            {
                string meshName = renderer.gameObject.name;
                string elementId = _settings.ExtractElementId(meshName);

                // Ensure every mesh object has an IFCMetadata component
                var ifcMetadata = renderer.gameObject.GetComponent<IFCMetadata>();
                if (ifcMetadata == null)
                {
                    ifcMetadata = renderer.gameObject.AddComponent<IFCMetadata>();
                }

                if (string.IsNullOrEmpty(elementId))
                {
                    // No ID extracted - mark as unmatched but still add basic IFC metadata
                    result.UnmatchedObjIds.Add(meshName);
                    AddDefaultIFCMetadata(ifcMetadata, meshName, "NoID");
                    continue;
                }

                // Try to find matching IFC element
                IfcElementData matchedProduct = FindMatchingProduct(elementId);

                if (matchedProduct != null)
                {
                    // Transfer IFC metadata
                    TransferIFCMetadata(renderer.gameObject, matchedProduct, result);
                    result.MatchedElements++;
                    result.MatchedIds.Add(elementId);
                }
                else
                {
                    result.UnmatchedObjIds.Add(meshName + " (ID: " + elementId + ")");

                    // Add default IFC metadata for unmatched objects so they can still be organized
                    AddDefaultIFCMetadata(ifcMetadata, meshName, elementId);

                    if (_settings.TrackDataLoss)
                    {
                        result.Warnings.Add("No IFC match for: " + meshName + " with ID: " + elementId);
                    }
                }
            }

            result.UnmatchedObjElements = result.TotalObjElements - result.MatchedElements;
            return sourceRoot;
        }

        /// <summary>
        /// Add default IFC metadata for objects that couldn't be matched to IFC
        /// </summary>
        private void AddDefaultIFCMetadata(IFCMetadata ifcMetadata, string meshName, string elementId)
        {
            ifcMetadata.AddProperty("ElementName", meshName, IFCMetadata.Categories.Identity);
            ifcMetadata.AddProperty("ElementID", elementId, IFCMetadata.Categories.Identity);
            ifcMetadata.AddProperty("IFCType", "Unmatched", IFCMetadata.Categories.Type);
            ifcMetadata.AddProperty("Storey", "Unassigned", IFCMetadata.Categories.Location);
            ifcMetadata.AddProperty("SpatialPath", "Unassigned", IFCMetadata.Categories.Location);
            ifcMetadata.AddProperty("Discipline", "Unmatched", IFCMetadata.Categories.Type);
            ifcMetadata.AddProperty("Category", "Unmatched", IFCMetadata.Categories.Type);
            ifcMetadata.SetMatchStatus(false, "Not Found in IFC");
            ifcMetadata.FinalizeProperties();
        }

        private IfcElementData FindMatchingProduct(string elementId)
        {
            if (string.IsNullOrEmpty(elementId))
                return null;

            // 1. Try exact match by Tag first (most common for Revit exports)
            if (_ifcElementsByTag.TryGetValue(elementId, out var productByTag))
            {
                return productByTag;
            }

            // 2. Try GlobalId match (22-character base64 IFC GUID)
            if (_settings.FallbackToGlobalId)
            {
                if (_ifcElementsByGlobalId.TryGetValue(elementId, out var productByGlobalId))
                {
                    return productByGlobalId;
                }

                // Check if the elementId contains a GlobalId pattern
                var globalIdMatch = System.Text.RegularExpressions.Regex.Match(elementId, @"([0-9A-Za-z_$]{22})");
                if (globalIdMatch.Success)
                {
                    string possibleGlobalId = globalIdMatch.Groups[1].Value;
                    if (_ifcElementsByGlobalId.TryGetValue(possibleGlobalId, out var productByExtractedGlobalId))
                    {
                        return productByExtractedGlobalId;
                    }
                }
            }

            // 3. Try Name match
            if (_ifcElementsByName.TryGetValue(elementId, out var productByName))
            {
                return productByName;
            }

            // 4. Try partial/fuzzy match on Tag
            foreach (var kvp in _ifcElementsByTag)
            {
                if (kvp.Key.Contains(elementId) || elementId.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            // 5. Try partial match on Name with normalization
            string normalizedId = elementId.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            foreach (var kvp in _ifcElementsByName)
            {
                string normalizedName = kvp.Key.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
                if (normalizedName.Contains(normalizedId) || normalizedId.Contains(normalizedName))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private void TransferIFCMetadata(GameObject target, IfcElementData source, SemanticEnrichmentResult result)
        {
            // Get or add IFCMetadata component
            var ifcMetadata = target.GetComponent<IFCMetadata>();
            if (ifcMetadata == null)
            {
                ifcMetadata = target.AddComponent<IFCMetadata>();
            }

            int propertiesAdded = 0;

            // Identity properties from IfcElementData
            string tag = source.tag ?? "";
            ifcMetadata.AddIdentityProperties(
                source.globalId ?? "",
                source.name ?? "",
                source.description ?? "",
                tag
            );
            propertiesAdded += 4;

            // Type properties - use proper IFC class name
            string ifcType = source.ifcClass ?? "Unknown";
            ifcMetadata.AddTypeProperties(
                ifcType,
                source.objectType ?? "",
                SemanticEnrichmentSettings.GetDiscipline(ifcType),
                SemanticEnrichmentSettings.GetCategory(ifcType)
            );
            propertiesAdded += 4;

            // Get additional properties from Python bridge
            var properties = _ifcNavigator.GetElementProperties(source.globalId);
            if (properties != null && properties.Count > 0)
            {
                // Transfer property sets and other metadata from Python
                foreach (var kvp in properties)
                {
                    string key = kvp.Key;
                    string value = kvp.Value;

                    // Categorize properties based on key prefix
                    if (key.StartsWith("Pset_") || key.StartsWith("PropertySet_"))
                    {
                        var parts = key.Split('.');
                        string psetName = parts.Length > 1 ? parts[0] : "PropertySet";
                        string propName = parts.Length > 1 ? parts[1] : key;
                        ifcMetadata.AddPropertySetProperty(psetName, propName, value);
                        propertiesAdded++;
                        result.TotalPropertySetsTransferred++;
                    }
                    else if (key.StartsWith("Qto_") || key.StartsWith("Quantity_"))
                    {
                        var parts = key.Split('.');
                        string qsetName = parts.Length > 1 ? parts[0] : "Quantities";
                        string qtyName = parts.Length > 1 ? parts[1] : key;
                        ifcMetadata.AddQuantitySetProperty(qsetName, qtyName, value);
                        propertiesAdded++;
                        result.TotalQuantitySetsTransferred++;
                    }
                    else if (key.StartsWith("Material"))
                    {
                        ifcMetadata.AddMaterialProperty(key, value);
                        propertiesAdded++;
                        result.TotalMaterialsTransferred++;
                    }
                    else if (key.StartsWith("Classification"))
                    {
                        var parts = key.Split('.');
                        string classSystem = parts.Length > 1 ? parts[0] : "Classification";
                        string classProp = parts.Length > 1 ? parts[1] : "Code";
                        ifcMetadata.AddClassificationProperty(classSystem, classProp, value);
                        propertiesAdded++;
                    }
                    else if (key.StartsWith("Identity."))
                    {
                        // Already handled above
                    }
                    else if (key.StartsWith("Geometry."))
                    {
                        string geomProp = key.Substring(9);
                        ifcMetadata.AddGeometryProperty(geomProp, value);
                        propertiesAdded++;
                    }
                    else if (key.StartsWith("Relationship."))
                    {
                        var parts = key.Split('.');
                        string relType = parts.Length > 1 ? parts[1] : "Relationship";
                        string relProp = parts.Length > 2 ? parts[2] : "Value";
                        ifcMetadata.AddRelationshipProperty(relType, relProp, value);
                        propertiesAdded++;
                        result.TotalRelationshipsTransferred++;
                    }
                    else
                    {
                        // Generic property
                        ifcMetadata.AddProperty(key, value, IFCMetadata.Categories.Custom);
                        propertiesAdded++;
                    }
                }
            }

            // Spatial containment / Location
            string globalId = source.globalId;
            if (_storeyByProduct.TryGetValue(globalId, out var storey))
            {
                string storeyName = !string.IsNullOrEmpty(storey.Name) ? storey.Name : storey.GlobalId;
                string elevation = ""; // Elevation would come from properties

                string storeyGlobalId = storey.GlobalId;
                string spatialPath = storeyName;
                if (_spatialStructure.TryGetValue(storeyGlobalId, out var storeyPath))
                {
                    spatialPath = storeyPath;
                }

                ifcMetadata.AddLocationProperties(storeyName, elevation, spatialPath);
                propertiesAdded += 3;
            }
            else
            {
                ifcMetadata.AddLocationProperties("Unassigned", "", "Unassigned");
                propertiesAdded += 3;
            }

            // Set match status
            ifcMetadata.SetMatchStatus(true, "Matched");

            // Finalize to update stats
            ifcMetadata.FinalizeProperties();

            // Rename the GameObject to follow IFC naming convention
            target.name = FormatIfcElementName(source);

            result.TotalPropertiesTransferred += propertiesAdded;
        }

        /// <summary>
        /// Format an IFC element name: [IfcClass]:[Name]_[Tag/GlobalId]
        /// </summary>
        private string FormatIfcElementName(IfcElementData element)
        {
            if (element == null) return "Unknown";

            string ifcClass = element.ifcClass ?? "Unknown";
            string name = SanitizeGameObjectName(element.name);
            string identifier = !string.IsNullOrEmpty(element.tag) ? element.tag : element.globalId ?? "";

            if (!string.IsNullOrEmpty(name))
            {
                return $"{ifcClass}:{name}_{identifier}";
            }
            else
            {
                return $"{ifcClass}:_{identifier}";
            }
        }

        private void ReorganizeHierarchy(GameObject root, SemanticEnrichmentResult result)
        {
            if (_settings.HierarchyMode == HierarchyMode.KeepOriginal)
            {
                Debug.Log("Hierarchy mode is KeepOriginal - skipping reorganization");
                return;
            }

            Debug.Log("Reorganizing hierarchy with mode: " + _settings.HierarchyMode);

            // Clear counts before reorganization
            _spatialContainers.Clear();
            int nodesBeforeReorganize = _spatialContainers.Count;

            // Collect all mesh objects
            var meshObjects = root.GetComponentsInChildren<MeshRenderer>(true)
                .Select(r => r.gameObject)
                .ToList();

            Debug.Log("Found " + meshObjects.Count + " mesh objects to reorganize");
            result.ElementsReorganized = meshObjects.Count;

            // Create container based on mode
            switch (_settings.HierarchyMode)
            {
                case HierarchyMode.IFCSpatialStructure:
                    ReorganizeBySpatialStructure(root, meshObjects);
                    break;
                case HierarchyMode.ByDiscipline:
                    ReorganizeByDiscipline(root, meshObjects);
                    break;
                case HierarchyMode.ByElementType:
                    ReorganizeByElementType(root, meshObjects);
                    break;
                case HierarchyMode.ByStorey:
                    ReorganizeByStorey(root, meshObjects);
                    break;
                case HierarchyMode.ByCategory:
                    ReorganizeByCategory(root, meshObjects);
                    break;
                case HierarchyMode.Flat:
                    ReorganizeFlat(root, meshObjects);
                    break;
            }

            // Track hierarchy nodes created
            result.HierarchyNodesCreated = _spatialContainers.Count;

            // Clean up empty GameObjects left behind after moving elements
            CleanupEmptyObjects(root);

            Debug.Log(string.Format("Hierarchy reorganization complete: {0} containers created, {1} elements reorganized",
                result.HierarchyNodesCreated, result.ElementsReorganized));
        }

        private void ReorganizeBySpatialStructure(GameObject root, List<GameObject> meshObjects)
        {
            Debug.Log("Reorganizing by IFC Spatial Structure (Full Schema with Relationships)...");
            _spatialContainers.Clear();
            int movedCount = 0;
            int hostedCount = 0;

            // First, create the full IFC spatial hierarchy from the schema
            CreateFullSpatialHierarchy(root);

            // Build a lookup of mesh objects by GlobalId for relationship-based parenting
            var meshByGlobalId = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in meshObjects)
            {
                var ifcMetadata = obj.GetComponent<IFCMetadata>();
                if (ifcMetadata != null && !string.IsNullOrEmpty(ifcMetadata.GlobalId))
                {
                    meshByGlobalId[ifcMetadata.GlobalId] = obj;
                }
            }

            // First pass: Move all mesh objects into spatial containers
            foreach (var obj in meshObjects)
            {
                var ifcMetadata = obj.GetComponent<IFCMetadata>();
                string path = "Unassigned";

                if (ifcMetadata != null)
                {
                    string metaPath = ifcMetadata.SpatialPath;
                    if (!string.IsNullOrEmpty(metaPath) && metaPath != "Unassigned")
                    {
                        path = metaPath;
                    }
                }

                // Find or create the container
                if (_spatialContainers.TryGetValue(path, out var container))
                {
                    obj.transform.SetParent(container);
                }
                else
                {
                    var newContainer = GetOrCreateContainerWithMetadata(root.transform, path, _spatialContainers, null);
                    obj.transform.SetParent(newContainer);
                }
                movedCount++;
            }

            // Second pass: Parent doors/windows under their host elements using IfcRelFillsElement
            foreach (var obj in meshObjects)
            {
                var ifcMetadata = obj.GetComponent<IFCMetadata>();
                if (ifcMetadata == null || string.IsNullOrEmpty(ifcMetadata.GlobalId))
                    continue;

                string globalId = ifcMetadata.GlobalId;

                // Check if this element fills an opening (door/window in a wall)
                if (_fillingToOpening.TryGetValue(globalId, out var openingGlobalId))
                {
                    // Find the host of the opening
                    if (_openingToHost.TryGetValue(openingGlobalId, out var hostGlobalId))
                    {
                        // Find the host mesh object
                        if (meshByGlobalId.TryGetValue(hostGlobalId, out var hostObj))
                        {
                            obj.transform.SetParent(hostObj.transform);
                            hostedCount++;
                            continue;
                        }
                    }
                }

                // Check if this is an opening element - parent under host
                if (_openingToHost.TryGetValue(globalId, out var hostForOpening))
                {
                    if (meshByGlobalId.TryGetValue(hostForOpening, out var hostObj))
                    {
                        obj.transform.SetParent(hostObj.transform);
                        hostedCount++;
                    }
                }
            }

            Debug.Log(string.Format("Moved {0} objects into {1} containers, {2} elements parented under hosts",
                movedCount, _spatialContainers.Count, hostedCount));
        }

        /// <summary>
        /// Create the complete IFC spatial hierarchy including empty containers for spaces
        /// </summary>
        private void CreateFullSpatialHierarchy(GameObject root)
        {
            Debug.Log("Creating full IFC spatial hierarchy...");

            // Process all spatial elements and create containers with metadata
            foreach (var kvp in _spatialStructure)
            {
                string globalId = kvp.Key;
                string path = kvp.Value;

                // Get the IFC spatial element (now as SpatialNode)
                _allSpatialElements.TryGetValue(globalId, out var spatialElement);

                // Create the container with full IFC metadata
                var container = GetOrCreateContainerWithMetadata(root.transform, path, _spatialContainers, spatialElement);

                // If this is a spatial element, add its IFC metadata
                if (spatialElement != null && container != null)
                {
                    var ifcMetadata = container.GetComponent<IFCMetadata>();
                    if (ifcMetadata == null)
                    {
                        ifcMetadata = container.gameObject.AddComponent<IFCMetadata>();
                    }

                    // Transfer full metadata to the spatial container
                    TransferSpatialElementMetadata(ifcMetadata, spatialElement);
                }
            }

            Debug.Log(string.Format("Created {0} spatial containers with IFC schema naming", _spatialContainers.Count));
        }

        /// <summary>
        /// Transfer metadata to a spatial element container (Site, Building, Storey, Space)
        /// Uses SpatialNode from Python bridge instead of XBIM interfaces
        /// </summary>
        private void TransferSpatialElementMetadata(IFCMetadata ifcMetadata, SpatialNode spatialElement)
        {
            if (spatialElement == null) return;

            // Identity properties
            ifcMetadata.AddIdentityProperties(
                spatialElement.GlobalId ?? "",
                spatialElement.Name ?? "",
                "", // Description not available in SpatialNode
                null
            );

            // Get IFC type from the IfcClass property
            string ifcType = spatialElement.IfcClass ?? "IfcSpatialElement";

            ifcMetadata.AddTypeProperties(
                ifcType,
                "", // ObjectType
                "Spatial",
                GetSpatialCategory(ifcType)
            );

            // Spatial structure specific properties
            string elevation = "";

            // Get properties from Python bridge
            var properties = _ifcNavigator.GetElementProperties(spatialElement.GlobalId);
            if (properties != null)
            {
                // Extract elevation if available
                if (properties.TryGetValue("Elevation", out var elev))
                {
                    elevation = elev;
                }

                // Transfer all properties
                foreach (var kvp in properties)
                {
                    string key = kvp.Key;
                    string value = kvp.Value;

                    if (key.StartsWith("Pset_") || key.Contains("."))
                    {
                        var parts = key.Split('.');
                        string psetName = parts.Length > 1 ? parts[0] : "PropertySet";
                        string propName = parts.Length > 1 ? parts[1] : key;
                        ifcMetadata.AddPropertySetProperty(psetName, propName, value);
                    }
                    else
                    {
                        ifcMetadata.AddProperty(key, value, IFCMetadata.Categories.Custom);
                    }
                }
            }

            ifcMetadata.AddSpatialStructureProperties(
                ifcType,
                spatialElement.LongName ?? "",
                "", // CompositionType
                elevation,
                "", // GrossArea
                "" // NetArea
            );

            // Location properties
            if (_spatialStructure.TryGetValue(spatialElement.GlobalId, out var path))
            {
                ifcMetadata.AddLocationProperties(
                    GetStoreyNameFromPath(path),
                    elevation,
                    path
                );
            }

            // Contained elements
            if (spatialElement.ContainedElementGlobalIds != null && spatialElement.ContainedElementGlobalIds.Count > 0)
            {
                ifcMetadata.AddContainedElements(spatialElement.ContainedElementGlobalIds);
            }

            // Children (decomposition)
            if (spatialElement.Children != null && spatialElement.Children.Count > 0)
            {
                var childNames = spatialElement.Children
                    .Select(c => !string.IsNullOrEmpty(c.Name) ? c.Name : c.GlobalId)
                    .ToList();
                ifcMetadata.AddDecompositionProperties(string.Join(", ", childNames), "");
            }

            ifcMetadata.SetMatchStatus(true, "IFC Spatial Element");
            ifcMetadata.FinalizeProperties();
        }

        /// <summary>
        /// Format an IFC entity name following IFC schema convention: [IfcClass]:[Name]_[GlobalId]
        /// </summary>
        private string FormatIfcEntityName(string ifcClass, string name, string globalId)
        {
            // Sanitize name for Unity GameObject naming
            string safeName = SanitizeGameObjectName(name);

            // Format: [IfcClass]:[Name]_[GlobalId] or [IfcClass]:_[GlobalId] if no name
            if (string.IsNullOrEmpty(safeName))
            {
                return string.Format("{0}:_{1}", ifcClass, globalId);
            }
            return string.Format("{0}:{1}_{2}", ifcClass, safeName, globalId);
        }

        /// <summary>
        /// Sanitize a string for use as a Unity GameObject name
        /// </summary>
        private string SanitizeGameObjectName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";

            // Replace problematic characters
            var sanitized = name
                .Replace('/', '-')
                .Replace('\\', '-')
                .Replace(':', '-')
                .Replace('*', '_')
                .Replace('?', '_')
                .Replace('"', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace('|', '_');

            // Trim whitespace
            sanitized = sanitized.Trim();

            // Limit length to avoid overly long names
            if (sanitized.Length > 60)
            {
                sanitized = sanitized.Substring(0, 60);
            }

            return sanitized;
        }

        /// <summary>
        /// Safely format latitude/longitude values - works with all IFC versions
        /// IFC2x3 uses IfcCompoundPlaneAngleMeasure (list of integers)
        /// IFC4 may have different representations
        /// </summary>
        private string FormatLatLongSafe(object latLongValue)
        {
            if (latLongValue == null)
                return "";

            try
            {
                // Try to cast to IEnumerable<long> first (common interface)
                if (latLongValue is IEnumerable<long> longValues)
                {
                    return FormatLatLong(longValues);
                }

                // Try IEnumerable<int>
                if (latLongValue is IEnumerable<int> intValues)
                {
                    var longList = intValues.Select(i => (long)i);
                    return FormatLatLong(longList);
                }

                // Fallback: try to convert to string directly
                return latLongValue.ToString();
            }
            catch
            {
                return "";
            }
        }

        private string FormatLatLong(IEnumerable<long> values)
        {
            if (values == null) return "";
            var parts = values.ToList();
            if (parts.Count >= 3)
            {
                return string.Format("{0}° {1}' {2}\"", parts[0], parts[1], parts[2]);
            }
            return string.Join(", ", parts);
        }

        private string GetStoreyNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var parts = path.Split('/');
            return parts.Length > 0 ? parts[parts.Length - 1] : "";
        }

        private string GetSpatialCategory(string ifcType)
        {
            if (ifcType.Contains("Site")) return "Site";
            if (ifcType.Contains("Building") && !ifcType.Contains("Storey")) return "Building";
            if (ifcType.Contains("Storey")) return "Storey";
            if (ifcType.Contains("Space")) return "Space";
            return "Spatial";
        }

        /// <summary>
        /// Create container with IFCMetadata for spatial elements
        /// Uses SpatialNode instead of XBIM IIfcSpatialStructureElement
        /// </summary>
        private Transform GetOrCreateContainerWithMetadata(Transform root, string path, Dictionary<string, Transform> containers, SpatialNode spatialElement)
        {
            if (containers.TryGetValue(path, out var existing))
                return existing;

            // Create nested containers for paths with /
            var parts = path.Split('/');
            Transform current = root;

            string currentPath = "";
            foreach (var part in parts)
            {
                currentPath += (currentPath.Length > 0 ? "/" : "") + part;

                if (!containers.TryGetValue(currentPath, out var container))
                {
                    var go = new GameObject(part);
                    go.transform.SetParent(current);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;

                    // Add IFCMetadata component to the container
                    var ifcMetadata = go.AddComponent<IFCMetadata>();

                    // Try to find the spatial element for this path segment
                    var pathSpatialElement = FindSpatialElementByPath(currentPath);
                    if (pathSpatialElement != null)
                    {
                        TransferSpatialElementMetadata(ifcMetadata, pathSpatialElement);
                    }
                    else
                    {
                        // Add basic metadata for hierarchy containers
                        ifcMetadata.AddProperty("ContainerPath", currentPath, IFCMetadata.Categories.Location);
                        ifcMetadata.AddProperty("ContainerName", part, IFCMetadata.Categories.Identity);
                        ifcMetadata.SetMatchStatus(false, "Hierarchy Container");
                        ifcMetadata.FinalizeProperties();
                    }

                    container = go.transform;
                    containers[currentPath] = container;
                }

                current = container;
            }

            return current;
        }

        /// <summary>
        /// Find a spatial element by its path. Returns SpatialNode instead of XBIM interface.
        /// </summary>
        private SpatialNode FindSpatialElementByPath(string path)
        {
            foreach (var kvp in _spatialStructure)
            {
                if (kvp.Value == path && _allSpatialElements.TryGetValue(kvp.Key, out var element))
                {
                    return element;
                }
            }
            return null;
        }

        private void ReorganizeByDiscipline(GameObject root, List<GameObject> meshObjects)
        {
            Debug.Log("Reorganizing by Discipline...");
            var containers = new Dictionary<string, Transform>();
            int movedCount = 0;

            foreach (var obj in meshObjects)
            {
                var ifcMetadata = obj.GetComponent<IFCMetadata>();
                string discipline = "Other";

                if (ifcMetadata != null)
                {
                    string metaDiscipline = ifcMetadata.Discipline;
                    if (!string.IsNullOrEmpty(metaDiscipline))
                    {
                        discipline = metaDiscipline;
                    }
                }

                var container = GetOrCreateContainer(root.transform, discipline, containers);
                obj.transform.SetParent(container);
                movedCount++;
            }

            if (_settings.SortAlphabetically)
            {
                SortChildren(root.transform);
            }

            Debug.Log("Moved " + movedCount + " objects into " + containers.Count + " discipline containers");
        }

        private void ReorganizeByElementType(GameObject root, List<GameObject> meshObjects)
        {
            Debug.Log("Reorganizing by Element Type...");
            var containers = new Dictionary<string, Transform>();
            int movedCount = 0;

            foreach (var obj in meshObjects)
            {
                var ifcMetadata = obj.GetComponent<IFCMetadata>();
                string ifcType = "Unknown";

                if (ifcMetadata != null)
                {
                    string metaType = ifcMetadata.IFCType;
                    if (!string.IsNullOrEmpty(metaType))
                    {
                        ifcType = metaType;
                    }
                }

                var container = GetOrCreateContainer(root.transform, ifcType, containers);
                obj.transform.SetParent(container);
                movedCount++;
            }

            if (_settings.SortAlphabetically)
            {
                SortChildren(root.transform);
            }

            Debug.Log("Moved " + movedCount + " objects into " + containers.Count + " type containers");
        }

        private void ReorganizeByStorey(GameObject root, List<GameObject> meshObjects)
        {
            Debug.Log("Reorganizing by Storey...");
            var containers = new Dictionary<string, Transform>();
            int movedCount = 0;

            foreach (var obj in meshObjects)
            {
                var ifcMetadata = obj.GetComponent<IFCMetadata>();
                string storey = "Unassigned";

                if (ifcMetadata != null)
                {
                    string metaStorey = ifcMetadata.Storey;
                    if (!string.IsNullOrEmpty(metaStorey))
                    {
                        storey = metaStorey;
                    }
                }

                var container = GetOrCreateContainer(root.transform, storey, containers);
                obj.transform.SetParent(container);
                movedCount++;
            }

            Debug.Log("Moved " + movedCount + " objects into " + containers.Count + " storey containers");
        }

        private void ReorganizeByCategory(GameObject root, List<GameObject> meshObjects)
        {
            Debug.Log("Reorganizing by Category...");
            var containers = new Dictionary<string, Transform>();
            int movedCount = 0;

            foreach (var obj in meshObjects)
            {
                var ifcMetadata = obj.GetComponent<IFCMetadata>();
                string category = "Other";

                if (ifcMetadata != null)
                {
                    string metaCategory = ifcMetadata.Category;
                    if (!string.IsNullOrEmpty(metaCategory))
                    {
                        category = metaCategory;
                    }
                }

                var container = GetOrCreateContainer(root.transform, category, containers);
                obj.transform.SetParent(container);
                movedCount++;
            }

            if (_settings.SortAlphabetically)
            {
                SortChildren(root.transform);
            }

            Debug.Log("Moved " + movedCount + " objects into " + containers.Count + " category containers");
        }

        private void ReorganizeFlat(GameObject root, List<GameObject> meshObjects)
        {
            Debug.Log("Reorganizing to flat structure...");
            int movedCount = 0;

            foreach (var obj in meshObjects)
            {
                obj.transform.SetParent(root.transform);
                movedCount++;
            }

            if (_settings.SortAlphabetically)
            {
                SortChildren(root.transform);
            }

            Debug.Log("Moved " + movedCount + " objects to flat structure");
        }

        private Transform GetOrCreateContainer(Transform root, string path, Dictionary<string, Transform> containers)
        {
            if (containers.TryGetValue(path, out var existing))
                return existing;

            // Create nested containers for paths with /
            var parts = path.Split('/');
            Transform current = root;

            string currentPath = "";
            foreach (var part in parts)
            {
                currentPath += (currentPath.Length > 0 ? "/" : "") + part;

                if (!containers.TryGetValue(currentPath, out var container))
                {
                    var go = new GameObject(part);
                    go.transform.SetParent(current);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    container = go.transform;
                    containers[currentPath] = container;
                }

                current = container;
            }

            return current;
        }

        private void SortChildren(Transform parent)
        {
            var children = new List<Transform>();
            for (int i = 0; i < parent.childCount; i++)
            {
                children.Add(parent.GetChild(i));
            }

            children = children.OrderBy(c => c.name).ToList();

            for (int i = 0; i < children.Count; i++)
            {
                children[i].SetSiblingIndex(i);
            }
        }

        private void CleanupEmptyObjects(GameObject root)
        {
            if (!_settings.CreateEmptyContainers)
            {
                // Remove empty parent objects that have no children with renderers
                var allTransforms = root.GetComponentsInChildren<Transform>(true).ToList();
                allTransforms.Reverse(); // Process from deepest to root

                foreach (var t in allTransforms)
                {
                    if (t == root.transform) continue;

                    if (t.GetComponentsInChildren<MeshRenderer>(true).Length == 0 &&
                        t.GetComponentsInChildren<MeshFilter>(true).Length == 0)
                    {
                        UnityEngine.Object.DestroyImmediate(t.gameObject);
                    }
                }
            }
        }

        private void SaveOutput(GameObject root, SemanticEnrichmentResult result)
        {
#if UNITY_EDITOR
            // Ensure output folder exists
            if (!Directory.Exists(_settings.OutputFolder))
            {
                Directory.CreateDirectory(_settings.OutputFolder);
            }

            string fileName = _settings.SourceObject.name + "_Enriched";

            if (_settings.CreatePrefab)
            {
                string prefabPath = Path.Combine(_settings.OutputFolder, fileName + ".prefab");
                prefabPath = prefabPath.Replace("\\", "/");

                // Make path relative to Assets
                if (!prefabPath.StartsWith("Assets"))
                {
                    if (prefabPath.Contains("Assets"))
                    {
                        prefabPath = prefabPath.Substring(prefabPath.IndexOf("Assets"));
                    }
                }

                UnityEditor.AssetDatabase.Refresh();

                var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                result.OutputPath = prefabPath;
                Debug.Log("Saved enriched prefab to: " + prefabPath);
            }
            else
            {
                result.OutputPath = "Scene object: " + root.name;
            }

            UnityEditor.AssetDatabase.Refresh();
#else
            result.OutputPath = "Scene object: " + root.name;
#endif
        }
    }
}
