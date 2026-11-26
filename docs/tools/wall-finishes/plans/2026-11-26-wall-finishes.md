# Wall Finishes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Wall Finishes ribbon button that draws a detail line 10 cm off every exposed wall face in the active floor plan, styled by the face's finish, and rebuilds a legend drafting view.

**Architecture:** One external command in `RVTuk.Revit/WallFinishes/`, no pane, no external events (like Room Floors). Geometry: cut every wall at the view's cut height, union the sections, take the merged face's edge loops, give each edge a material, offset the loops 10 cm into the open, draw. Only the style-name rules are Revit-free, in `RVTuk.Core/WallFinishes/`, and unit-tested; everything else is checked in Revit.

**Tech Stack:** C#, Revit API 2024–2027 (Nice3point packages already referenced), xunit. No new dependencies.

**Spec:** `docs/tools/wall-finishes/specs/2026-11-26-wall-finishes-design.md`

## Global Constraints

- Revit 2024–2027 only, in `RVTuk.Revit`; nothing in `KKarea.Revit`.
- Namespace = root namespace + folder path, exactly (`RVTuk.Core.WallFinishes`, `RVTuk.Revit.WallFinishes`).
- Style names: `_DP-Mat <material name>`. Legend view name: `RVTuk Wall Finishes`, `ViewDrafting`, scale 1:50.
- Offset: 10 cm (`10 / 30.48` ft). Legend: 1 m sample line, rows 1 m apart, sorted by name.
- Styles are created through `Shared/LineStyleCreator.EnsureExists(doc, name, colour)`, colour = `Material.Color`; an existing style is never changed.
- Lines run across doors, windows and `Opening`s; fallback breaks them at openings (spec step 2).
- Ribbon button on the RVTuk panel, gated by `RegisterWallFinishes` (on) in `Application.cs`, placed after Room Floor.
- `.\build\Test-All.ps1` passes before every commit.

## Review Focus

1. Two walls of different finishes meeting at a corner: each offset line carries its own wall's finish up to the corner (Task 3's Revit check, case 2).
2. A material name with characters Revit refuses in a subcategory name (`:`, `[`, `{`, `|`, …): the style is still created, with them replaced (Task 1 test `ForbiddenCharactersAreReplaced`).
3. A view with no cut walls, or a non-plan active view: a message, nothing drawn, nothing left in the undo list (Task 4's Revit check, case 1).
4. A material renamed between runs: the new style is used, the old one drops off the legend once no view has lines on it (Task 4's Revit check, case 5).
5. A workshared model where someone else has borrowed a door: the run still succeeds through the fallback and says so once (Task 2's Revit check, case 4).

---

### Task 1: Style names (Core)

**Files:**
- Create: `src/RVTuk.Core/WallFinishes/FinishStyleName.cs`
- Test: `tests/RVTuk.Core.Tests/WallFinishes/FinishStyleNameTests.cs`

**Interfaces:**
- Produces: `public static class FinishStyleName` with
  `const string Prefix = "_DP-Mat ";`,
  `string For(string materialName)`,
  `bool IsFinishStyle(string styleName)`,
  `string MaterialOf(string styleName)` (the name after the prefix, for the legend's text).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void PrefixesTheMaterialName() => Assert.Equal("_DP-Mat Plaster", FinishStyleName.For("Plaster"));
[Fact] public void ForbiddenCharactersAreReplaced() => Assert.Equal("_DP-Mat Paint - RAL 9010 -A-", FinishStyleName.For("Paint : RAL 9010 [A]"));
[Fact] public void HebrewIsKept() => Assert.Equal("_DP-Mat טיח", FinishStyleName.For("טיח"));
[Fact] public void RecognisesItsOwnStyles() { Assert.True(FinishStyleName.IsFinishStyle("_DP-Mat Plaster")); Assert.False(FinishStyleName.IsFinishStyle("_DP-Dim Outer")); }
[Fact] public void MaterialOfReturnsTheNameAfterThePrefix() => Assert.Equal("Plaster", FinishStyleName.MaterialOf("_DP-Mat Plaster"));
```

- [ ] **Step 2: Run, expect FAIL (type not defined)**
  `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter FinishStyleName`
- [ ] **Step 3: Implement.** Replace each of `{ } [ ] | ; < > ? \` ~ \ :` with `-`, trim. (Characters, not regex classes, so the test value above is exact.)
- [ ] **Step 4: Run, expect PASS** (same command).
- [ ] **Step 5: Commit** `feat(wall-finishes): finish style names`

### Task 2: The exposed outline and its materials (Revit)

**Files:**
- Create: `src/RVTuk.Revit/WallFinishes/FinishOutline.cs`

**Interfaces:**
- Produces:
  - `public sealed class FinishEdge { public Curve Edge; public ElementId MaterialId; }` (`MaterialId` is `InvalidElementId` when none was found)
  - `public sealed class FinishLoop { public PlanarFace Face; public List<FinishEdge> Edges; }` (the merged section's face the loop bounds — a free solid, so it survives the rollback)
  - `public sealed class FinishOutlineResult { public List<FinishLoop> Loops; public int BooleanFailures; public int EdgesWithoutMaterial; public bool UsedFallback; }`
  - `public static FinishOutlineResult FinishOutline.Read(Document doc, ViewPlan view)` — call outside any open transaction; leaves the document unchanged.

- [ ] **Step 1: Implement `Read`**, following spec steps 1–4:
  - Walls: `new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall))`, skip `WallKind.Curtain`; a stacked wall (`IsStackedWall`) is replaced by `GetStackedWallMemberIds()`.
  - Cut height: `view.GenLevel.Elevation + view.GetViewRange().GetOffset(PlanViewPlane.CutPlane)`. Thin box: `GeometryCreationUtilities.CreateExtrusionGeometry` of a rectangle 1 mm high centred there, covering the walls' combined bounding box plus 1 m.
  - Inside `using var tx = new Transaction(doc, "Wall Finishes — read")`, `tx.Start()`, delete the walls' inserts (`wall.FindInserts(true, false, true, true)`), `doc.Regenerate()`, read, `tx.RollBack()` in `finally`. If `Start`/`Delete`/`Regenerate` throws, roll back and read again without deleting, with `UsedFallback = true`, dropping edges whose matched face has a non-empty `wall.GetGeneratingElementIds(face)` that contains an insert.
  - Per wall: its `Solid`s from `get_Geometry(new Options { ComputeReferences = true })`, `BooleanOperationsUtils.ExecuteBooleanOperation(solid, box, Intersect)`; union all into one solid. When a wall's union throws, count it in `BooleanFailures` and keep its section as a solid of its own, unioned with nothing.
  - Loops: for each resulting solid, the `PlanarFace` with `FaceNormal.Z < -0.99`; `GetEdgesAsCurveLoops()`, curves flattened to the cut height.
  - Material per edge (midpoint, tolerance 1 mm, prefilter walls by bounding box): try the wall's `HostObjectUtils.GetSideFaces(wall, Exterior)` faces → layer 0; then `…Interior` → last layer; otherwise any other face of the wall's solid (a free end) → no layer. On any matched face, `doc.IsPainted(wall.Id, face)` wins with `doc.GetPaintedMaterial`. A layer's `MaterialId` invalid → `Category.GetCategory(doc, BuiltInCategory.OST_Walls).Material?.Id`.
  - Free ends: after a loop is filled, every edge that still has no material takes the previous edge's on the loop (wrapping round); edges still without one count in `EdgesWithoutMaterial`.
- [ ] **Step 2: Build** `dotnet build RVTuk.sln -c Release2024` and `-c Release2027`. Expected: 0 errors.
- [ ] **Step 3: Revit check** (temporary `TaskDialog` from Task 4's command, or after Task 4): in a test project, (1) a room of four plastered walls → one outer loop and one inner loop; (2) a core wall with a separate finish wall against it, not joined → no edge between them; (3) a wall with a door → its loop runs straight across; (4) the same model workshared, a door borrowed by another user → `UsedFallback` and the loop breaks at the door.
- [ ] **Step 4: Commit** `feat(wall-finishes): read the exposed outline and its finishes`

### Task 3: Offset 10 cm, matched by geometry (Revit)

**Files:**
- Create: `src/RVTuk.Revit/WallFinishes/FinishOffset.cs`

**Interfaces:**
- Consumes: `FinishEdge`, `FinishLoop` (Task 2).
- Produces: `public static List<FinishEdge> FinishOffset.Apply(FinishLoop loop, out bool failed)` — the offset curves, each with the material of its source edge.

- [ ] **Step 1: Implement `Apply`.** `CurveLoop.CreateViaOffset(loop, 10 cm, XYZ.BasisZ)`; try +10 cm and keep it if its first curve's midpoint is outside `loop.Face` (`Face.Project` → `UVPoint`, `!Face.IsInside(uv)`), otherwise use −10 cm. Each offset curve takes the material of the source edge whose distance to its midpoint is 10 cm ± 1 mm and whose tangent there is parallel; never by index. `failed = true` (and an empty list) when `CreateViaOffset` throws or the result has two curves that intersect other than at shared ends (`Curve.Intersect` → `Overlap`/more than end points).
- [ ] **Step 2: Build** both configs as in Task 2. Expected: 0 errors.
- [ ] **Step 3: Revit check:** (1) the plastered room → lines 10 cm off inside and outside, corners meeting; (2) corner of a plastered wall and a tiled wall → each line carries its own style to the corner; (3) two walls 15 cm apart → that loop skipped and counted; (4) an arc wall → an arc line.
- [ ] **Step 4: Commit** `feat(wall-finishes): offset the outline, materials matched by geometry`

### Task 4: Command, drawing, legend, ribbon, docs

**Files:**
- Create: `src/RVTuk.Revit/WallFinishes/Commands/WallFinishesCommand.cs`, `src/RVTuk.Revit/WallFinishes/FinishLegend.cs`, `docs/tools/wall-finishes/README.md`, `docs/tools/wall-finishes/backlog.md`
- Modify: `src/RVTuk.Revit/Application.cs` (flag next to `RegisterRoomFloors`, button after Room Floor's, icon method next to `CreateRoomFloorsIcon`), `CLAUDE.md` (Terminology tool list, canonical names, features list)

**Interfaces:**
- Consumes: `FinishStyleName` (Task 1), `FinishOutline.Read` (Task 2), `FinishOffset.Apply` (Task 3), `LineStyleCreator.EnsureExists`.
- Produces: `[Transaction(TransactionMode.Manual)] public class WallFinishesCommand : IExternalCommand`; `public static void FinishLegend.Rebuild(Document doc)` (inside an open transaction).

- [ ] **Step 1: Implement the command.** Active view not a `ViewPlan` with a `GenLevel` → `TaskDialog` "Open a floor plan first: Wall Finishes draws in a plan view.", `Cancelled`. Read; no loops → "No walls are cut in this view.", `Cancelled`. Offset each loop. Then one `Transaction(doc, "Wall Finishes")`: delete detail lines owned by this view whose `LineStyle.Name` is a finish style; per material `EnsureExists(doc, FinishStyleName.For(material.Name), material.Color)`; `doc.Create.NewDetailCurve(view, curve)` with `LineStyle` set to the style's `GraphicsStyle` (`category.GetGraphicsStyle(GraphicsStyleType.Projection)`); `FinishLegend.Rebuild(doc)`; commit. Report only if something was skipped (`BooleanFailures`, `EdgesWithoutMaterial`, failed loops, `UsedFallback`), one line each.
- [ ] **Step 2: Implement `FinishLegend.Rebuild`.** Find the `ViewDrafting` named `RVTuk Wall Finishes` or create one (`ViewDrafting.Create` with the first `ViewFamilyType` of `ViewFamily.Drafting`, `Scale = 50`). Delete everything owned by it. Styles = distinct `LineStyle`s of all `CurveElement`s in the document (excluding this view) that are finish styles, sorted by name. Row *i* at y = −*i* m: a 1 m detail line in the style, then a `TextNote` with `MaterialOf(name)` 0.3 m to its right, default text type.
- [ ] **Step 3: Ribbon.** `RegisterWallFinishes = true`; `PushButtonData("WallFinishes", "Wall\nFinishes", …)`, tooltip "Draw a line 10 cm off every exposed wall face in this plan, styled by its finish, and update the RVTuk Wall Finishes legend."; a 16/32 icon in the style of `CreateRoomFloorsIcon` (a wall in grey with a coloured line along each face).
- [ ] **Step 4: Docs.** README (what it is, the rules from the spec's *What it does*, status "built, not yet checked in Revit", entry point), empty backlog, CLAUDE.md entries.
- [ ] **Step 5: Build and test** `.\build\Test-All.ps1`, then `.\build\Test-Staging.ps1`. Expected: all pass.
- [ ] **Step 6: Revit check** (deploy `wall-finishes` with `.\Deploy.ps1 2024` from the checkout): (1) a 3D view, then an empty plan → messages, nothing in Undo; (2) a plan of rooms → lines, legend listing each finish; (3) run again → the same lines, not doubled; (4) paint a face → re-run gives that face the paint's style; (5) rename a material → new style used, old one gone from the legend after re-running in every view that had it; (6) one Ctrl+Z removes the whole run.
- [ ] **Step 7: Commit** `feat(wall-finishes): command, legend and ribbon button`, then merge `wall-finishes` into `master` (CLAUDE.md, *Branches*).
