using UnityEngine;
using UnityEditor;
using System.Linq;

namespace BIMUniXchange.SemanticEnrichment
{
    /// <summary>
    /// Custom editor for IFCMetadata component - displays properties by category with statistics
    /// </summary>
    [CustomEditor(typeof(IFCMetadata))]
    public class IFCMetadataEditor : Editor
    {
        private bool _showStatistics = true;
        private bool _showCategoryStats = true;
        private bool _showIdentity = true;
        private bool _showType = true;
        private bool _showLocation = true;
        private bool _showGeometry = false;
        private bool _showPropertySets = false;
        private bool _showQuantitySets = false;
        private bool _showMaterials = true;
        private bool _showClassifications = true;
        private bool _showRelationships = true;
        private bool _showCustom = false;
        private bool _showUnmatched = false;
        private bool _showAllProperties = false;

        private GUIStyle _headerStyle;
        private GUIStyle _statsBoxStyle;
        private GUIStyle _categoryBoxStyle;
        private GUIStyle _matchedStyle;
        private GUIStyle _unmatchedStyle;

        public override void OnInspectorGUI()
        {
            IFCMetadata ifcMetadata = (IFCMetadata)target;

            InitStyles();

            // Header with match status
            DrawHeader(ifcMetadata);

            EditorGUILayout.Space(5);

            // Overall Statistics
            DrawStatistics(ifcMetadata);

            EditorGUILayout.Space(5);

            // Category Statistics
            DrawCategoryStatistics(ifcMetadata);

            EditorGUILayout.Space(5);

            // Properties by category
            DrawPropertiesByCategory(ifcMetadata);

            EditorGUILayout.Space(5);

            // Show all properties option
            DrawAllProperties(ifcMetadata);

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }

        private void InitStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter
                };

                _statsBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 10, 10)
                };

                _categoryBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(5, 5, 5, 5)
                };

                _matchedStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = new Color(0.2f, 0.8f, 0.2f) }
                };

                _unmatchedStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = new Color(0.9f, 0.5f, 0.2f) }
                };
            }
        }

        private void DrawHeader(IFCMetadata ifcMetadata)
        {
            EditorGUILayout.BeginVertical(_statsBoxStyle);

            EditorGUILayout.LabelField("IFC Semantic Data", _headerStyle);

            EditorGUILayout.Space(5);

            // Match status
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Match Status:", GUILayout.Width(100));
            if (ifcMetadata.IsMatched)
            {
                EditorGUILayout.LabelField("✓ " + ifcMetadata.MatchStatus, _matchedStyle);
            }
            else
            {
                EditorGUILayout.LabelField("✗ " + ifcMetadata.MatchStatus, _unmatchedStyle);
            }
            EditorGUILayout.EndHorizontal();

            // Quick info
            if (ifcMetadata.HasIFCData)
            {
                EditorGUILayout.LabelField("GlobalId: " + ifcMetadata.GlobalId);
                EditorGUILayout.LabelField("Name: " + ifcMetadata.ElementName);
                EditorGUILayout.LabelField("Type: " + ifcMetadata.IFCType);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStatistics(IFCMetadata ifcMetadata)
        {
            _showStatistics = EditorGUILayout.Foldout(_showStatistics, "Overall Statistics", true);
            if (_showStatistics)
            {
                EditorGUILayout.BeginVertical(_statsBoxStyle);

                var stats = ifcMetadata.Stats;

                // Progress bar for completion
                EditorGUILayout.LabelField("Completion", EditorStyles.boldLabel);
                Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
                float progress = stats.CompletionPercentage / 100f;
                EditorGUI.ProgressBar(progressRect, progress, $"{stats.CompletionPercentage:F1}% Complete");

                EditorGUILayout.Space(5);

                // Stats grid
                EditorGUILayout.BeginHorizontal();
                DrawStatBox("Total", stats.TotalParameters.ToString(), Color.white);
                DrawStatBox("Filled", stats.FilledParameters.ToString(), new Color(0.2f, 0.8f, 0.2f));
                DrawStatBox("Empty", stats.EmptyParameters.ToString(), new Color(0.9f, 0.5f, 0.2f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawStatBox(string label, string value, Color color)
        {
            EditorGUILayout.BeginVertical(_categoryBoxStyle, GUILayout.Width(80));
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color }
            };
            EditorGUILayout.LabelField(value, style);
            EditorGUILayout.LabelField(label, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });
            EditorGUILayout.EndVertical();
        }

        private void DrawCategoryStatistics(IFCMetadata ifcMetadata)
        {
            _showCategoryStats = EditorGUILayout.Foldout(_showCategoryStats, "Category Statistics", true);
            if (_showCategoryStats && ifcMetadata.CategoryStats.Count > 0)
            {
                EditorGUILayout.BeginVertical(_statsBoxStyle);

                foreach (var catStats in ifcMetadata.CategoryStats.OrderByDescending(c => c.TotalProperties))
                {
                    EditorGUILayout.BeginHorizontal();

                    // Category name
                    EditorGUILayout.LabelField(catStats.CategoryName, GUILayout.Width(120));

                    // Mini progress bar
                    Rect progressRect = EditorGUILayout.GetControlRect(false, 16, GUILayout.Width(100));
                    float progress = catStats.CompletionPercentage / 100f;
                    EditorGUI.ProgressBar(progressRect, progress, "");

                    // Stats
                    EditorGUILayout.LabelField($"{catStats.FilledProperties}/{catStats.TotalProperties}", GUILayout.Width(60));
                    EditorGUILayout.LabelField($"{catStats.CompletionPercentage:F0}%", GUILayout.Width(40));

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawPropertiesByCategory(IFCMetadata ifcMetadata)
        {
            EditorGUILayout.LabelField("Properties by Category", EditorStyles.boldLabel);

            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Identity, ref _showIdentity, "Identity");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Type, ref _showType, "Type");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Location, ref _showLocation, "Location");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Geometry, ref _showGeometry, "Geometry");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.PropertySets, ref _showPropertySets, "Property Sets");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.QuantitySets, ref _showQuantitySets, "Quantity Sets");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Materials, ref _showMaterials, "Materials");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Classifications, ref _showClassifications, "Classifications");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Relationships, ref _showRelationships, "Relationships");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Custom, ref _showCustom, "Custom");
            DrawCategorySection(ifcMetadata, IFCMetadata.Categories.Unmatched, ref _showUnmatched, "Unmatched");
        }

        private void DrawCategorySection(IFCMetadata ifcMetadata, string category, ref bool foldout, string displayName)
        {
            var properties = ifcMetadata.GetPropertiesByCategory(category);
            if (properties.Count == 0) return;

            var catStats = ifcMetadata.GetCategoryStats(category);
            string header = $"{displayName} ({properties.Count})";
            if (catStats != null)
            {
                header += $" - {catStats.CompletionPercentage:F0}%";
            }

            foldout = EditorGUILayout.Foldout(foldout, header, true);
            if (foldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(_categoryBoxStyle);

                foreach (var prop in properties)
                {
                    EditorGUILayout.BeginHorizontal();

                    // Key
                    EditorGUILayout.LabelField(prop.Key, GUILayout.Width(200));

                    // Value with color coding for empty
                    if (prop.IsEmpty)
                    {
                        var emptyStyle = new GUIStyle(EditorStyles.label)
                        {
                            fontStyle = FontStyle.Italic,
                            normal = { textColor = Color.gray }
                        };
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(prop.Value) ? "(empty)" : prop.Value, emptyStyle);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(prop.Value);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        private void DrawAllProperties(IFCMetadata ifcMetadata)
        {
            _showAllProperties = EditorGUILayout.Foldout(_showAllProperties, $"All Properties ({ifcMetadata.Properties.Count})", true);
            if (_showAllProperties)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(_categoryBoxStyle);

                foreach (var prop in ifcMetadata.Properties.OrderBy(p => GetCategoryFromKey(p.Key)).ThenBy(p => p.Key))
                {
                    EditorGUILayout.BeginHorizontal();

                    // Category badge (extracted from key prefix)
                    string category = GetCategoryFromKey(prop.Key);
                    EditorGUILayout.LabelField($"[{category}]", EditorStyles.miniLabel, GUILayout.Width(100));

                    // Key (show property name without category prefix)
                    string displayKey = GetPropertyNameFromKey(prop.Key);
                    EditorGUILayout.LabelField(displayKey, GUILayout.Width(180));

                    // Value
                    if (prop.IsEmpty)
                    {
                        var emptyStyle = new GUIStyle(EditorStyles.label)
                        {
                            fontStyle = FontStyle.Italic,
                            normal = { textColor = Color.gray }
                        };
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(prop.Value) ? "(empty)" : prop.Value, emptyStyle);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(prop.Value);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// Extract category prefix from a property key (e.g., "Identity.GlobalId" -> "Identity")
        /// </summary>
        private string GetCategoryFromKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "Unknown";
            int dotIndex = key.IndexOf('.');
            return dotIndex > 0 ? key.Substring(0, dotIndex) : "Custom";
        }

        /// <summary>
        /// Extract property name from a property key (e.g., "Identity.GlobalId" -> "GlobalId")
        /// </summary>
        private string GetPropertyNameFromKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            int dotIndex = key.IndexOf('.');
            return dotIndex > 0 && dotIndex < key.Length - 1 ? key.Substring(dotIndex + 1) : key;
        }
    }
}
