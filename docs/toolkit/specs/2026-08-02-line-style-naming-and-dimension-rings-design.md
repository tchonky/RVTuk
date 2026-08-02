# Design: line style naming, first-run creation, dimension rings, and reference lines

**Status:** approved design — not yet implemented.

Two tools drive their work off a dedicated line subcategory the user draws detail lines
on: Auto Dimensions (`Dimensions_Line`) and Topo Tools (`Topo_Line`). This design renames
both to the office's `_DP-` convention, makes both create their styles on first run with
no setup button, splits Auto Dimensions' single style into an outer and an inner ring that
own their openings by priority, and adds a third style that points the tool at a reference
the automatic pass structurally cannot find.

## Goal

1. **Rename** the styles to `_DP-Dim Outer`, `_DP-Dim Inner` (Auto Dimensions) and
   `_DP-Topo Line` (Topo Tools).
2. **Drop the setup button.** Both tools create whatever styles are missing when their
   pane refreshes. Topo Tools' "Set up this project" banner and its whole setup path go.
3. **Rings.** An outer dimension string gets first claim on every opening it can see; the
   inner strings take only what no outer string can reach.
4. **Reference lines.** A `_DP-Dim Ref` line drawn from a wall's end to a dimension string
   adds a mark for that wall end on that string.

## Decisions made

- **No migration.** The old subcategories are left untouched and never read again. There
  are no existing models drawing on them, so nothing is stranded — confirmed with the
  user, and the reason this design carries no rename-in-place step.
- **Settings stay shared.** Both rings run with the same category ticks and the same
  dimension type. The ring changes *who owns an opening*, nothing else.
- **Walls are untouched by rings.** A wall crossed by three strings is still measured by
  all three, inner and outer alike — that is what a chained string is.
- **A ref line points; it is never itself dimensioned.** See §4 for why the obvious
  implementation fails silently.
- **Ref lines resolve wall ends in this round,** with the resolver shaped so other
  categories drop in later.
- **A ref line joins every string it touches.** How far you draw it is the control.

## Design

### 1. Names

| Tool | Was | Becomes |
|------|-----|---------|
| Topo Tools | `Topo_Line` | `_DP-Topo Line` |
| Auto Dimensions | `Dimensions_Line` | `_DP-Dim Outer`, `_DP-Dim Inner`, `_DP-Dim Ref` |

A leading underscore, a hyphen and spaces are all legal in a Revit subcategory name (the
disallowed set is ``\ : { } [ ] | ; < > ? ` ~``), and the underscore sorts them to the top
of the Line Styles dialog.

### 2. First-run creation (`RVTuk.Revit/Shared/LineStyleCreator.cs`)

A new shared helper — `Shared/` rather than a tool folder because two tools use it, per
the repo's one-folder-per-tool rule:

```csharp
bool Exists(Document doc, string name);
void EnsureExists(Document doc, string name);          // caller's transaction
bool TryEnsureInOwnTransaction(Document doc, params string[] names);
```

`TryEnsureInOwnTransaction` opens a transaction **only when something is actually
missing**, so the common refresh stays read-only and does not mark the document modified.
It swallows failure and returns false: a read-only or otherwise untransactable document
must still refresh normally rather than throw at the user.

Both discovery handlers call it before discovering — `TopoDiscoveryEventHandler` for the
one style, `LevelDiscoveryEventHandler` for all three dimension styles.
`CreateDimensionsEventHandler` and `TopoRunner.Apply` keep ensuring inside their own run
transactions, so a style deleted between refresh and run cannot fail the run.

This also fixes a standing chicken-and-egg in Auto Dimensions: its style was created only
by the Create Dimensions run, so the first press necessarily did nothing — there had been
no style to draw a reference line on.

**Removed from Topo Tools:** `TopoSetupEventHandler`, `SetUpCommand` / `RunSetUp` / the
`_setUpProject` delegate, `IsProjectSetUp` / `NeedsSetup`, `TopoScope.IsProjectSetUp` and
`TopoScope.NotSetUp`, and the setup banner in `TopoToolsPaneView.xaml`.

### 3. Dimension rings (`RVTuk.Core/AutoDimensions`)

```csharp
/// Ordered: the value IS the priority. Outer must sort first.
public enum DimensionRing { Outer = 0, Inner = 1 }

public record ReferenceLine(XyPoint Start, XyPoint End,
                            DimensionRing Ring = DimensionRing.Outer);
```

The default keeps the single-line `FindMatchIndices` wrapper (and its tests) meaningful —
one line's ring cannot matter.

The behavioural change is one sort key in `CandidateMatcher`. Ownership of an opening goes
from *nearest qualifying line* to **(ring, then distance, then line order)**:

- An outer line that qualifies beats every inner line outright, however much closer the
  inner one stands. This is the point of the change: a facade window belongs on the facade
  string, not on the interior string that happens to sit nearer it.
- Among lines of the same ring, nearest wins, and ties go to the earlier line — so
  re-running never shuffles an opening between strings.
- What **no** outer line can see falls through to the inner lines and is settled among
  them the same way.

The qualification test itself — host wall parallel to the line, centre within the line's
span, no parallel wall standing in between — is unchanged. "Visible to an outer string"
means exactly what `IsBlocked` already means.

**New method, to serve §4:** `FindMatchesForLines` returns `(int Index, double T)` pairs
instead of bare indices, and `FindMatchIndicesForLines` becomes a thin wrapper that strips
the `T`. The runner needs each candidate's station along the line in order to merge ref
line marks into the right place in the string, and `T` is already computed — recomputing
it in the runner would duplicate the matcher's rules and invite drift. Existing tests keep
working against the wrapper.

### 4. Reference lines — `_DP-Dim Ref`

#### 4.1 Why the line is a pointer, not a reference

Revit can dimension to a detail line (`GeometryCurve.Reference`), and that is the tempting
one-liner. It is wrong here twice over:

- **Detail lines are view-specific.** Reference lines live in one view of a level and the
  dimensions fan out to every other view of that level. A dimension created in view B
  cannot reference a detail line owned by view A — it would work in the reference view and
  produce nothing in the fanned-out ones, silently.
- **It would not track the wall.** Move the wall, re-run, and the mark stays where the
  line is, so the ref line would need repositioning by hand forever.

So the ref line points at a wall end and the tool dimensions to the **wall's end face** —
a model-level geometric reference, valid in every fanned-out view, moving with the wall.

This is a reach for the one reference the automatic pass structurally cannot use rather
than a special case bolted on. `ReferenceAlignment` requires a reference's normal to run
along the dimension direction. For a wall parallel to the string, its **side** faces have
normals across the string (unusable — which is exactly why parallel walls are invisible
today) while its **end** face has a normal along the wall, hence along the string, and is
therefore usable. `ReferenceAlignment.CanDimension(..., ReferenceNormal.AlongSegment)`
already expresses precisely that test and is reused verbatim.

#### 4.2 Resolution

**Core — `RefLineMatcher` (pure 2D, no Revit):** given a ref line's two endpoints and the
strings, return every string it meets, the parameter `T` at which it meets each, and which
of its own endpoints is the **target end**. A ref line must be straight, like the strings.

- A string is *met* when the ref line crosses it or an endpoint lies on it within tolerance.
- The **target end** is the ref line endpoint farthest from the met intersection points —
  the end aiming away from the strings, at the wall.
- A ref line meeting no string, or one whose both ends sit on strings (no target end), is
  rejected and reported.

**Revit — wall end and face:**

1. Find the nearest wall end to the target point among every collected wall, within
   `WallEndTolerance` (1 ft). Generous on purpose: the user may snap to the wall's *face*
   corner rather than its location-curve end, and the two differ by half the wall's
   thickness — 1 ft covers walls to 600 mm.
2. Check the wall passes `CanDimension(..., AlongSegment)` against that string; a wall too
   far off parallel has an end face Revit will reject.
3. Pull the wall's solid with `ComputeReferences = true` and **view-independent** options,
   so one resolution serves every fanned-out view. Keep the `PlanarFace`s whose normal is
   within the parallel tolerance of the wall's direction, and take the one nearest the
   target point. Use its `Reference`; for a linked wall, `CreateLinkReference(link)` as
   elsewhere.

**Every collected wall**, because a ref line must resolve whether or not Walls is ticked —
the same reasoning that already makes every wall an occluder. `DimensionCandidateSet`
gains `OccluderItems`, a list of `DimensionCandidate` index-aligned with the existing
`Occluders`, carrying the `Wall` and its `Link`. Core is untouched by this.

**Contribution:** one reference per ref line, not the two a wall or opening contributes —
a single witness line at the wall end. It slots into the string at its `T`, so a mark
landing at the same station as a wall face is merged by the existing coincident filter,
which is the correct reading of two references at one place.

**Not a reference view on its own:** `LevelScopeFinder` still looks only for `_DP-Dim
Outer` / `_DP-Dim Inner` lines when choosing a level's reference view. Ref lines with no
string produce nothing, so a view holding only ref lines is not a reference view.

### 5. Callers

- `DimensionLineStyle`: `OuterLineStyleName` / `InnerLineStyleName` / `RefLineStyleName`
  constants; `IsDimensionsLine` becomes `TryGetRing(CurveElement, out DimensionRing)`,
  plus `IsRefLine`.
- `DimensionRunner.CollectReferenceLines` returns each line paired with its ring, and a
  companion collects the reference view's ref lines. `RunPair` passes the ring into
  `ReferenceLine` and resolves ref lines per string.
- `DimensionCandidateCollector.TryAppendReferences` becomes `TryResolveReferences`,
  returning the references rather than appending them, so `RunLine` can order candidate
  marks and ref line marks together by `T` before building the `ReferenceArray`.
- `LevelScopeFinder` accepts a line on either ring style when deciding which view owns the
  reference lines.
- `TopoLineStyle`, `TopoRunner`, `TopoScope`: the renamed constant, and the setup path
  stripped per §2.
- The Auto Dimensions pane needs no change — it never names a style.

## Error handling

- A document that cannot take a transaction refreshes without its styles rather than
  failing; the missing style then simply yields no lines, which is the same state as a
  project nobody has drawn in yet.
- A style deleted between refresh and run is recreated by the run's own transaction.
- **Joined wall ends are the known limit.** A wall joined into another at that end has its
  end face clipped or consumed by Revit, and there may be no planar face to reference. A
  wall that genuinely stops — the case this feature is for — keeps its end face. Where no
  face resolves, the ref line is reported, not failed.
- `DimensionRunTally` gains `RefLinesUnresolved` (no wall end within tolerance, no usable
  end face, or the wall too far off parallel) and `RefLinesUnattached` (touched no string).
  Both join the run summary's existing "Not marked:" block, which is where this tool
  already accounts for its silent causes.
- Unchanged: one unreadable reference line never aborts a run, and the run transaction
  still guarantees all-or-nothing on a hard Revit failure.

## Testing

`tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs`:

- An opening visible to both rings goes to the outer line even when the inner line is nearer.
- An opening walled off from every outer line falls to the nearest inner line.
- Nearest-wins still holds within a ring.
- A tie within a ring still goes to the earlier line.
- A wall crossed by one line of each ring is measured by both.
- `FindMatchesForLines` returns the same indices as the wrapper, each with its `T`.

New `tests/RVTuk.Core.Tests/AutoDimensions/RefLineMatcherTests.cs`:

- An endpoint touching a string matches it at the right `T`, and the target end is the
  other endpoint.
- A line crossing both strings matches both, at the right `T` on each.
- A line crossing a string mid-span and running past it still identifies the far endpoint
  as the target end.
- A line touching no string matches nothing.
- A line with both ends on strings is rejected for having no target end.

Plus: build `Release2024` and `Release2025`, and an in-Revit pass for

- first-run creation (open a project with none of the styles, show each pane, confirm all
  four appear in Line Styles and that a second refresh does not re-mark the document as
  modified);
- a ref line on a free-ending parallel wall, verified in the reference view **and** in a
  fanned-out view of the same level, before and after moving the wall;
- a ref line on a joined wall end, to confirm it reports rather than throws.

## Docs to update

`docs/tools/auto-dimensions/README.md`, `docs/tools/topo-tools/README.md`, both
`backlog.md` files, and the two feature blurbs in `CLAUDE.md`.
