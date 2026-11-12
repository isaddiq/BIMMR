using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BIMUniXchange.SemanticEnrichment
{
    /// <summary>
    /// Editor window for IFC Semantic Enrichment
    /// </summary>
    public class IFCSemanticEnrichmentWindow : EditorWindow
    {
        private SemanticEnrichmentSettings _settings;
        private SemanticEnrichmentResult _lastResult;
        private Vector2 _scrollPosition;
        private Vector2 _resultScrollPosition;
        private Vector2 _logScrollPosition;
        private bool _showMatchingSettings = false;
        private bool _showMetadataSettings = true;
        private bool _showOutputSettings = false;
        private bool _showResults = true;
        private bool _showPerformanceLog = true;
        private bool _isProcessing = false;
        private string _performanceLog = "";
        private string _lastLogFilePath = "";

        [MenuItem("Window/BIMUniXchange/IFC Semantic Enrichment", false, 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<IFCSemanticEnrichmentWindow>("IFC Semantic Enrichment");
            window.minSize = new Vector2(450, 600);
            window.Show();
        }

        private void OnEnable()
        {
            if (_settings == null)
            {
                _settings = new SemanticEnrichmentSettings();
            }
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawHeader();
            EditorGUILayout.Space(10);

            DrawInputSection();
            EditorGUILayout.Space(10);

            DrawHierarchySection();
            EditorGUILayout.Space(10);

            DrawMatchingSection();
            EditorGUILayout.Space(5);

            DrawMetadataSection();
            EditorGUILayout.Space(5);

            DrawOutputSection();
            EditorGUILayout.Space(15);

            DrawActionButtons();
            EditorGUILayout.Space(10);

            DrawResultsSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.fontSize = 16;
            titleStyle.alignment = TextAnchor.MiddleCenter;

            EditorGUILayout.LabelField("IFC Semantic Enrichment", titleStyle);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField(
                "Match mesh elements with IFC metadata by Element ID.\n" +
                "Drag a GameObject from the Hierarchy. Mesh names should contain Element ID.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawInputSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Source GameObject (drag from hierarchy)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Source Object:", GUILayout.Width(90));
            _settings.SourceObject = (GameObject)EditorGUILayout.ObjectField(_settings.SourceObject, typeof(GameObject), true);
            EditorGUILayout.EndHorizontal();

            // Validate Source Object
            if (_settings.SourceObject == null)
            {
                EditorGUILayout.HelpBox("Drag a GameObject from the Hierarchy to enrich with IFC metadata.", MessageType.Info);
            }
            else
            {
                int meshCount = _settings.SourceObject.GetComponentsInChildren<MeshRenderer>(true).Length;
                string prefabStatus = "";
#if UNITY_EDITOR
                if (UnityEditor.PrefabUtility.IsPartOfAnyPrefab(_settings.SourceObject))
                {
                    prefabStatus = " (Prefab - will be unpacked)";
                }
#endif
                EditorGUILayout.HelpBox("Found " + meshCount + " mesh elements" + prefabStatus, MessageType.None);
            }

            // Unpack option
            _settings.UnpackPrefab = EditorGUILayout.Toggle("Unpack Prefab", _settings.UnpackPrefab);

            EditorGUILayout.Space(5);

            // IFC File - Drag and Drop
            EditorGUILayout.LabelField("IFC File", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Drag IFC:", GUILayout.Width(90));
            var newIfcAsset = EditorGUILayout.ObjectField(_settings.IfcFileAsset, typeof(UnityEngine.Object), false);
            if (newIfcAsset != _settings.IfcFileAsset)
            {
                _settings.IfcFileAsset = newIfcAsset;
                if (newIfcAsset != null)
                {
                    string assetPath = AssetDatabase.GetAssetPath(newIfcAsset);
                    if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".ifc", System.StringComparison.OrdinalIgnoreCase))
                    {
                        _settings.IfcFilePath = Path.GetFullPath(assetPath);
                    }
                    else
                    {
                        Debug.LogWarning("Please select an IFC file.");
                        _settings.IfcFileAsset = null;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // IFC File - Browse path
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Or Path:", GUILayout.Width(90));
            string newPath = EditorGUILayout.TextField(_settings.IfcFilePath);
            if (newPath != _settings.IfcFilePath)
            {
                _settings.IfcFilePath = newPath;
                _settings.IfcFileAsset = null; // Clear asset reference when manually typing path
            }
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("Select IFC File",
                    string.IsNullOrEmpty(_settings.IfcFilePath) ? Application.dataPath : Path.GetDirectoryName(_settings.IfcFilePath),
                    "ifc");
                if (!string.IsNullOrEmpty(path))
                {
                    _settings.IfcFilePath = path;
                    _settings.IfcFileAsset = null;
                }
            }
            EditorGUILayout.EndHorizontal();

            // Validate IFC
            if (string.IsNullOrEmpty(_settings.IfcFilePath))
            {
                EditorGUILayout.HelpBox("Drag an IFC file from Project or use Browse to select.", MessageType.Info);
            }
            else if (!File.Exists(_settings.IfcFilePath))
            {
                EditorGUILayout.HelpBox("IFC file not found!", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("IFC: " + Path.GetFileName(_settings.IfcFilePath), MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawHierarchySection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Hierarchy Organization", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            _settings.HierarchyMode = (HierarchyMode)EditorGUILayout.EnumPopup("Hierarchy Mode:", _settings.HierarchyMode);

            // Show description based on selected mode
            string description = GetHierarchyModeDescription(_settings.HierarchyMode);
            EditorGUILayout.HelpBox(description, MessageType.Info);

            EditorGUILayout.Space(3);
            _settings.CreateEmptyContainers = EditorGUILayout.Toggle("Create Empty Containers", _settings.CreateEmptyContainers);
            _settings.SortAlphabetically = EditorGUILayout.Toggle("Sort Alphabetically", _settings.SortAlphabetically);

            EditorGUILayout.EndVertical();
        }

        private string GetHierarchyModeDescription(HierarchyMode mode)
        {
            switch (mode)
            {
                case HierarchyMode.KeepOriginal:
                    return "Keep the original OBJ hierarchy structure unchanged.";
                case HierarchyMode.IFCSpatialStructure:
                    return "Organize by IFC spatial structure: Site > Building > Storey > Space";
                case HierarchyMode.ByDiscipline:
                    return "Organize by discipline: Architectural, Structural, Mechanical, Electrical, Plumbing, etc.";
                case HierarchyMode.ByElementType:
                    return "Organize by IFC element type: IfcWall, IfcDoor, IfcWindow, etc.";
                case HierarchyMode.ByStorey:
                    return "Organize by building storey only.";
                case HierarchyMode.ByCategory:
                    return "Organize by category: Walls, Doors, Windows, Floors, etc.";
                case HierarchyMode.Flat:
                    return "Flat structure - all elements as direct children of root.";
                default:
                    return "";
            }
        }

        private void DrawMatchingSection()
        {
            _showMatchingSettings = EditorGUILayout.Foldout(_showMatchingSettings, "Element ID Matching Settings", true);

            if (_showMatchingSettings)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("OBJ Mesh Name Pattern", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox(
                    "Expected format: ElementName<separator>ElementID\n" +
                    "Example: Wall_12345, Door-ABC123, Column:C01",
                    MessageType.None);

                EditorGUILayout.Space(5);
                _settings.IdSeparator = EditorGUILayout.TextField("ID Separator:", _settings.IdSeparator);
                _settings.IdPositionFromEnd = EditorGUILayout.IntField("ID Position (from end):", _settings.IdPositionFromEnd);

                EditorGUILayout.HelpBox(
                    "Position 0 = last part after separator\n" +
                    "Example: 'Wall_Type1_12345' with position 0 gives '12345'",
                    MessageType.None);

                EditorGUILayout.Space(5);
                _settings.CaseInsensitiveMatch = EditorGUILayout.Toggle("Case Insensitive Match", _settings.CaseInsensitiveMatch);
                _settings.FallbackToGlobalId = EditorGUILayout.Toggle("Fallback to GlobalId", _settings.FallbackToGlobalId);

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawMetadataSection()
        {
            _showMetadataSettings = EditorGUILayout.Foldout(_showMetadataSettings, "Metadata Import Options", true);

            if (_showMetadataSettings)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;

                _settings.ImportPropertySets = EditorGUILayout.Toggle("Import Property Sets (Psets)", _settings.ImportPropertySets);
                _settings.ImportQuantitySets = EditorGUILayout.Toggle("Import Quantity Sets (Qtos)", _settings.ImportQuantitySets);
                _settings.ImportMaterials = EditorGUILayout.Toggle("Import Material Info", _settings.ImportMaterials);
                _settings.ImportClassifications = EditorGUILayout.Toggle("Import Classifications", _settings.ImportClassifications);
                _settings.ImportRelationships = EditorGUILayout.Toggle("Import Relationships", _settings.ImportRelationships);

                EditorGUILayout.Space(5);
                _settings.TrackDataLoss = EditorGUILayout.Toggle("Track Unmatched Elements", _settings.TrackDataLoss);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Material Options", EditorStyles.miniBoldLabel);
                _settings.PreserveObjMaterials = EditorGUILayout.Toggle("Preserve OBJ Materials", _settings.PreserveObjMaterials);
                _settings.ApplyIfcColors = EditorGUILayout.Toggle("Apply IFC Colors", _settings.ApplyIfcColors);

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawOutputSection()
        {
            _showOutputSettings = EditorGUILayout.Foldout(_showOutputSettings, "Output Settings", true);

            if (_showOutputSettings)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Output Folder:", GUILayout.Width(90));
                _settings.OutputFolder = EditorGUILayout.TextField(_settings.OutputFolder);
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFolderPanel("Select Output Folder", _settings.OutputFolder, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        // Convert to relative path if within Assets
                        if (path.Contains("Assets"))
                        {
                            path = path.Substring(path.IndexOf("Assets"));
                        }
                        _settings.OutputFolder = path;
                    }
                }
                EditorGUILayout.EndHorizontal();

                _settings.CreatePrefab = EditorGUILayout.Toggle("Create Prefab", _settings.CreatePrefab);
                _settings.SaveMeshesAsSeparateAssets = EditorGUILayout.Toggle("Save Meshes as Assets", _settings.SaveMeshesAsSeparateAssets);

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Combined action
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isProcessing && CanProcess();

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.fixedHeight = 35;

            if (GUILayout.Button("Assign Metadata + Reorganize Hierarchy", buttonStyle))
            {
                ProcessEnrichment();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Or perform separately:", EditorStyles.miniLabel);

            // Separate actions
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !_isProcessing && CanProcess();
            if (GUILayout.Button("1. Assign Metadata Only", GUILayout.Height(30)))
            {
                ProcessMetadataOnly();
            }

            GUI.enabled = !_isProcessing && CanReorganize();
            if (GUILayout.Button("2. Reorganize Hierarchy Only", GUILayout.Height(30)))
            {
                ProcessHierarchyOnly();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Preview button
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isProcessing && _settings.SourceObject != null;
            if (GUILayout.Button("Preview ID Extraction", GUILayout.Height(25)))
            {
                PreviewIdExtraction();
            }
            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            if (_isProcessing)
            {
                EditorGUILayout.HelpBox("Processing... Please wait.", MessageType.Info);
            }

            if (!CanProcess() && !CanReorganize())
            {
                EditorGUILayout.HelpBox("Please assign a Source Object and select an IFC file to proceed.", MessageType.Warning);
            }
        }

        private bool CanProcess()
        {
            return _settings.SourceObject != null &&
                   !string.IsNullOrEmpty(_settings.IfcFilePath) &&
                   File.Exists(_settings.IfcFilePath);
        }

        private bool CanReorganize()
        {
            return _settings.SourceObject != null;
        }

        private void ProcessEnrichment()
        {
            _isProcessing = true;
            _performanceLog = "";

            try
            {
                var enricher = new IFCSemanticEnricher(_settings);
                _lastResult = enricher.Process();

                // Capture the performance summary
                if (_lastResult != null)
                {
                    _performanceLog = _lastResult.GetPerformanceSummary();
                }

                if (_lastResult.Success)
                {
                    EditorUtility.DisplayDialog("Enrichment Complete",
                        "Successfully enriched OBJ with IFC metadata!\n\n" +
                        "Matched: " + _lastResult.MatchedElements + " / " + _lastResult.TotalObjElements + " elements\n" +
                        "Match Rate: " + _lastResult.MatchPercentage.ToString("F1") + "%\n" +
                        "Properties Transferred: " + _lastResult.TotalPropertiesTransferred + "\n\n" +
                        "Output: " + _lastResult.OutputPath,
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Enrichment Failed",
                        "Failed to enrich OBJ:\n" + _lastResult.ErrorMessage,
                        "OK");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Enrichment error: " + ex.Message);
                EditorUtility.DisplayDialog("Error", "An error occurred: " + ex.Message, "OK");
            }
            finally
            {
                _isProcessing = false;
                Repaint();
            }
        }

        private void ProcessMetadataOnly()
        {
            _isProcessing = true;
            _performanceLog = "";

            try
            {
                var enricher = new IFCSemanticEnricher(_settings);
                _lastResult = enricher.AssignMetadataOnly();

                // Capture the performance summary and save to file
                if (_lastResult != null)
                {
                    _performanceLog = _lastResult.GetPerformanceSummary();
                    SaveLogToFile("IFC_MetadataAssignment", _performanceLog);
                }

                if (_lastResult.Success)
                {
                    EditorUtility.DisplayDialog("Metadata Assignment Complete",
                        "Successfully assigned IFC metadata!\n\n" +
                        "Matched: " + _lastResult.MatchedElements + " / " + _lastResult.TotalObjElements + " elements\n" +
                        "Match Rate: " + _lastResult.MatchPercentage.ToString("F1") + "%\n" +
                        "Properties Transferred: " + _lastResult.TotalPropertiesTransferred + "\n\n" +
                        "You can now reorganize the hierarchy separately.",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Metadata Assignment Failed",
                        "Failed to assign metadata:\n" + _lastResult.ErrorMessage,
                        "OK");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Metadata assignment error: " + ex.Message);
                EditorUtility.DisplayDialog("Error", "An error occurred: " + ex.Message, "OK");
            }
            finally
            {
                _isProcessing = false;
                Repaint();
            }
        }

        private void ProcessHierarchyOnly()
        {
            _isProcessing = true;
            _performanceLog = "";

            try
            {
                var enricher = new IFCSemanticEnricher(_settings);
                _lastResult = enricher.ReorganizeHierarchyOnly();

                // Capture the performance summary and save to file
                if (_lastResult != null)
                {
                    _performanceLog = _lastResult.GetPerformanceSummary();
                    SaveLogToFile("IFC_HierarchyReorganization", _performanceLog);
                }

                if (_lastResult.Success)
                {
                    EditorUtility.DisplayDialog("Hierarchy Reorganization Complete",
                        "Successfully reorganized hierarchy!\n\n" +
                        "Elements processed: " + _lastResult.TotalObjElements + "\n" +
                        "Elements with metadata: " + _lastResult.MatchedElements + "\n\n" +
                        "Output: " + _lastResult.OutputPath,
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Hierarchy Reorganization Failed",
                        "Failed to reorganize hierarchy:\n" + _lastResult.ErrorMessage,
                        "OK");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Hierarchy reorganization error: " + ex.Message);
                EditorUtility.DisplayDialog("Error", "An error occurred: " + ex.Message, "OK");
            }
            finally
            {
                _isProcessing = false;
                Repaint();
            }
        }

        private void PreviewIdExtraction()
        {
            if (_settings.SourceObject == null)
            {
                EditorUtility.DisplayDialog("Preview", "Please assign a Source Object first.", "OK");
                return;
            }

            // Get mesh names from the source object
            var meshRenderers = _settings.SourceObject.GetComponentsInChildren<MeshRenderer>(true);
            var meshNames = new System.Collections.Generic.List<string>();

            foreach (var renderer in meshRenderers)
            {
                meshNames.Add(renderer.gameObject.name);
                if (meshNames.Count >= 10) break;
            }

            if (meshNames.Count == 0)
            {
                EditorUtility.DisplayDialog("Preview", "No mesh objects found in the Source Object.", "OK");
                return;
            }

            string preview = "Sample ID Extractions:\n\n";
            foreach (var name in meshNames)
            {
                string id = _settings.ExtractElementId(name);
                preview += "'" + name + "' => ID: " + (id ?? "(none)") + "\n";
            }

            EditorUtility.DisplayDialog("ID Extraction Preview", preview, "OK");
        }

        private void DrawResultsSection()
        {
            if (_lastResult == null) return;

            _showResults = EditorGUILayout.Foldout(_showResults, "Last Enrichment Results", true);

            if (_showResults)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // Status
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Status:", GUILayout.Width(120));
                EditorGUILayout.LabelField(_lastResult.Success ? "SUCCESS" : "FAILED",
                    _lastResult.Success ? EditorStyles.boldLabel : EditorStyles.label);
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_lastResult.ErrorMessage))
                {
                    EditorGUILayout.HelpBox(_lastResult.ErrorMessage, MessageType.Error);
                }

                EditorGUILayout.Space(5);

                // Statistics
                EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);
                DrawStatRow("Total OBJ Elements:", _lastResult.TotalObjElements.ToString());
                DrawStatRow("Total IFC Elements:", _lastResult.TotalIfcElements.ToString());
                DrawStatRow("Matched Elements:", _lastResult.MatchedElements.ToString());
                DrawStatRow("Unmatched Elements:", _lastResult.UnmatchedObjElements.ToString());
                DrawStatRow("Match Rate:", _lastResult.MatchPercentage.ToString("F1") + "%");
                DrawStatRow("Properties Transferred:", _lastResult.TotalPropertiesTransferred.ToString());
                DrawStatRow("Processing Time:", _lastResult.ProcessingTimeSeconds.ToString("F2") + "s");
                DrawStatRow("Output:", _lastResult.OutputPath);

                EditorGUILayout.EndVertical();

                // Performance Log Section
                EditorGUILayout.Space(5);
                _showPerformanceLog = EditorGUILayout.Foldout(_showPerformanceLog, "Performance Summary Log", true);

                if (_showPerformanceLog && !string.IsNullOrEmpty(_performanceLog))
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    // Copy button
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Performance Metrics", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Copy to Clipboard", GUILayout.Width(120)))
                    {
                        GUIUtility.systemCopyBuffer = _performanceLog;
                        EditorUtility.DisplayDialog("Copied", "Performance summary copied to clipboard!", "OK");
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(3);

                    // Scrollable log area
                    _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition, GUILayout.Height(300));

                    GUIStyle logStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        wordWrap = false,
                        richText = false,
                        font = Font.CreateDynamicFontFromOSFont("Consolas", 11)
                    };

                    // Make the text selectable
                    EditorGUILayout.SelectableLabel(_performanceLog, logStyle,
                        GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true), GUILayout.MinHeight(280));

                    EditorGUILayout.EndScrollView();

                    EditorGUILayout.EndVertical();
                }

                // Warnings Section
                if (_lastResult.Warnings.Count > 0)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("Warnings (" + _lastResult.Warnings.Count + ")", EditorStyles.boldLabel);

                    _resultScrollPosition = EditorGUILayout.BeginScrollView(_resultScrollPosition, GUILayout.MaxHeight(150));

                    int maxWarnings = Mathf.Min(_lastResult.Warnings.Count, 50);
                    for (int i = 0; i < maxWarnings; i++)
                    {
                        EditorGUILayout.LabelField("• " + _lastResult.Warnings[i], EditorStyles.wordWrappedMiniLabel);
                    }

                    if (_lastResult.Warnings.Count > 50)
                    {
                        EditorGUILayout.LabelField("... and " + (_lastResult.Warnings.Count - 50) + " more warnings");
                    }

                    EditorGUILayout.EndScrollView();
                    EditorGUILayout.EndVertical();
                }

                // Unmatched IDs
                if (_lastResult.UnmatchedObjIds.Count > 0)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Export Unmatched IDs to Console"))
                    {
                        Debug.Log("=== Unmatched OBJ Elements ===");
                        foreach (var id in _lastResult.UnmatchedObjIds)
                        {
                            Debug.Log(id);
                        }
                        Debug.Log("=== End of Unmatched Elements ===");
                    }
                    if (GUILayout.Button("Copy Unmatched IDs"))
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("=== Unmatched OBJ Elements ===");
                        foreach (var id in _lastResult.UnmatchedObjIds)
                        {
                            sb.AppendLine(id);
                        }
                        sb.AppendLine("=== End of Unmatched Elements ===");
                        GUIUtility.systemCopyBuffer = sb.ToString();
                        EditorUtility.DisplayDialog("Copied", "Unmatched IDs copied to clipboard!", "OK");
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void DrawStatRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(150));
            EditorGUILayout.LabelField(value);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Save log and metrics to a text file
        /// </summary>
        private void SaveLogToFile(string processName, string logContent)
        {
            try
            {
                // Create logs directory in project
                string logsFolder = Path.Combine(Application.dataPath, "BIMUniXchange", "Logs");
                if (!Directory.Exists(logsFolder))
                {
                    Directory.CreateDirectory(logsFolder);
                }

                // Generate filename with timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{processName}_{timestamp}.txt";
                _lastLogFilePath = Path.Combine(logsFolder, fileName);

                // Build full log content
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Process: {processName}");
                sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Source Object: {(_settings?.SourceObject?.name ?? "N/A")}");
                sb.AppendLine($"IFC File: {(_settings?.IfcFilePath ?? "N/A")}");
                sb.AppendLine();
                sb.AppendLine(logContent);

                // Add unmatched elements if any
                if (_lastResult?.UnmatchedObjIds != null && _lastResult.UnmatchedObjIds.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("=== UNMATCHED ELEMENTS ===");
                    foreach (var id in _lastResult.UnmatchedObjIds)
                    {
                        sb.AppendLine(id);
                    }
                }

                // Add warnings if any
                if (_lastResult?.Warnings != null && _lastResult.Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("=== WARNINGS ===");
                    foreach (var warning in _lastResult.Warnings)
                    {
                        sb.AppendLine(warning);
                    }
                }

                File.WriteAllText(_lastLogFilePath, sb.ToString());
                Debug.Log($"[IFC Semantic Enrichment] Log saved to: {_lastLogFilePath}");

                // Refresh AssetDatabase to show new file
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[IFC Semantic Enrichment] Failed to save log file: {ex.Message}");
            }
        }
    }
}
