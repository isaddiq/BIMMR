# 5.x IFC-Based Semantic Enrichment (Pipeline Subsection)

## ⚠️ IMPORTANT: Migration to IfcOpenShell

This module has been **migrated from XBIM Toolkit to IfcOpenShell** (Python-based).

**Quick Start**: See [QuickStart.md](QuickStart.md) for installation instructions.  
**Migration Details**: See [IfcOpenShell_Migration.md](IfcOpenShell_Migration.md) for complete documentation.

### Prerequisites

- Python 3.7 or newer
- IfcOpenShell Python package: `pip install ifcopenshell`

---

## Context and Objective

This subsection describes the semantic enrichment layer that augments imported IFC-derived assets with additional meaning, relationships, and derived attributes inside Unity. The goal is to bridge the gap between raw geometry/metadata (GlobalId, class, hierarchy) and higher-level operations such as querying, filtering, analytics, and downstream simulation. The process must remain aligned with IFC identifiers to preserve traceability back to the source model.

## Overview of the Enrichment Workflow

Semantic enrichment operates after IFC-to-mesh import. It consumes the metadata already attached to GameObjects (via `IfcMetadataComponent`) and applies additional annotations, categorizations, and computed properties. Figure 1 sketches the data path; Table 1 summarizes responsibilities and constraints.

```mermaid
flowchart LR
    IFC[IFC-derived scene<br/>(meshes + metadata)] --> ENR[Semantic Enrichment<br/>IFCMetadataEditor.cs]
    ENR --> TAG[Tags/Categories]
    ENR --> DER[Derived Attributes]
    ENR --> REL[Explicit Relations]
    TAG --> CON[Queries/Filters]
    DER --> CON
    REL --> CON
    CON --> APPS[Downstream apps<br/>(analysis, sim, UX)]
```

_Figure 1. Enrichment data flow from imported IFC scene to downstream uses._

| Aspect                  | Policy/Behavior                                                                  |
| ----------------------- | -------------------------------------------------------------------------------- |
| Identifier fidelity     | All enrichments keep GlobalId as the primary key for traceability                |
| Non-destructive updates | Enrichment adds/overlays data; imported geometry and base metadata remain intact |
| Schema flexibility      | Supports arbitrary key-value additions; does not enforce a fixed ontology        |
| Editor-first workflow   | Operates in Unity Editor; results saved with scenes/prefabs for runtime use      |
| Repeatability           | Enrichment steps can be re-applied after re-imports using scripted rules         |

_Table 1. Enrichment policies and constraints._

## Enrichment Inputs and Anchors

Each GameObject produced by the importer carries:

- `GlobalId` and `IfcClass` for identity and classification.
- Spatial context (`Project`, `Site`, `Building`, `Storey`, `ParentGlobalId`).
- Material/color metadata and a property bag from IFC property sets.
  These fields act as anchors for enrichment rules (e.g., tagging all `IfcWall` elements on a specific storey, or deriving fire rating from a property set).

## Enrichment Operations

Semantic enrichment typically includes:

- **Tagging and categorization:** Assigning domain tags (e.g., structural, MEP, envelope) based on `IfcClass`, properties, or location.
- **Derived attributes:** Computing new fields (e.g., floor area per storey, volume per element, occupancy class) from existing properties.
- **Relationship mapping:** Establishing explicit links (e.g., element-to-zone, element-to-system) when IFC relations are implicit or absent.
- **Normalization:** Harmonizing property keys/units for consistent querying across heterogeneous IFC sources.
- **Quality flags:** Marking incomplete or suspect data (e.g., missing fire rating) for downstream validation.

## Process Description

The enrichment process is carried out in the Unity Editor via custom inspectors/tools (e.g., `IFCMetadataEditor.cs`) that operate over selected GameObjects or entire scenes. Rules can be scripted to ensure repeatability after re-imports:

1. **Selection and scope:** Choose targets by hierarchy, `IfcClass`, or property predicates.
2. **Rule execution:** Apply tagging, derived computations, or relation creation; store results alongside the existing `IfcMetadataComponent` or companion data structures.
3. **Validation:** Spot-check enriched fields; log missing/ambiguous values for remediation.
4. **Persistence:** Save enriched metadata with the scene/prefab so runtime systems can consume it without recomputation.

## Challenges and Mitigations

- **Heterogeneous property naming:** IFC property sets vary across authoring tools. Mitigation: normalization maps and rule-based lookups.
- **Incomplete semantics:** Some relations (e.g., system membership) may be missing. Mitigation: infer from location, naming conventions, or user input.
- **Performance on large scenes:** Bulk edits over thousands of elements can be slow. Mitigation: batch operations and filtered scopes.
- **Re-import drift:** Re-running imports can overwrite objects. Mitigation: keep enrichment rules scriptable and re-runnable to reapply after refresh.

## Why Enrich Instead of Using Raw IFC Properties

Raw IFC properties are often inconsistent, tool-specific, or incomplete for downstream tasks such as simulation, analytics, or domain-specific UX. Enrichment:

- Adds **consistency** by normalizing keys/units.
- Adds **explicitness** by surfacing relations that are implicit or absent in the source.
- Adds **fitness** by computing derived values tailored to target applications (e.g., occupancy grouping, safety zoning).
- Preserves **traceability** by retaining GlobalId links back to the original IFC elements.

## Reproducibility Notes

- Ensure imported elements carry `IfcMetadataComponent` with GlobalId and properties intact.
- Define enrichment rules in editor scripts to reapply after IFC re-imports.
- Persist enriched metadata within scenes/prefabs; avoid runtime-only mutations if reproducibility is required.

---

## IFC Hierarchy Builder Tool

### Overview

The IFC Hierarchy Builder is an Editor tool that reads an IFC file and automatically builds the best suitable spatial hierarchy for the project, then reparents existing mesh GameObjects under the correct spatial nodes based on IFC relationships.

### Access

Menu: **BIMUniXchange > Build IFC Hierarchy**

### Features

#### Automatic Spatial Pattern Detection

The tool detects which spatial hierarchy pattern exists in the IFC and uses the most suitable schema:

| Pattern            | Schema                                                                         | When Used                                                             |
| ------------------ | ------------------------------------------------------------------------------ | --------------------------------------------------------------------- |
| **FacilityBased**  | `IfcProject → IfcFacility → IfcFacilityPart (nested 0..n) → Elements`          | IFC 4.3 infrastructure models with `IfcFacility` or `IfcFacilityPart` |
| **BuildingBased**  | `IfcProject → IfcSite → IfcBuilding → IfcBuildingStorey → IfcSpace → Elements` | Traditional building models with sites and buildings                  |
| **SiteOnly**       | `IfcProject → IfcSite → Elements`                                              | Models with sites but no buildings                                    |
| **BuildingDirect** | `IfcProject → IfcBuilding → Elements`                                          | Models with buildings but no sites                                    |

#### Spatial Node Creation

For each spatial element, a GameObject is created with:

- **Name format:** `<IfcClassName>: <Name or LongName or GlobalId>`
- **Marker component:** `IfcSpatialNodeMarker` storing GlobalId, IfcClass, and names
- **Hierarchy:** Parented according to IFC relationships (`IfcRelAggregates`, `IfcRelContainedInSpatialStructure`)

#### Element Reparenting

- Finds existing mesh GameObjects by matching `IFCMetadata.GlobalId` or extracting Element ID from the name (format: `ElementName_ElementId`)
- Reparents elements under their correct spatial container (space > storey > building > site > project)
- Preserves world position during reparenting
- Falls back to project root if no spatial container is found

#### Idempotent Rebuild

- When rebuilding, deletes only old spatial hierarchy nodes (identified by `IfcSpatialNodeMarker`)
- Preserves mesh GameObjects
- Running multiple times produces the same result without duplicating nodes

### Code Structure

#### IfcModelNavigator (Runtime)

Helper class wrapping xBIM library access for easy swapping:

```csharp
// Key methods:
LoadModel(string ifcPath)           // Load IFC file
DetectSpatialPattern()              // Detect hierarchy pattern
BuildSpatialTree()                  // Build complete spatial tree
GetSpatialContainer(string globalId) // Get spatial parent for element
GetElementByGlobalId(string id)     // Find element by GlobalId
GetElementByTag(string tag)         // Find element by Tag/ElementId
```

#### IfcHierarchyBuilder (Editor)

Main Editor window with workflow:

```csharp
// Core methods:
LoadIfcModel()                      // Load IFC via navigator
BuildProjectNode()                  // Create/find project root
BuildSpatialTree()                  // Create spatial GameObjects
ReparentElementsToSpatialNodes()    // Match and reparent elements
```

### Settings

| Setting                      | Description                                  | Default |
| ---------------------------- | -------------------------------------------- | ------- |
| **Preserve World Position**  | Keep element world position when reparenting | `true`  |
| **Delete Old Spatial Nodes** | Remove existing hierarchy before rebuild     | `true`  |
| **Sort Alphabetically**      | Sort spatial nodes by name                   | `true`  |

### Supported IFC Schemas

- IFC 2x3
- IFC 4
- IFC 4.3 (infrastructure with facilities)

### Dependencies

- xBIM libraries (`Xbim.Ifc`, `Xbim.Ifc4`, `Xbim.Ifc4x3`)
