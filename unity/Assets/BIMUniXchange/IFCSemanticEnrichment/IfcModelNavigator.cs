using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BIMUniXchange.SemanticEnrichment
{
    /// <summary>
    /// Spatial hierarchy pattern detected in the IFC model.
    /// </summary>
    public enum SpatialHierarchyPattern
    {
        /// <summary>
        /// IFC 4.3 infrastructure style: IfcProject -> IfcFacility -> IfcFacilityPart (nested) -> Elements
        /// </summary>
        FacilityBased,

        /// <summary>
        /// Traditional building style: IfcProject -> IfcSite -> IfcBuilding -> IfcBuildingStorey -> IfcSpace -> Elements
        /// </summary>
        BuildingBased,

        /// <summary>
        /// Minimal: IfcProject -> IfcSite -> Elements (no building)
        /// </summary>
        SiteOnly,

        /// <summary>
        /// Direct: IfcProject -> IfcBuilding -> Elements (no site)
        /// </summary>
        BuildingDirect,

        /// <summary>
        /// Unknown or empty spatial structure
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Represents a spatial element in the IFC hierarchy.
    /// </summary>
    public class SpatialNode
    {
        public string GlobalId { get; set; }
        public string Name { get; set; }
        public string LongName { get; set; }
        public string IfcClass { get; set; }
        public SpatialNode Parent { get; set; }
        public List<SpatialNode> Children { get; set; } = new List<SpatialNode>();
        public List<string> ContainedElementGlobalIds { get; set; } = new List<string>();

        /// <summary>
        /// Gets the display name for this spatial node.
        /// </summary>
        public string DisplayName
        {
            get
            {
                string displayName = !string.IsNullOrEmpty(Name) ? Name :
                                    !string.IsNullOrEmpty(LongName) ? LongName : GlobalId;
                return $"{IfcClass}: {displayName}";
            }
        }
    }

    /// <summary>
    /// Represents an IFC element with its spatial containment info.
    /// </summary>
    public class ElementInfo
    {
        public string GlobalId { get; set; }
        public string Tag { get; set; }
        public string Name { get; set; }
        public string IfcClass { get; set; }
        public string SpatialContainerGlobalId { get; set; }
        public IfcElementData ElementData { get; set; }
    }

    /// <summary>
    /// Helper class for navigating IFC models using IfcOpenShell through Python.
    /// Provides methods to query spatial structure, relationships, and elements.
    /// </summary>
    public class IfcModelNavigator : IDisposable
    {
        private IfcPythonBridge _bridge;
        private bool _disposed;
        private string _filePath;

        // Cached spatial data
        private SpatialNode _projectNode;
        private Dictionary<string, SpatialNode> _spatialNodesByGlobalId;
        private List<IfcElementData> _allElements;
        private Dictionary<string, IfcElementData> _elementsByGlobalId;
        private Dictionary<string, List<IfcElementData>> _elementsByTag;
        private Dictionary<string, string> _spatialContainment; // Element GlobalId -> Spatial Container GlobalId

        public bool IsLoaded => _bridge != null && _bridge.IsModelLoaded;
        public string FilePath => _filePath;

        /// <summary>
        /// Initialize the navigator with optional Python executable path.
        /// </summary>
        public IfcModelNavigator(string pythonExecutable = "python")
        {
            _bridge = new IfcPythonBridge(pythonExecutable);
            _spatialNodesByGlobalId = new Dictionary<string, SpatialNode>(StringComparer.OrdinalIgnoreCase);
            _allElements = new List<IfcElementData>();
            _elementsByGlobalId = new Dictionary<string, IfcElementData>(StringComparer.OrdinalIgnoreCase);
            _elementsByTag = new Dictionary<string, List<IfcElementData>>(StringComparer.OrdinalIgnoreCase);
            _spatialContainment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Loads an IFC model from the specified file path.
        /// </summary>
        /// <param name="ifcPath">Full path to the IFC file.</param>
        /// <returns>True if loaded successfully, false otherwise.</returns>
        public bool LoadModel(string ifcPath)
        {
            try
            {
                if (!File.Exists(ifcPath))
                {
                    Debug.LogError($"IFC file not found: {ifcPath}");
                    return false;
                }

                _filePath = ifcPath;
                Debug.Log($"Loading IFC model: {ifcPath}");

                // Load the model through Python bridge
                var loadResult = _bridge.LoadModel(ifcPath);

                if (!loadResult.Success)
                {
                    Debug.LogError($"Failed to load IFC model: {loadResult.ErrorMessage}");
                    return false;
                }

                Debug.Log($"IFC model loaded successfully. Schema: {_bridge.Schema}, Elements: {_bridge.ElementCount}");

                // Build caches
                BuildElementCaches();
                BuildSpatialStructure();

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading IFC model: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Gets the IFC schema version of the loaded model.
        /// </summary>
        public string GetSchemaVersion()
        {
            return _bridge?.Schema ?? "Unknown";
        }

        #region Spatial Element Queries

        /// <summary>
        /// Gets the IFC project name.
        /// </summary>
        public string GetProjectName()
        {
            return _projectNode?.Name ?? "Unknown Project";
        }

        /// <summary>
        /// Gets the project GlobalId.
        /// </summary>
        public string GetProjectGlobalId()
        {
            return _projectNode?.GlobalId ?? "";
        }

        /// <summary>
        /// Gets the complete spatial structure tree.
        /// </summary>
        public SpatialNode GetSpatialTree()
        {
            return _projectNode;
        }

        /// <summary>
        /// Detects the spatial hierarchy pattern used in this IFC model.
        /// </summary>
        public SpatialHierarchyPattern DetectSpatialPattern()
        {
            if (_projectNode == null || _projectNode.Children.Count == 0)
            {
                return SpatialHierarchyPattern.Unknown;
            }

            // Check for facility-based pattern
            bool hasFacilities = _projectNode.Children.Any(c => 
                c.IfcClass.Contains("IfcFacility", StringComparison.OrdinalIgnoreCase));

            if (hasFacilities)
            {
                return SpatialHierarchyPattern.FacilityBased;
            }

            // Check for site and building presence
            bool hasSites = _projectNode.Children.Any(c => 
                c.IfcClass.Equals("IfcSite", StringComparison.OrdinalIgnoreCase));
            
            bool hasBuildings = false;
            if (hasSites)
            {
                hasBuildings = _projectNode.Children
                    .Where(c => c.IfcClass.Equals("IfcSite", StringComparison.OrdinalIgnoreCase))
                    .Any(site => site.Children.Any(child => 
                        child.IfcClass.Equals("IfcBuilding", StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                hasBuildings = _projectNode.Children.Any(c => 
                    c.IfcClass.Equals("IfcBuilding", StringComparison.OrdinalIgnoreCase));
            }

            if (hasSites && hasBuildings)
            {
                return SpatialHierarchyPattern.BuildingBased;
            }
            else if (hasSites && !hasBuildings)
            {
                return SpatialHierarchyPattern.SiteOnly;
            }
            else if (!hasSites && hasBuildings)
            {
                return SpatialHierarchyPattern.BuildingDirect;
            }

            return SpatialHierarchyPattern.Unknown;
        }

        /// <summary>
        /// Gets all sites in the model.
        /// </summary>
        public List<SpatialNode> GetSites()
        {
            var sites = new List<SpatialNode>();
            if (_projectNode != null)
            {
                sites.AddRange(_projectNode.Children.Where(c => 
                    c.IfcClass.Equals("IfcSite", StringComparison.OrdinalIgnoreCase)));
            }
            return sites;
        }

        /// <summary>
        /// Gets all buildings in the model.
        /// </summary>
        public List<SpatialNode> GetBuildings()
        {
            var buildings = new List<SpatialNode>();
            if (_projectNode != null)
            {
                // Direct children of project
                buildings.AddRange(_projectNode.Children.Where(c => 
                    c.IfcClass.Equals("IfcBuilding", StringComparison.OrdinalIgnoreCase)));
                
                // Children of sites
                foreach (var site in GetSites())
                {
                    buildings.AddRange(site.Children.Where(c => 
                        c.IfcClass.Equals("IfcBuilding", StringComparison.OrdinalIgnoreCase)));
                }
            }
            return buildings;
        }

        /// <summary>
        /// Gets all building storeys in the model.
        /// </summary>
        public List<SpatialNode> GetStoreys()
        {
            var storeys = new List<SpatialNode>();
            foreach (var building in GetBuildings())
            {
                storeys.AddRange(building.Children.Where(c => 
                    c.IfcClass.Equals("IfcBuildingStorey", StringComparison.OrdinalIgnoreCase)));
            }
            return storeys;
        }

        /// <summary>
        /// Gets all spaces in the model.
        /// </summary>
        public List<SpatialNode> GetSpaces()
        {
            var spaces = new List<SpatialNode>();
            foreach (var storey in GetStoreys())
            {
                spaces.AddRange(storey.Children.Where(c => 
                    c.IfcClass.Equals("IfcSpace", StringComparison.OrdinalIgnoreCase)));
            }
            return spaces;
        }

        #endregion

        #region Element Queries

        /// <summary>
        /// Gets all elements in the model.
        /// </summary>
        public IEnumerable<ElementInfo> GetAllElements()
        {
            foreach (var elementData in _allElements)
            {
                yield return new ElementInfo
                {
                    GlobalId = elementData.globalId,
                    Tag = elementData.tag,
                    Name = elementData.name,
                    IfcClass = elementData.ifcClass,
                    SpatialContainerGlobalId = GetSpatialContainer(elementData.globalId),
                    ElementData = elementData
                };
            }
        }

        /// <summary>
        /// Gets the spatial container GlobalId for an element.
        /// </summary>
        public string GetSpatialContainer(string elementGlobalId)
        {
            if (_spatialContainment.TryGetValue(elementGlobalId, out var containerGlobalId))
            {
                return containerGlobalId;
            }
            return null;
        }

        /// <summary>
        /// Gets all element GlobalIds contained in a spatial structure element.
        /// </summary>
        public List<string> GetContainedElements(string spatialGlobalId)
        {
            if (_spatialNodesByGlobalId.TryGetValue(spatialGlobalId, out var node))
            {
                return new List<string>(node.ContainedElementGlobalIds);
            }
            return new List<string>();
        }

        /// <summary>
        /// Finds an element by GlobalId.
        /// </summary>
        public IfcElementData GetElementByGlobalId(string globalId)
        {
            if (_elementsByGlobalId.TryGetValue(globalId, out var element))
            {
                return element;
            }
            return null;
        }

        /// <summary>
        /// Finds element(s) by Tag (Element ID).
        /// </summary>
        public IfcElementData GetElementByTag(string tag)
        {
            if (_elementsByTag.TryGetValue(tag, out var elements) && elements.Count > 0)
            {
                return elements[0];
            }
            return null;
        }

        /// <summary>
        /// Gets all properties for an element.
        /// </summary>
        public Dictionary<string, string> GetElementProperties(string globalId)
        {
            var result = _bridge.GetElementProperties(globalId);
            
            if (result.Success)
            {
                return result.Data;
            }
            
            Debug.LogWarning($"Failed to get properties for {globalId}: {result.ErrorMessage}");
            return new Dictionary<string, string>();
        }

        #endregion

        #region Relationship Queries

        /// <summary>
        /// Gets child nodes from a spatial node.
        /// </summary>
        public List<SpatialNode> GetSpatialChildren(string spatialGlobalId)
        {
            if (_spatialNodesByGlobalId.TryGetValue(spatialGlobalId, out var node))
            {
                return new List<SpatialNode>(node.Children);
            }
            return new List<SpatialNode>();
        }

        /// <summary>
        /// Gets parent node from a spatial node.
        /// </summary>
        public SpatialNode GetSpatialParent(string spatialGlobalId)
        {
            if (_spatialNodesByGlobalId.TryGetValue(spatialGlobalId, out var node))
            {
                return node.Parent;
            }
            return null;
        }

        #endregion

        #region Private Cache Building Methods

        private void BuildElementCaches()
        {
            _allElements.Clear();
            _elementsByGlobalId.Clear();
            _elementsByTag.Clear();

            var result = _bridge.GetAllElements();
            
            if (!result.Success)
            {
                Debug.LogError($"Failed to get all elements: {result.ErrorMessage}");
                return;
            }

            _allElements = result.Data ?? new List<IfcElementData>();

            foreach (var element in _allElements)
            {
                // Cache by GlobalId
                _elementsByGlobalId[element.globalId] = element;

                // Cache by Tag
                if (!string.IsNullOrEmpty(element.tag))
                {
                    if (!_elementsByTag.ContainsKey(element.tag))
                    {
                        _elementsByTag[element.tag] = new List<IfcElementData>();
                    }
                    _elementsByTag[element.tag].Add(element);
                }
            }

            Debug.Log($"Cached {_elementsByGlobalId.Count} elements, {_elementsByTag.Count} unique tags");
        }

        private void BuildSpatialStructure()
        {
            _spatialNodesByGlobalId.Clear();
            _spatialContainment.Clear();

            var result = _bridge.GetSpatialStructure();
            
            if (!result.Success)
            {
                Debug.LogError($"Failed to get spatial structure: {result.ErrorMessage}");
                return;
            }

            _projectNode = result.Data;

            // Build flattened lookup and containment mapping
            BuildSpatialNodeLookup(_projectNode);

            Debug.Log($"Built spatial structure with {_spatialNodesByGlobalId.Count} spatial nodes");
        }

        private void BuildSpatialNodeLookup(SpatialNode node)
        {
            if (node == null) return;

            _spatialNodesByGlobalId[node.GlobalId] = node;

            // Map contained elements to this spatial container
            foreach (var elementId in node.ContainedElementGlobalIds)
            {
                _spatialContainment[elementId] = node.GlobalId;
            }

            // Recursively process children
            foreach (var child in node.Children)
            {
                BuildSpatialNodeLookup(child);
            }
        }

        #endregion

        #region Model Statistics

        /// <summary>
        /// Gets a summary of the model contents.
        /// </summary>
        public string GetModelSummary()
        {
            if (!IsLoaded) return "No model loaded.";

            var pattern = DetectSpatialPattern();
            var sites = GetSites();
            var buildings = GetBuildings();
            var storeys = GetStoreys();
            var spaces = GetSpaces();

            return $"IFC Model Summary:\n" +
                   $"  Schema: {GetSchemaVersion()}\n" +
                   $"  Project: {GetProjectName()}\n" +
                   $"  Spatial Pattern: {pattern}\n" +
                   $"  Sites: {sites.Count}\n" +
                   $"  Buildings: {buildings.Count}\n" +
                   $"  Storeys: {storeys.Count}\n" +
                   $"  Spaces: {spaces.Count}\n" +
                   $"  Total Elements: {_elementsByGlobalId.Count}";
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _bridge?.Dispose();
                _bridge = null;

                _elementsByGlobalId?.Clear();
                _elementsByTag?.Clear();
                _spatialContainment?.Clear();
                _spatialNodesByGlobalId?.Clear();
                _allElements?.Clear();
            }

            _disposed = true;
        }

        ~IfcModelNavigator()
        {
            Dispose(false);
        }

        #endregion
    }
}
