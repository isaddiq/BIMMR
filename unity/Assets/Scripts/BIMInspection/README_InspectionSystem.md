# BIM Inspection & Monitoring Module

A drop-in, modular **BIM inspection** subsystem for Unity Mixed Reality
applications. Lets an MR user load any BIM model, select elements in 3D, view
their metadata, mark inspection status, attach issues with severity and
comments, monitor progress, and export the results to JSON / CSV.

- **Unity:** 2022 LTS or newer
- **Namespace:** `BIMMR.Inspection`
- **Folder:** `Assets/Scripts/BIMInspection/`
- **Dependencies:** none required. XR Interaction Toolkit is used *if present*
  (resolved by reflection). UnityEngine.UI only — **no TextMeshPro, no MRTK**.

---

## 1. Files in this module

| Script                              | Responsibility                                       |
|-------------------------------------|------------------------------------------------------|
| `BIMInspectionManager.cs`           | Central controller; owns records & selection state. |
| `BIMElementMetadataReader.cs`       | Pulls BIM metadata via reflection.                  |
| `BIMElementInfo.cs`                 | Metadata DTO returned by the reader.                |
| `InspectionRecord.cs`               | Per-element inspection record + `InspectionStatus`. |
| `InspectionIssue.cs`                | Single issue, with severity, comment, world pos.    |
| `BIMInspectionHighlighter.cs`       | Selection + status overlay materials.               |
| `BIMInspectionSelector.cs`          | XR ray + mouse-fallback element picker.             |
| `BIMIssuePinManager.cs`             | World-space 3D issue pins (billboarded).            |
| `BIMInspectionWorldUI.cs`           | Auto-built world-space `UnityEngine.UI` panel.      |
| `BIMInspectionProgressMonitor.cs`   | Aggregate progress statistics.                      |
| `InspectionProgressSummary.cs`      | DTO with totals + per-category / per-level.         |
| `BIMInspectionExporter.cs`          | JSON + CSV export (`Application.persistentDataPath`).|
| `BIMInspectionColliderUtility.cs`   | Adds missing `MeshCollider`s to BIM meshes.         |
| `BIMInspectionBootstrapper.cs`      | One-line scene integration helper.                  |

---

## 2. Quick-start (3 minutes)

1. **Create an empty GameObject** in your scene called `InspectionSystem`.
2. Add the `BIMInspectionBootstrapper` component to it.
3. In the inspector:
   - Drag your BIM model root(s) into **`Bim Model Roots`**.
     Any number of roots is supported — different BIM models can be mixed.
   - Drag your MR camera into **`Xr Camera`** (optional; `Camera.main` is used otherwise).
   - Set **`Inspector Name`** (e.g. `"A. Engineer"`).
   - Leave **`Add Missing Colliders On Awake`** ON unless your BIM importer
     already adds colliders.
4. Press Play. You'll see the world-space UI hidden by default.
5. Click **Start** in the UI (or call `Manager.StartInspectionMode()` from a
   button on your existing scene).
6. **Click** (Editor) or **point + trigger** (XR) at any BIM element to select it.
7. Use **Mark Pass / Fail / Needs Review**, **Add Issue**, **Add Comment**, etc.
8. Click **Export JSON** / **Export CSV** to write reports under
   `Application.persistentDataPath/BIMInspectionExports`.

---

## 3. Assigning BIM model roots

A "BIM model root" is just any `Transform` that contains the imported BIM
elements as descendants. For example:

```
Scene
└─ ImportedRevitModel              <-- assign THIS
   ├─ Walls
   │  ├─ Wall_001  (MeshRenderer, MeshFilter, MetadataComponent)
   │  └─ Wall_002  ...
   ├─ Doors / ...
```

You can assign multiple roots if you load multiple BIM models.

---

## 4. Selecting BIM elements

The selector tries XR ray interaction first (Unity XR Interaction Toolkit,
via reflection so XRI is *not* required for compilation) and falls back to
**left mouse click** raycasts in the Editor.

Make sure BIM elements have **Colliders** (`MeshCollider` is fine). If they
do not:
- Toggle `Add Missing Colliders On Awake` on the bootstrapper, **or**
- Call `BIMInspectionColliderUtility.AddMissingColliders()` from a button.

The size threshold filters out tiny decorative objects — adjust
`minimumBoundsSize` if needed.

---

## 5. Status, colors, issues

Status changes apply a translucent material overlay in real time:

| Status         | Default color |
|----------------|---------------|
| NotInspected   | Neutral gray  |
| InProgress     | Blue          |
| Passed         | Green         |
| Failed         | Red           |
| NeedsReview    | Orange/Yellow |

Color values are public fields on `BIMInspectionHighlighter` — change them in
the inspector. The highlighter caches and restores the **original** materials,
so it never permanently modifies your model.

Adding an issue spawns a 3D pin at the last raycast hit point (or the element
centre if no hit point is known). Pins billboard to the MR camera.

---

## 6. Stable identifiers (very important for re-inspection)

Inspection records are keyed by a **stable BIM id** so they survive across
scene reloads and re-imports:

```
GlobalId -> IfcGuid / IFCGuid -> RevitUniqueId -> UniqueId -> ElementId
```

You can override the preferred key on `BIMInspectionBootstrapper.preferredStableIdKey`.

If none is found, a runtime id (`runtime::<name>::<instanceID>`) is generated
and a clear `Debug.LogWarning` is emitted — the UI also shows `(fallback)` next
to the id so you know the record cannot be reliably re-linked next session.

---

## 7. Extending metadata mapping

`BIMElementMetadataReader` walks every Component on the element (plus parents
and a small radius of children) via reflection. It understands:

- `Dictionary<string, string>`
- `Dictionary<string, object>`
- `IEnumerable<KeyValuePair<TKey, TValue>>`
- Custom classes exposing `Key/Value`, `Name/Value`, or
  `PropertyName/PropertyValue` members
- Public/private fields and properties returning primitives, strings, enums

To add a new fast-path key, change `DefaultStableIdKeys` or extend the
`FirstNonEmpty(...)` calls at the top of `Read()`.

---

## 8. Exporting reports

`Manager.ExportInspectionData()` writes BOTH JSON and CSV. Individual buttons
exist too. Files land in:

```
%AppData%/<company>/<product>/BIMInspectionExports/inspection_YYYYMMDD_HHMMSS.{json,csv}
```

CSV is RFC-4180 quoted and safe to load into Excel / Power BI / Python pandas.
JSON is `JsonUtility`-flattened (one document per export).

> **TODO (cloud):** swap or extend `BIMInspectionExporter` to push to a REST
> API / Firebase / BCF. Hook points are clearly marked with `TODO(cloud-sync)`
> in code so the rest of the module does not need to change.

---

## 9. Programmatic API (cheat-sheet)

```csharp
var mgr = FindObjectOfType<BIMInspectionManager>();

mgr.StartInspectionMode();
mgr.SelectElement(someGameObject);
mgr.MarkSelectedAsPassed();
mgr.AddIssueToSelected("Damage", "High", "Crack visible on the south face.");
mgr.AddCommentToSelected("Re-check after epoxy repair.");
var progress = mgr.GetInspectionProgress();
mgr.ExportInspectionData();
mgr.StopInspectionMode();
```

Public events:

```csharp
mgr.OnInspectionModeChanged += active => ...;
mgr.OnElementSelected       += (go, info, rec) => ...;
mgr.OnRecordUpdated         += rec => ...;
mgr.OnIssueAdded            += issue => ...;
```

---

## 10. Known limitations

- **No automatic defect detection.** This module supports *manual* and
  *semi-structured* inspection. Real-world defect detection requires image
  capture, computer vision, reality-capture (photogrammetry, LiDAR), or
  sensor integration — none of which is provided here.
- **Registration.** Accurate construction monitoring also requires accurate
  registration between the BIM model and the physical site (anchors, QR
  markers, SLAM). The module assumes this is handled elsewhere.
- **Stable ids.** Reliable multi-session tracking requires real BIM ids
  (IFC `GlobalId`, Revit `UniqueId`). Without them, runtime fallback ids
  are not durable — re-imports will produce *new* records.
- **TextMeshPro.** The UI uses legacy `UnityEngine.UI.Text` for maximum
  compatibility. If you prefer TextMeshPro, you can attach your own canvas
  manually (turn off `Auto Build Canvas` and assign the `Text` fields with a
  thin wrapper) — no other change is required.
- **XR Interaction Toolkit.** Used if found at runtime. The selector still
  works without it (mouse fallback). For MR-only workflows, assign your
  `XRRayInteractor` to the same root and the selector will pick it up.
- **Persistence.** Reports are flat files on disk. Cloud sync, BCF export, and
  database persistence are intentionally out of scope; they are marked in
  code with `TODO(cloud-sync)` for easy extension.

---

## 11. Sample outputs

See `sample_output.json` and `sample_output.csv` next to this README for the
exact shape of an export.
