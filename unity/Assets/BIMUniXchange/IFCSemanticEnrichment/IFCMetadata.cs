using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BIMUniXchange.SemanticEnrichment
{
    /// <summary>
    /// Serializable key-value pair for storing IFC property data.
    /// Mirrors the IfcProperty struct from IFCImporter for consistency.
    /// </summary>
    [Serializable]
    public struct IFCProperty
    {
        /// <summary>
        /// The property key in format "PsetName.PropertyName" or "Category.PropertyName".
        /// </summary>
        public string Key;

        /// <summary>
        /// The property value as a string.
        /// </summary>
        public string Value;

        /// <summary>
        /// Whether this property has no meaningful value.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value) ||
                              Value.Equals("<undefined>", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Creates a new IFC property.
        /// </summary>
        public IFCProperty(string key, string value)
        {
            Key = key ?? "";
            Value = value ?? "";
        }
    }

    /// <summary>
    /// Statistics for IFC metadata properties.
    /// </summary>
    [Serializable]
    public class IFCMetadataStats
    {
        [SerializeField] private int totalParameters;
        [SerializeField] private int emptyParameters;
        [SerializeField] private int filledParameters;
        [SerializeField] private float completionPercentage;

        public int TotalParameters { get => totalParameters; set => totalParameters = value; }
        public int EmptyParameters { get => emptyParameters; set => emptyParameters = value; }
        public int FilledParameters { get => filledParameters; set => filledParameters = value; }
        public float CompletionPercentage { get => completionPercentage; set => completionPercentage = value; }

        public void Reset()
        {
            totalParameters = 0;
            emptyParameters = 0;
            filledParameters = 0;
            completionPercentage = 0f;
        }

        public void Calculate(List<IFCProperty> properties)
        {
            totalParameters = properties.Count;
            emptyParameters = properties.Count(p => p.IsEmpty);
            filledParameters = totalParameters - emptyParameters;
            completionPercentage = totalParameters > 0 ? (filledParameters * 100f / totalParameters) : 0f;
        }
    }

    /// <summary>
    /// Category statistics for grouped IFC properties (for Editor display).
    /// </summary>
    [Serializable]
    public class IFCCategoryStats
    {
        public string CategoryName;
        public int TotalProperties;
        public int FilledProperties;
        public int EmptyProperties;
        public float CompletionPercentage;

        public IFCCategoryStats(string name)
        {
            CategoryName = name;
            TotalProperties = 0;
            FilledProperties = 0;
            EmptyProperties = 0;
            CompletionPercentage = 0f;
        }

        public void Calculate(List<IFCProperty> properties, string categoryPrefix)
        {
            var categoryProps = properties.Where(p => p.Key.StartsWith(categoryPrefix + ".", StringComparison.OrdinalIgnoreCase)).ToList();
            TotalProperties = categoryProps.Count;
            EmptyProperties = categoryProps.Count(p => p.IsEmpty);
            FilledProperties = TotalProperties - EmptyProperties;
            CompletionPercentage = TotalProperties > 0 ? (FilledProperties * 100f / TotalProperties) : 0f;
        }
    }

    /// <summary>
    /// MonoBehaviour component for storing IFC semantic enrichment data on GameObjects.
    /// Follows the same structure as IfcMetadataComponent from IFCImporter for consistency.
    /// This component handles semantic data, relationships, and attributes - not geometry.
    /// </summary>
    [AddComponentMenu("BIMUniXchange/IFC Semantic Metadata")]
    public class IFCMetadata : MonoBehaviour
    {
        #region Category Constants (for property key prefixes - matches IFCImporter style)

        /// <summary>
        /// Standard property key prefixes for organizing IFC properties.
        /// Properties use format "Category.PropertyName" (e.g., "Identity.GlobalId").
        /// </summary>
        public static class Categories
        {
            public const string Identity = "Identity";
            public const string Location = "Location";
            public const string Type = "Type";
            public const string Geometry = "Geometry";
            public const string PropertySets = "Pset";
            public const string QuantitySets = "Qto";
            public const string Materials = "Material";
            public const string Classifications = "Classification";
            public const string Relationships = "Relationships";
            public const string Custom = "Custom";
            public const string Unmatched = "Unmatched";
        }

        #endregion

        #region IFC Identification (matches IfcMetadataComponent)

        [Header("IFC Identification")]
        [Tooltip("The Element ID (e.g., Revit Element ID extracted from Tag or properties).")]
        public string ElementId;

        [Tooltip("The unique GlobalId (GUID) of the IFC element.")]
        public string GlobalId;

        [Tooltip("The IFC class type (e.g., IfcWallStandardCase, IfcBeam, IfcColumn).")]
        public string IfcClass;

        [Tooltip("The name of the element as defined in the IFC file.")]
        public string ElementName;

        [Tooltip("The description of the element as defined in the IFC file.")]
        public string Description;

        [Tooltip("The object type or predefined type of the element.")]
        public string ObjectType;

        #endregion

        #region IFC Hierarchy (matches IfcMetadataComponent)

        [Header("IFC Hierarchy")]
        [Tooltip("The IFC project this element belongs to.")]
        public string Project;

        [Tooltip("The IFC site this element belongs to.")]
        public string Site;

        [Tooltip("The IFC building this element belongs to.")]
        public string Building;

        [Tooltip("GlobalId of the spatial parent (e.g., site/building/storey).")]
        public string ParentGlobalId;

        [Tooltip("The building storey this element belongs to.")]
        public string Storey;

        [Tooltip("The full spatial path (Site > Building > Storey).")]
        public string SpatialPath;

        #endregion

        #region Classification & Discipline

        [Header("Classification")]
        [Tooltip("The discipline (Architectural, Structural, MEP, etc.).")]
        public string Discipline;

        [Tooltip("The category (Walls, Doors, Windows, etc.).")]
        public string Category;

        #endregion

        #region Material Information (matches IfcMetadataComponent)

        [Header("Material Information")]
        [Tooltip("The name of the material assigned to this element.")]
        public string MaterialName;

        [Tooltip("The color of the element as defined in the IFC file.")]
        public Color ElementColor = Color.white;

        [Tooltip("Additional material layers (for layered materials).")]
        public List<string> MaterialLayers = new List<string>();

        #endregion

        #region Relationships

        [Header("Relationships")]
        [Tooltip("Elements contained in this spatial element.")]
        public List<string> ContainedElements = new List<string>();

        [Tooltip("Elements this element is connected to.")]
        public List<string> ConnectedTo = new List<string>();

        [Tooltip("Elements connected to this element.")]
        public List<string> ConnectedFrom = new List<string>();

        [Tooltip("Openings in this element (for walls, slabs, etc.).")]
        public List<string> HasOpenings = new List<string>();

        [Tooltip("The opening this element fills (for doors, windows).")]
        public string FillsVoid;

        [Tooltip("Child elements (decomposition).")]
        public List<string> DecomposedBy = new List<string>();

        [Tooltip("Parent element (decomposition).")]
        public string Decomposes;

        [Tooltip("Groups this element belongs to.")]
        public List<string> AssignedToGroups = new List<string>();

        [Tooltip("Systems this element belongs to (MEP systems).")]
        public List<string> AssignedToSystems = new List<string>();

        [Tooltip("Spaces that bound this element.")]
        public List<string> BoundingSpaces = new List<string>();

        #endregion

        #region Properties (matches IfcMetadataComponent style)

        [Header("Properties")]
        [Tooltip("List of IFC properties from property sets. Keys use format 'PsetName.PropertyName'.")]
        [SerializeField] private List<IFCProperty> _properties = new List<IFCProperty>();

        #endregion

        #region Statistics & Status

        [Header("Statistics")]
        [SerializeField] private IFCMetadataStats _stats = new IFCMetadataStats();

        [Header("Status")]
        [SerializeField] private bool _hasIFCData = false;
        [SerializeField] private bool _isMatched = false;
        [SerializeField] private string _matchStatus = "Not Processed";

        #endregion

        #region Public Accessors

        /// <summary>
        /// List of all IFC properties from property sets and quantity sets.
        /// </summary>
        public List<IFCProperty> Properties => _properties;

        /// <summary>
        /// Statistics about property completion.
        /// </summary>
        public IFCMetadataStats Stats => _stats;

        /// <summary>
        /// Get statistics for all categories (computed on demand for Editor display).
        /// </summary>
        public List<IFCCategoryStats> CategoryStats
        {
            get
            {
                var stats = new List<IFCCategoryStats>();
                var categories = new[]
                {
                    Categories.Identity, Categories.Type, Categories.Location,
                    Categories.Geometry, Categories.PropertySets, Categories.QuantitySets,
                    Categories.Materials, Categories.Classifications, Categories.Relationships,
                    Categories.Custom, Categories.Unmatched
                };

                foreach (var category in categories)
                {
                    var catStats = new IFCCategoryStats(category);
                    catStats.Calculate(_properties, category);
                    if (catStats.TotalProperties > 0)
                        stats.Add(catStats);
                }
                return stats;
            }
        }

        /// <summary>
        /// Get statistics for a specific category.
        /// </summary>
        public IFCCategoryStats GetCategoryStats(string category)
        {
            var catStats = new IFCCategoryStats(category);
            catStats.Calculate(_properties, category);
            return catStats.TotalProperties > 0 ? catStats : null;
        }

        /// <summary>
        /// Whether this component has valid IFC data.
        /// </summary>
        public bool HasIFCData => _hasIFCData && !string.IsNullOrEmpty(GlobalId);

        /// <summary>
        /// Whether this element was successfully matched to an IFC element.
        /// </summary>
        public bool IsMatched => _isMatched;

        /// <summary>
        /// Status message describing the match result.
        /// </summary>
        public string MatchStatus => _matchStatus;

        #endregion

        #region Property Management (matches IfcMetadataComponent API)

        /// <summary>
        /// Gets a property value by key.
        /// </summary>
        /// <param name="key">The property key to look up (e.g., "Pset_WallCommon.IsExternal").</param>
        /// <returns>The property value, or null if not found.</returns>
        public string GetProperty(string key)
        {
            foreach (var prop in _properties)
            {
                if (prop.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return prop.Value;
            }
            return null;
        }

        /// <summary>
        /// Check if a property exists.
        /// </summary>
        public bool HasProperty(string key)
        {
            return _properties.Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all properties as a dictionary.
        /// </summary>
        public Dictionary<string, string> GetPropertiesAsDictionary()
        {
            var dict = new Dictionary<string, string>();
            foreach (var prop in _properties)
            {
                if (!dict.ContainsKey(prop.Key))
                    dict.Add(prop.Key, prop.Value);
            }
            return dict;
        }

        /// <summary>
        /// Sets properties from a dictionary.
        /// </summary>
        public void SetPropertiesFromDictionary(Dictionary<string, string> propertyDict)
        {
            _properties.Clear();
            if (propertyDict == null) return;

            foreach (var kvp in propertyDict)
            {
                _properties.Add(new IFCProperty(kvp.Key, kvp.Value));
            }
            _hasIFCData = true;
            UpdateStats();
            MarkDirty();
        }

        /// <summary>
        /// Add or update a property with the given key and value.
        /// </summary>
        /// <param name="key">Property key in format "PsetName.PropertyName".</param>
        /// <param name="value">Property value.</param>
        public void AddProperty(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;

            // Find and update existing property
            for (int i = 0; i < _properties.Count; i++)
            {
                if (_properties[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    _properties[i] = new IFCProperty(key, value);
                    _hasIFCData = true;
                    UpdateStats();
                    MarkDirty();
                    return;
                }
            }

            // Add new property
            _properties.Add(new IFCProperty(key, value));
            _hasIFCData = true;
            UpdateStats();
            MarkDirty();
        }

        /// <summary>
        /// Add or update a property with a category prefix (for backward compatibility).
        /// The key will be formatted as "category.key" following IFC naming convention.
        /// </summary>
        /// <param name="key">Property name.</param>
        /// <param name="value">Property value.</param>
        /// <param name="category">Category prefix (e.g., Categories.Identity).</param>
        public void AddProperty(string key, string value, string category)
        {
            if (string.IsNullOrEmpty(key)) return;

            // Format key with category prefix if not already prefixed
            string fullKey = key.Contains(".") ? key : $"{category}.{key}";
            AddProperty(fullKey, value);
        }

        /// <summary>
        /// Remove a specific property by key.
        /// </summary>
        public bool RemoveProperty(string key)
        {
            for (int i = 0; i < _properties.Count; i++)
            {
                if (_properties[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    _properties.RemoveAt(i);
                    UpdateStats();
                    MarkDirty();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get all property keys.
        /// </summary>
        public List<string> GetAllPropertyKeys()
        {
            return _properties.Select(p => p.Key).ToList();
        }

        /// <summary>
        /// Clear all properties.
        /// </summary>
        public void ClearProperties()
        {
            _properties.Clear();
            _stats.Reset();
            _hasIFCData = false;
            MarkDirty();
        }

        #endregion

        #region Identity & Hierarchy Setup

        /// <summary>
        /// Set the core IFC identification properties.
        /// </summary>
        public void SetIdentification(string globalId, string ifcClass, string elementName,
            string description = null, string objectType = null, string elementId = null)
        {
            GlobalId = globalId ?? "";
            IfcClass = ifcClass ?? "";
            ElementName = elementName ?? "";
            Description = description ?? "";
            ObjectType = objectType ?? "";
            ElementId = elementId ?? "";
            _hasIFCData = !string.IsNullOrEmpty(globalId);
            MarkDirty();
        }

        /// <summary>
        /// Set the IFC spatial hierarchy information.
        /// </summary>
        public void SetHierarchy(string project, string site, string building, string storey,
            string parentGlobalId = null, string spatialPath = null)
        {
            Project = project ?? "";
            Site = site ?? "";
            Building = building ?? "";
            Storey = storey ?? "";
            ParentGlobalId = parentGlobalId ?? "";
            SpatialPath = spatialPath ?? "";
            MarkDirty();
        }

        /// <summary>
        /// Set the classification information.
        /// </summary>
        public void SetClassification(string discipline, string category)
        {
            Discipline = discipline ?? "";
            Category = category ?? "";
            MarkDirty();
        }

        /// <summary>
        /// Set the material information.
        /// </summary>
        public void SetMaterial(string materialName, Color? color = null, List<string> layers = null)
        {
            MaterialName = materialName ?? "";
            if (color.HasValue)
                ElementColor = color.Value;
            if (layers != null)
                MaterialLayers = new List<string>(layers);
            MarkDirty();
        }

        #endregion

        #region Property Set & Quantity Set Operations

        /// <summary>
        /// Add a property from a PropertySet using IFC naming convention.
        /// </summary>
        /// <param name="psetName">Property set name (e.g., "Pset_WallCommon").</param>
        /// <param name="propertyName">Property name (e.g., "IsExternal").</param>
        /// <param name="value">Property value.</param>
        public void AddPropertySetProperty(string psetName, string propertyName, string value)
        {
            string key = $"{psetName}.{propertyName}";
            AddProperty(key, value);
        }

        /// <summary>
        /// Add a quantity from a QuantitySet using IFC naming convention.
        /// </summary>
        /// <param name="qsetName">Quantity set name (e.g., "Qto_WallBaseQuantities").</param>
        /// <param name="quantityName">Quantity name (e.g., "GrossVolume").</param>
        /// <param name="value">Quantity value.</param>
        public void AddQuantitySetProperty(string qsetName, string quantityName, string value)
        {
            string key = $"{qsetName}.{quantityName}";
            AddProperty(key, value);
        }

        /// <summary>
        /// Add a classification reference.
        /// </summary>
        /// <param name="classificationSystem">Classification system name (e.g., "Uniclass2015").</param>
        /// <param name="propertyName">Property name (e.g., "Code", "Description").</param>
        /// <param name="value">Classification value.</param>
        public void AddClassification(string classificationSystem, string propertyName, string value)
        {
            string key = $"Classification.{classificationSystem}.{propertyName}";
            AddProperty(key, value);
        }

        /// <summary>
        /// Get all properties from a specific property set.
        /// </summary>
        public Dictionary<string, string> GetPropertySet(string psetName)
        {
            var result = new Dictionary<string, string>();
            string prefix = psetName + ".";

            foreach (var prop in _properties)
            {
                if (prop.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string propertyName = prop.Key.Substring(prefix.Length);
                    result[propertyName] = prop.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// Get all property set names that have properties.
        /// </summary>
        public List<string> GetPropertySetNames()
        {
            var psetNames = new HashSet<string>();
            foreach (var prop in _properties)
            {
                int dotIndex = prop.Key.IndexOf('.');
                if (dotIndex > 0)
                {
                    string psetName = prop.Key.Substring(0, dotIndex);
                    psetNames.Add(psetName);
                }
            }
            return psetNames.ToList();
        }

        #endregion

        #region Relationship Management

        /// <summary>
        /// Set containment relationships (for spatial elements).
        /// </summary>
        public void SetContainment(List<string> containedElementIds)
        {
            ContainedElements = containedElementIds != null ? new List<string>(containedElementIds) : new List<string>();
            MarkDirty();
        }

        /// <summary>
        /// Set connection relationships.
        /// </summary>
        public void SetConnections(List<string> connectedToIds, List<string> connectedFromIds)
        {
            ConnectedTo = connectedToIds != null ? new List<string>(connectedToIds) : new List<string>();
            ConnectedFrom = connectedFromIds != null ? new List<string>(connectedFromIds) : new List<string>();
            MarkDirty();
        }

        /// <summary>
        /// Set void/opening relationships.
        /// </summary>
        public void SetVoidRelationships(List<string> openingIds, string fillsVoidId = null)
        {
            HasOpenings = openingIds != null ? new List<string>(openingIds) : new List<string>();
            FillsVoid = fillsVoidId ?? "";
            MarkDirty();
        }

        /// <summary>
        /// Set decomposition relationships.
        /// </summary>
        public void SetDecomposition(List<string> decomposedByIds, string decomposesId = null)
        {
            DecomposedBy = decomposedByIds != null ? new List<string>(decomposedByIds) : new List<string>();
            Decomposes = decomposesId ?? "";
            MarkDirty();
        }

        /// <summary>
        /// Set group assignments.
        /// </summary>
        public void SetGroupAssignments(List<string> groupNames)
        {
            AssignedToGroups = groupNames != null ? new List<string>(groupNames) : new List<string>();
            MarkDirty();
        }

        /// <summary>
        /// Set system assignments (for MEP elements).
        /// </summary>
        public void SetSystemAssignments(List<string> systemNames)
        {
            AssignedToSystems = systemNames != null ? new List<string>(systemNames) : new List<string>();
            MarkDirty();
        }

        /// <summary>
        /// Set bounding space relationships.
        /// </summary>
        public void SetBoundingSpaces(List<string> spaceIds)
        {
            BoundingSpaces = spaceIds != null ? new List<string>(spaceIds) : new List<string>();
            MarkDirty();
        }

        /// <summary>
        /// Check if this element has any relationships defined.
        /// </summary>
        public bool HasRelationships =>
            ContainedElements.Count > 0 ||
            ConnectedTo.Count > 0 ||
            ConnectedFrom.Count > 0 ||
            HasOpenings.Count > 0 ||
            !string.IsNullOrEmpty(FillsVoid) ||
            DecomposedBy.Count > 0 ||
            !string.IsNullOrEmpty(Decomposes) ||
            AssignedToGroups.Count > 0 ||
            AssignedToSystems.Count > 0 ||
            BoundingSpaces.Count > 0;

        #endregion

        #region Statistics

        /// <summary>
        /// Update property statistics.
        /// </summary>
        public void UpdateStats()
        {
            _stats.Calculate(_properties);
        }

        #endregion

        #region Match Status

        /// <summary>
        /// Set the match status (matched to IFC or not).
        /// </summary>
        public void SetMatchStatus(bool matched, string status = null)
        {
            _isMatched = matched;
            _matchStatus = status ?? (matched ? "Matched" : "Not Found");
            _hasIFCData = matched;
            MarkDirty();
        }

        #endregion

        #region Clear & Reset

        /// <summary>
        /// Clear all data from this component.
        /// </summary>
        public void ClearAll()
        {
            // Clear identification
            ElementId = "";
            GlobalId = "";
            IfcClass = "";
            ElementName = "";
            Description = "";
            ObjectType = "";

            // Clear hierarchy
            Project = "";
            Site = "";
            Building = "";
            ParentGlobalId = "";
            Storey = "";
            SpatialPath = "";

            // Clear classification
            Discipline = "";
            Category = "";

            // Clear material
            MaterialName = "";
            ElementColor = Color.white;
            MaterialLayers.Clear();

            // Clear relationships
            ContainedElements.Clear();
            ConnectedTo.Clear();
            ConnectedFrom.Clear();
            HasOpenings.Clear();
            FillsVoid = "";
            DecomposedBy.Clear();
            Decomposes = "";
            AssignedToGroups.Clear();
            AssignedToSystems.Clear();
            BoundingSpaces.Clear();

            // Clear properties
            _properties.Clear();
            _stats.Reset();

            // Reset status
            _hasIFCData = false;
            _isMatched = false;
            _matchStatus = "Cleared";

            MarkDirty();
        }

        #endregion

        #region Summary & Export

        /// <summary>
        /// Get a formatted summary of the metadata.
        /// </summary>
        public string GetSummary()
        {
            return $"IFC: {IfcClass} - {ElementName} ({GlobalId})\n" +
                   $"Location: {SpatialPath}\n" +
                   $"Properties: {_stats.TotalParameters} ({_stats.FilledParameters} filled)\n" +
                   $"Relationships: {(HasRelationships ? "Yes" : "None")}\n" +
                   $"Status: {_matchStatus}";
        }

        /// <summary>
        /// Export all metadata to a dictionary (for JSON serialization, etc.).
        /// </summary>
        public Dictionary<string, object> ExportToDict()
        {
            var result = new Dictionary<string, object>
            {
                // Identification
                ["ElementId"] = ElementId,
                ["GlobalId"] = GlobalId,
                ["IfcClass"] = IfcClass,
                ["ElementName"] = ElementName,
                ["Description"] = Description,
                ["ObjectType"] = ObjectType,

                // Hierarchy
                ["Project"] = Project,
                ["Site"] = Site,
                ["Building"] = Building,
                ["Storey"] = Storey,
                ["ParentGlobalId"] = ParentGlobalId,
                ["SpatialPath"] = SpatialPath,

                // Classification
                ["Discipline"] = Discipline,
                ["Category"] = Category,

                // Material
                ["MaterialName"] = MaterialName,
                ["MaterialLayers"] = MaterialLayers,

                // Properties
                ["Properties"] = GetPropertiesAsDictionary(),

                // Relationships
                ["ContainedElements"] = ContainedElements,
                ["ConnectedTo"] = ConnectedTo,
                ["ConnectedFrom"] = ConnectedFrom,
                ["HasOpenings"] = HasOpenings,
                ["FillsVoid"] = FillsVoid,
                ["DecomposedBy"] = DecomposedBy,
                ["Decomposes"] = Decomposes,
                ["AssignedToGroups"] = AssignedToGroups,
                ["AssignedToSystems"] = AssignedToSystems,
                ["BoundingSpaces"] = BoundingSpaces
            };

            return result;
        }

        #endregion

        #region Editor Helpers

        private void MarkDirty()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Copy all data to another IFCMetadata component.
        /// </summary>
        public void CopyTo(IFCMetadata target)
        {
            if (target == null) return;

            // Copy identification
            target.ElementId = ElementId;
            target.GlobalId = GlobalId;
            target.IfcClass = IfcClass;
            target.ElementName = ElementName;
            target.Description = Description;
            target.ObjectType = ObjectType;

            // Copy hierarchy
            target.Project = Project;
            target.Site = Site;
            target.Building = Building;
            target.ParentGlobalId = ParentGlobalId;
            target.Storey = Storey;
            target.SpatialPath = SpatialPath;

            // Copy classification
            target.Discipline = Discipline;
            target.Category = Category;

            // Copy material
            target.MaterialName = MaterialName;
            target.ElementColor = ElementColor;
            target.MaterialLayers = new List<string>(MaterialLayers);

            // Copy relationships
            target.ContainedElements = new List<string>(ContainedElements);
            target.ConnectedTo = new List<string>(ConnectedTo);
            target.ConnectedFrom = new List<string>(ConnectedFrom);
            target.HasOpenings = new List<string>(HasOpenings);
            target.FillsVoid = FillsVoid;
            target.DecomposedBy = new List<string>(DecomposedBy);
            target.Decomposes = Decomposes;
            target.AssignedToGroups = new List<string>(AssignedToGroups);
            target.AssignedToSystems = new List<string>(AssignedToSystems);
            target.BoundingSpaces = new List<string>(BoundingSpaces);

            // Copy properties
            target._properties = new List<IFCProperty>(_properties);
            target._stats = new IFCMetadataStats();
            target.UpdateStats();

            // Copy status
            target._hasIFCData = _hasIFCData;
            target._isMatched = _isMatched;
            target._matchStatus = _matchStatus;

            target.MarkDirty();
        }

        #endregion

        #region Compatibility with legacy code and IfcMetadataComponent

        // Legacy compatibility methods
        public string GetValue(string key) => GetProperty(key);
        public bool HasKey(string key) => HasProperty(key);
        public void AddMetadataEntry(string key, string value) => AddProperty(key, value);
        public Dictionary<string, string> ToDictionary() => GetPropertiesAsDictionary();

        // Legacy property accessors for old code using category-based approach
        public string IFCType => IfcClass;

        // Backward compatibility for old bulk operation methods
        public void AddIdentityProperties(string globalId, string name, string description, string tag = null)
        {
            GlobalId = globalId ?? "";
            ElementName = name ?? "";
            Description = description ?? "";
            ElementId = tag ?? "";
            _hasIFCData = !string.IsNullOrEmpty(globalId);
            MarkDirty();
        }

        public void AddTypeProperties(string ifcType, string objectType, string discipline, string category)
        {
            IfcClass = ifcType ?? "";
            ObjectType = objectType ?? "";
            Discipline = discipline ?? "";
            Category = category ?? "";
            MarkDirty();
        }

        public void AddLocationProperties(string storey, string elevation, string spatialPath)
        {
            Storey = storey ?? "";
            if (!string.IsNullOrEmpty(elevation))
                AddProperty("Location.StoreyElevation", elevation);
            SpatialPath = spatialPath ?? "";
            MarkDirty();
        }

        public void AddSpatialStructureProperties(string spatialType, string longName, string compositionType,
            string elevation = null, string grossArea = null, string netArea = null)
        {
            AddProperty("Spatial.Type", spatialType);
            if (!string.IsNullOrEmpty(longName))
                AddProperty("Identity.LongName", longName);
            if (!string.IsNullOrEmpty(compositionType))
                AddProperty("Type.CompositionType", compositionType);
            if (!string.IsNullOrEmpty(elevation))
                AddProperty("Location.Elevation", elevation);
            if (!string.IsNullOrEmpty(grossArea))
                AddProperty("Geometry.GrossArea", grossArea);
            if (!string.IsNullOrEmpty(netArea))
                AddProperty("Geometry.NetArea", netArea);
        }

        public void AddSpaceProperties(string interiorOrExterior, string boundedBy, string servicedBy)
        {
            if (!string.IsNullOrEmpty(interiorOrExterior))
                AddProperty("Space.InteriorOrExterior", interiorOrExterior);
            if (!string.IsNullOrEmpty(boundedBy))
                AddProperty("Relationships.BoundedBy", boundedBy);
            if (!string.IsNullOrEmpty(servicedBy))
                AddProperty("Relationships.ServicedBy", servicedBy);
        }

        public void AddContainedElements(List<string> elementIds)
        {
            if (elementIds != null)
            {
                ContainedElements = new List<string>(elementIds);
                MarkDirty();
            }
        }

        public void AddDecompositionProperties(string decomposedBy, string decomposes)
        {
            if (!string.IsNullOrEmpty(decomposedBy))
            {
                DecomposedBy = decomposedBy.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            Decomposes = decomposes ?? "";
            MarkDirty();
        }

        public void AddConnectionProperties(List<string> connectedTo, List<string> connectedFrom)
        {
            if (connectedTo != null)
                ConnectedTo = new List<string>(connectedTo);
            if (connectedFrom != null)
                ConnectedFrom = new List<string>(connectedFrom);
            MarkDirty();
        }

        public void AddVoidProperties(List<string> hasOpenings, string fillsVoid)
        {
            if (hasOpenings != null)
                HasOpenings = new List<string>(hasOpenings);
            FillsVoid = fillsVoid ?? "";
            MarkDirty();
        }

        public void AddMaterialProperty(string key, string value)
        {
            if (key.Equals("MaterialName", StringComparison.OrdinalIgnoreCase))
                MaterialName = value ?? "";
            else
                AddProperty($"Material.{key}", value);
        }

        public void AddClassificationProperty(string classificationSystem, string propertyName, string value)
        {
            AddClassification(classificationSystem, propertyName, value);
        }

        public void AddRelationshipProperty(string relationshipType, string propertyName, string value)
        {
            AddProperty($"Relationships.{relationshipType}.{propertyName}", value);
        }

        public void AddGeometryProperty(string key, string value)
        {
            AddProperty($"Geometry.{key}", value);
        }

        public void FinalizeProperties()
        {
            UpdateStats();
            MarkDirty();
        }

        // For category-based queries (legacy support)
        public List<IFCProperty> GetPropertiesByCategory(string category)
        {
            string prefix = category + ".";
            return _properties.Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public List<string> GetAllKeys() => GetAllPropertyKeys();
        public List<string> GetCategories() => GetPropertySetNames();

        #endregion
    }
}
