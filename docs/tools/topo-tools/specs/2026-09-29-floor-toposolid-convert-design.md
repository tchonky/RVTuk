# Topo Tools — convert floors ⇄ toposolids from the pane

**Date:** 2026-09-29
**Status:** Design, approved in conversation; spec awaiting review

## Why

Site work moves back and forth between the two elements. A piece of terrain modelled as a toposolid
has to act as a floor for a while (floor-hosted families, floor schedules, a structural slab), and
paving or a podium drawn as a floor has to become terrain again (site tools, Topo Tools points).
Revit has no command for either direction: you redraw the sketch and re-pick every shape point by
hand.

**The bar is the round trip.** Floor → toposolid → floor must give back the element you started
with: same outline, same level and offset, same top surface point for point, same parameter values,
and RVTuk's own data intact. The type is the one stated exception (see Types).

## Scope

**In:** one ⇄ button in the Topo Tools pane. It converts every floor and toposolid in Revit's
current selection into the other kind.

**Out:**
- choosing the target type in the tool: it takes Revit's last-placed type (see Types);
- floors or toposolids in linked models: they cannot be selected in the host anyway;
- restoring what the other kind cannot express (a slope arrow, subdivisions, joins, per-view
  graphic overrides, excavation): these are listed under Losses;
- re-hosting dependents (families, slab edges, tags) onto the new element: they are deleted, after
  a confirm dialog.

**Revit 2024/2025 only,** like the rest of Topo Tools. `Toposolid` does not exist in 2023.
`SlabShapeEditor.AddPoints` is 2024.2+, which Topo Tools already requires.

## Workflow

1. Select floors and/or toposolids in any view.
2. Press **⇄** in the Topo Tools pane, beside ▶ and ⟳. Tooltip: *"Convert selected floors ⇄
   toposolids"*.
3. Each selected `Floor` becomes a toposolid, and each selected `Toposolid` becomes a floor. A
   mixed selection flips each element its own way. Any other element in the selection is ignored.
   If there is no floor or toposolid at all, the status line says *"Select floors or toposolids
   first."* and nothing happens.
4. **Refusals.** These elements are skipped with a reason before anything is asked or changed:
   - a toposolid subdivision (`HostTopoId` is valid). It belongs to its host, which is also why
     `ToposolidCollector` skips subdivisions;
   - an element in a model group (`GroupId` is valid). Deleting a group member and creating the
     replacement outside the group would break the group;
   - an element whose design option is not the active one
     (`DesignOption.GetActiveDesignOptionId(doc)`, where `InvalidElementId` means the main model).
     The replacement is created in the active option, so the element would change option;
   - in a workshared model, an element owned by another user
     (`WorksharingUtils.GetCheckoutStatus == OwnedByOtherUser`). The reason names the owner.
5. **Ask phase.** A dialog appears for each remaining element that has dependents (see Dependents).
   Every answer is collected before any change is made.
6. **Convert phase.** The elements that passed are converted, as one undoable run.
7. Afterwards:
   - the **new elements are selected**, so pressing ⇄ again flips them straight back;
   - the status line summarises the run, e.g. *"2 floors → toposolids, 1 toposolid → floor.
     Skipped 2: subdivision; kept its 2 hosted families."*;
   - the pane refreshes.

The pane calls a `Func<string>` delegate from the thread pool. The delegate raises a new
`TopoConvertEventHandler` external event and waits on the `ManualResetEventSlim` ping-pong, exactly
as ▶ Apply does. The dialogs are Revit `TaskDialog`s shown from inside that handler, on Revit's main
thread.

## Types

The converted element gets **Revit's last-placed type** for its new kind, meaning the type Revit's
own Floor or Toposolid tool would place next. The pane has no type choice.

- **Floor:** `doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType)` if it resolves to a
  `FloorType`. Otherwise, the first non-foundation `FloorType`. This is Room Floor's existing
  `RoomFloorBuilder.DefaultFloorTypeId`, moved to `Shared/` because two tools now use it.
- **Toposolid:** there is no `ElementTypeGroup` for toposolids. The tool tries
  `doc.GetDefaultFamilyTypeId(new ElementId(BuiltInCategory.OST_Toposolid))` and uses the result if
  it is a `ToposolidType`. Otherwise it uses the first `ToposolidType` by name. **Whether Revit
  answers that call for a system type is unverified.** It is the first in-Revit check, and the
  fallback keeps the tool working either way.
- The tool's own creations do not change Revit's default type, because the API's `Create` does not
  touch it.
- **Consequence:** a round trip restores everything in *What carries across* except the type. The
  type comes back as whatever Revit would place next.
- If the document has no type of the target kind, every element in that direction is skipped with
  *"the project has no floor types"* or *"the project has no toposolid types"*.

## What carries across

**The top surface is what is preserved.** The thickness and the bottom come from the target type.

### Outline, level, offset

- **Outline:** the source's sketch (`doc.GetElement(source.SketchId) as Sketch`). Each
  `CurveArray` in `sketch.Profile` becomes one `CurveLoop`, with curves as they are. Arcs stay arcs
  and holes stay holes.
- **Creation:**
  - toposolid: `Toposolid.Create(doc, loops, typeId, levelId)`;
  - floor: `Floor.Create(doc, loops, typeId, levelId)`.
  - The loops are ordered by plan extent, largest first, as `RoomFloorBuilder` does. It costs
    nothing and removes the question of loop order.
- **Level:** `source.LevelId`.
- **Offset:** `FLOOR_HEIGHTABOVELEVEL_PARAM` ↔ `TOPOSOLID_HEIGHTABOVELEVEL_PARAM`, set right after
  creation.

### Shape

A slab shape is its vertices plus its creases.

- **Vertices:** corners at the sketch vertices, edge points on the boundary, and interior points.
- **Creases:** `Boundary`, `Auto` (from Revit's own triangulation), or `UserDrawn` (split lines).

`SlabShapeVertex.Position` is absolute model coordinates, which is how the Topo Tools applier
already uses `AddPoints`.

1. **Read.** If the source's editor `IsEnabled`, snapshot every vertex position, and every
   `UserDrawn` crease as its two end positions.
2. **Create the target flat** (above), then `doc.Regenerate()`, which `AddPoints` requires after a
   creation in the same transaction. Then `Enable()` the target's editor.
3. **Plan** (pure Core, `ShapeReplayPlanner`). Each source vertex is matched to a vertex of the flat
   target by XY within 0.1 mm (`TopoPointApplier.MatchToleranceFeet`). The plan says:
   - **matched** → `ModifySubElement(vertex, offset)`, where
     `offset = source.Z − targetVertex.Position.Z`. The target is flat and fresh, so every target
     vertex sits at offset 0;
   - **unmatched** → `AddPoints` at the absolute source position, in one call;
   - **each user-drawn crease** → its two end positions, to be matched to live vertices after the
     points exist. A crease is dropped, and counted, if either end has no vertex within tolerance.
4. **Apply** the plan: modify, add, `Regenerate()`, re-read the vertex list, then `DrawSplitLine`
   for each user-drawn crease.
5. **Auto creases are not copied.** Revit regenerates them from the same vertices.

**Edge points** are source vertices on a boundary edge that are not corners. They have no match on
the flat target, and `AddPoints` asks for points *inside* the boundary. If `AddPoints` rejects them
(an in-Revit check), the fallback is `DrawPoint(XYZ)` at the flat position followed by
`ModifySubElement`. Any that still fail are counted and reported. Nothing is guessed.

**Sloped floors** have a slope arrow or a slope-defining edge. Their editor has no vertices, yet
their top is not flat.
- For each sketch corner, the tool reads the height of the source's top face at that XY. It
  projects the point onto the top `PlanarFace` of the floor's solid, the face whose normal has
  `Z > 0`.
- It sets those heights on the target's corners as matched vertices.
- The return trip gives a **flat floor with edited corners**: the same geometry, but no slope
  arrow.

A source whose editor is not enabled and whose top is flat has nothing to replay, so the target
stays flat.

### Parameters

`ParameterCarrier` walks the source's instance parameters. It skips:
- read-only ones (area, volume, perimeter, elevations at top and bottom);
- ones that have no value;
- the ones this design handles explicitly: level, height offset, type, design option.

A parameter is identified the same way on both kinds:
- **built-in:** by its `BuiltInParameter`;
- **shared:** by its GUID;
- **project parameter:** by name, among parameters that are neither built-in nor shared.

For each parameter:
- **The target has it** (writable, same `StorageType`) → the value is copied. This covers
  Comments, Mark, Phase Created and Phase Demolished, Workset (`ELEM_PARTITION_PARAM`), and shared
  or project parameters bound to both categories.
- **The target does not have it** → it is **parked**. Examples: a floor's *Structural* flag, or a
  shared parameter bound to Floors only.

**Parking.** The parked values travel in Extensible Storage on the new element (`ParkedParameterStore`).
- On every conversion, parked entries from the source are offered to the target first. An entry
  the target can take is set and leaves the record. One it cannot take stays parked.
- The record therefore survives any number of hops. A Floors-only finish code survives floor →
  toposolid → floor, and *Structural* comes back ticked.
- A copy or restore that Revit refuses is counted as "N parameter values not carried", never fatal.

**Encoding** is a Core codec, `ParkedParameterCodec`, in the pattern of `TopoPointLedgerCodec`:
- one string field;
- each entry holds the key kind, the key, the storage type and the value;
- keys and string values are percent-escaped (`Uri.EscapeDataString`), so any character survives,
  including `;`, `=`, `|` and Hebrew;
- doubles use "R" formatting, and element ids are stored as `long`;
- decoding never throws, and a damaged record decodes to empty.

### RVTuk's own storage, pinning

- **Our storage.** Every Extensible Storage entity on the source whose schema's vendor is
  `KnafoKlimor` is copied to the new element as it is. The parked-parameter entity is the
  exception, because `ParameterCarrier` writes an updated one. Two entities matter today:
  - **The Topo Tools point ledger.** The vertices are replayed at the same absolute positions, so
    after a round trip ▶ Apply Points still matches its points within 0.1 mm and stays idempotent.
    While the element is a floor, the ledger is inert: Apply only writes to toposolids.
  - **The Room Floor link.** It survives the round trip. **Limitation:** while a room's floor is a
    toposolid, Room Floor does not see it, because `FindLinkedFloors` collects `Floor` only.
    Clicking that room creates a second floor. Converting back restores the link.
- **Pinned:** the source's `Pinned` is copied to the target last. The source is unpinned before it
  is deleted.

### Losses

These are said in the spec and README, and counted in the status line where the tool can see them:
- the slope arrow (see Sloped floors);
- the dependents the user agreed to delete (see Dependents);
- user-drawn creases or edge points that could not be matched;
- *Join Geometry* relationships;
- per-element graphic overrides in views;
- excavation relationships (Revit 2025).

## Dependents

**What counts** is what deleting the source would delete with it, found with `GetDependentElements`:
- **hosted:** `FamilyInstance` (floor-based families, planting or site components on a toposolid),
  `Opening`, `HostedSweep` (slab edges);
- **annotations:** `IndependentTag`, `Dimension` (which includes spot elevations);
- **subdivisions:** on a toposolid, `GetSubDivisionIds()`.

The source's own sketch and slab shape data are not counted, because they are rebuilt.
`RoomFloorBuilder.CountHostedElements` and `CountAnnotations` move to `Shared/DependentsCounter`,
generalised from `Floor` to `Element`, and the subdivision count is added there.

**Ask phase.** For each element with dependents, in selection order, a `TaskDialog` appears:

> **Convert floor → toposolid?**
> Floor *Paving 200* (id 418732) carries **2 hosted families, 1 slab edge, 3 tags** that converting
> deletes.
> ▸ **Convert and delete them**
> ▸ **Skip this one**
> ☐ Do the same for the rest of the selection *(shown only while more elements with dependents
> remain)*

- Only the non-zero counts are listed.
- **Esc or ✕ cancels the whole run.** No transaction has been opened yet, so nothing has changed.
  The status line says *"Cancelled — nothing converted."*
- When the box is ticked, later elements with dependents take the same answer without a dialog.
- A skipped element is reported as *"kept its …"* followed by the counts.

Asking everything before converting anything means no dialog is ever on screen with a transaction
open, and Cancel really does mean nothing happened.

## Undo and errors

- **One Ctrl+Z undoes the whole run.** The convert phase runs inside one `TransactionGroup` named
  "Convert Floor ⇄ Toposolid", which is assimilated at the end.
- **Each element has its own `Transaction`** inside the group. The whole element happens in that
  one transaction: read, create, replay, carry, then unpin and delete the source. If anything in it
  throws, or Revit refuses the commit, only that element rolls back. The original is back
  untouched, and the reason is listed.
- **The order within the transaction** is to read everything from the source, delete the source,
  then create the target. The source is gone before the target exists, which avoids the
  overlapping-floors warning. A rollback restores it.
- **A failures preprocessor** on each element's transaction deletes warnings and counts them into
  the summary, so 20 elements do not produce 20 warning popups. Errors cause that element's
  rollback.
- If the group ends with nothing converted, it is rolled back rather than assimilated, so no empty
  undo entry is left behind.

## Code

The repo's rules apply:
- one folder per tool;
- a file used by two tools moves to `Shared/`;
- the namespace follows the folder.

| Layer | File | Role |
|-------|------|------|
| Core | `TopoTools/ShapeReplayPlanner.cs` | Source vertices + flat target vertices → modify / add / crease-endpoint plan (pure, `XyzPoint`). |
| Core | `TopoTools/ParkedParameterCodec.cs` | Parked entries ⇄ one string. |
| Revit | `TopoTools/SlabConverter.cs` | Converts one element inside the caller's transaction: read, delete, create, replay, carry. |
| Revit | `TopoTools/ConvertRunner.cs` | Selection → refusals → ask phase → convert phase → summary text. |
| Revit | `TopoTools/ParameterCarrier.cs` | Copies the shared parameters; parks and restores one-sided ones. |
| Revit | `TopoTools/ParkedParameterStore.cs` | Extensible Storage for the parked record (vendor `KnafoKlimor`, new GUID). |
| Revit | `TopoTools/ExternalEvents/TopoConvertEventHandler.cs` | External event wrapper, same shape as `TopoApplyEventHandler`. |
| Revit | `Shared/DefaultTypes.cs` | `DefaultFloorTypeId` (moved from Room Floor) + `DefaultToposolidTypeId`. |
| Revit | `Shared/DependentsCounter.cs` | Hosted, annotation and subdivision counts (moved from Room Floor, generalised). |
| Revit | `RoomFloors/RoomFloorBuilder.cs` | Calls the two moved helpers; behaviour unchanged. |
| UI | `TopoTools/ViewModels/TopoToolsPaneViewModel.cs` | `ConvertCommand`, `Func<string>` delegate, busy flag and refresh, like Apply. |
| UI | `TopoTools/Views/TopoToolsPaneView.xaml` | The ⇄ button beside ▶ and ⟳. |
| Revit | `Application.cs` | Creates the handler and event, and wires the delegate into the view model. |

## Testing

**Core, xunit,** in `tests/RVTuk.Core.Tests/TopoTools/`:
- `ShapeReplayPlannerTests`:
  - corners matched within tolerance and not outside it;
  - interior and edge points become adds;
  - offsets computed from the target's Z;
  - a crease whose end has no vertex is dropped and counted;
  - an empty source gives an empty plan.
- `ParkedParameterCodecTests`:
  - every storage type round-trips;
  - separators inside values survive;
  - mixed Hebrew and Latin text (`ריצוף A-12`, `מטבח 2`) round-trips byte for byte, per the office
    rule that text is never tested on Hebrew-only samples;
  - damaged input decodes to empty without throwing.

**In Revit,** as a checklist table in the Topo Tools README with 2024 and 2025 columns:

| # | Case | Expected |
|---|------|----------|
| 1 | Default toposolid type probe | Note whether `GetDefaultFamilyTypeId(OST_Toposolid)` returns the last-placed type or falls back |
| 2 | Flat rectangular floor → toposolid | Same outline, level and offset; top at the same height |
| 3 | Floor with a hole and an arc edge → toposolid → floor | Hole and arc intact both ways |
| 4 | Shape-edited floor (raised corners, interior points) → toposolid → floor | Every vertex back at its Z within 0.1 mm |
| 5 | Survey toposolid (hundreds of points) → floor → toposolid | Same surface; acceptable speed |
| 6 | Topo Tools: Apply Points, round trip the toposolid, Apply Points again | Same counts added and replaced; nothing reported "moved by hand" |
| 7 | User-drawn split line on a floor → toposolid | Split line present on the toposolid |
| 8 | Floor with points on a boundary edge → toposolid | Edge points present (`AddPoints`, or the `DrawPoint` fallback) |
| 9 | Sloped floor (slope arrow) → toposolid → floor | Same geometry; the floor comes back shape-edited with no slope arrow |
| 10 | Structural floor with a Floors-only shared parameter set to `ריצוף A-12` → toposolid → floor | Structural ticked again; the parameter reads `ריצוף A-12` |
| 11 | Comments, Mark, Phase Created, Workset | Copied both ways |
| 12 | A Room Floor floor → toposolid → floor, then click its room with Room Floor | The floor is replaced, not duplicated |
| 13 | Floor with a hosted family: Convert / Skip / Esc | Deleted and converted / untouched and "kept its 1 hosted family" / nothing changed |
| 14 | Three floors with hosted families, "Do the same" ticked on the first | One dialog only |
| 15 | Mixed selection: 2 floors, 1 toposolid, 1 wall | Each flips; the wall is ignored; the new elements are selected |
| 16 | A toposolid subdivision selected | Skipped: subdivision |
| 17 | An element in a model group; one in an inactive design option | Both skipped with their reasons |
| 18 | Ctrl+Z after converting three elements | All three back in one step |
| 19 | Workshared: an element borrowed by another user | Skipped, naming the owner |
| 20 | Pinned floor → toposolid | The toposolid is pinned |
| 21 | Nothing relevant selected | "Select floors or toposolids first." |

## Docs to update with the implementation

- `docs/tools/topo-tools/README.md`: the converter, its losses, and the checklist above.
- `docs/tools/topo-tools/backlog.md`: the checklist items to verify.
- `docs/tools/room-floors/README.md`: one line on the toposolid limitation.
- `CLAUDE.md`: the Topo Tools feature line mentions the ⇄ converter.
