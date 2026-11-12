# Quick Start Guide - IFC Semantic Enrichment with IfcOpenShell

## Installation (5 Minutes)

### Step 1: Install Python

Download from https://www.python.org/downloads/ (Python 3.7 or newer)

### Step 2: Install IfcOpenShell

Open command prompt/terminal and run:

```bash
pip install ifcopenshell
```

### Step 3: Verify Installation

```bash
python -c "import ifcopenshell; print('IfcOpenShell installed successfully!')"
```

## Usage in Unity

### Quick Test

1. Open Unity project
2. Go to `Window → BIMUniXchange → IFC Semantic Enrichment`
3. Drag a GameObject with meshes into "Source Object"
4. Browse and select your IFC file
5. Click "Process Semantic Enrichment"

### Results

- ✅ Meshes matched with IFC metadata
- ✅ Properties assigned to GameObjects
- ✅ Hierarchy reorganized by IFC structure
- ✅ Prefab created in output folder

## Common Issues

### "Python not found"

- Add Python to your system PATH
- Or specify full path in code:
  ```csharp
  var navigator = new IfcModelNavigator("C:\\Python39\\python.exe");
  ```

### "IfcOpenShell not found"

- Run: `pip install ifcopenshell`
- Check: `python -m pip list | findstr ifcopenshell`

### "Script not found"

- Verify file exists: `Assets/BIMUniXchange/IFCSemanticEnrichment/PythonScripts/ifc_helper.py`
- Unity may need to reimport the file

## What's Changed from XBIM

| Feature      | XBIM (Old)       | IfcOpenShell (New)   |
| ------------ | ---------------- | -------------------- |
| Platform     | Windows only     | Windows/Mac/Linux    |
| Installation | Manual DLL setup | `pip install`        |
| IFC Support  | IFC2x3, IFC4     | IFC2x3, IFC4, IFC4.3 |
| Dependencies | .NET Framework   | Python 3.7+          |
| License      | CDDL             | LGPL 3.0             |

## Code Examples

### Load and Query IFC

```csharp
using BIMUniXchange.SemanticEnrichment;

var navigator = new IfcModelNavigator();
navigator.LoadModel("path/to/model.ifc");

// Get all elements
foreach (var elem in navigator.GetAllElements())
{
    Debug.Log($"{elem.Name}: {elem.IfcClass}");
}

// Get properties
var props = navigator.GetElementProperties(globalId);
Debug.Log($"Found {props.Count} properties");

navigator.Dispose();
```

### Use in Semantic Enrichment

```csharp
var settings = new SemanticEnrichmentSettings
{
    SourceObject = myGameObject,
    IfcFilePath = "C:/Models/building.ifc",
    HierarchyMode = HierarchyMode.IFCSpatialStructure
};

var enricher = new IFCSemanticEnricher(settings);
var result = enricher.Process();

if (result.Success)
{
    Debug.Log($"Matched {result.MatchedElements} elements");
}
```

## Performance Tips

1. **Reuse navigator instances** - don't create new ones repeatedly
2. **Cache results** - save element lookups for later use
3. **Test with small files first** - validate before processing large models

## Next Steps

- Read full documentation: `IfcOpenShell_Migration.md`
- Check examples in: `IFCSemanticEnrichmentWindow.cs`
- Join community: https://github.com/IfcOpenShell/IfcOpenShell/discussions

## Need Help?

- Check Python installation: `python --version`
- Check IfcOpenShell: `python -c "import ifcopenshell"`
- View Python output: Run script manually to see errors
  ```bash
  cd Assets/BIMUniXchange/IFCSemanticEnrichment/PythonScripts
  python ifc_helper.py get_all_elements "path/to/model.ifc"
  ```

---

**Quick Reference**: Save this file for easy access!
