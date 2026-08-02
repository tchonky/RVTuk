# Design: line style naming, first-run creation, and dimension rings

**Status:** approved design — not yet implemented.

Two tools drive their work off a dedicated line subcategory the user draws detail lines
on: Auto Dimensions (`Dimensions_Line`) and Topo Tools (`Topo_Line`). This design renames
both to the office's `_DP-` convention, makes both create their styles on first run with
no setup button, and splits Auto Dimensions' single style into an outer and an inner ring
that own their openings by priority.

## Goal

1. **Rename** the styles to `_DP-Outer Dim`, `_DP-Inner Dim` (Auto Dimensions) and
   `_DP-Topo Line` (Topo Tools).
2. **Drop the setup button.** Both tools create whatever styles are missing when their
   pane refreshes. Topo Tools' "Set up this project" banner and its whole setup path go.
3. **Rings.** An outer dimension string gets first claim on every opening it can see; the
   inner strings take only what no outer string can reach.

## Decisions made

- **No migration.** The old subcategories are left untouched and never read again. There
  are no existing models drawing on them, so nothing is stranded — confirmed with the
  user, and the reason this design carries no rename-in-place step.
- **Settings stay shared.** Both rings run with the same category ticks and the same
  dimension type. The ring changes *who owns an opening*, nothing else.
- **Walls are untouched by rings.** A wall crossed by three strings is still measured by
  all three, inner and outer alike — that is what a chained string is.

## Design

### 1. Names

| Tool | Was | Becomes |
|------|-----|---------|
| Topo Tools | `Topo_Line` | `_DP-Topo Line` |
| Auto Dimensions | `Dimensions_Line` | `_DP-Outer Dim` **and** `_DP-Inner Dim` |

A leading underscore, a hyphen and spaces are all legal in a Revit subcategory name (the
disallowed set is ``\ : { } [ ] | ; < > ? ` ~``), and the underscore sorts the pair to the
top of the Line Styles dialog.

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
one style, `LevelDiscoveryEventHandler` for both dimension styles. `CreateDimensionsEventHandler`
and `TopoRunner.Apply` keep ensuring inside their own run transactions, so a style deleted
between refresh and run cannot fail the run.

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

The behavioural change is one sort key in `CandidateMatcher.FindMatchIndicesForLines`.
Ownership of an opening goes from *nearest qualifying line* to **(ring, then distance,
then line order)**:

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

### 4. Callers

- `DimensionLineStyle`: `OuterLineStyleName` / `InnerLineStyleName` constants;
  `IsDimensionsLine` becomes `TryGetRing(CurveElement, out DimensionRing)`.
- `DimensionRunner.CollectReferenceLines` returns each line paired with its ring;
  `RunPair` passes the ring into `ReferenceLine`.
- `LevelScopeFinder` accepts a line on **either** style when deciding which view owns the
  reference lines.
- `TopoLineStyle`, `TopoRunner`, `TopoScope`: the renamed constant, and the setup path
  stripped per §2.
- The Auto Dimensions pane needs no change — it never names the style.

## Error handling

- A document that cannot take a transaction refreshes without its styles rather than
  failing; the missing style then simply yields no lines, which is the same state as a
  project nobody has drawn in yet.
- A style deleted between refresh and run is recreated by the run's own transaction.
- Unchanged: one unreadable reference line never aborts a run, and the run transaction
  still guarantees all-or-nothing on a hard Revit failure.

## Testing

Core tests in `tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs`:

- An opening visible to both rings goes to the outer line even when the inner line is nearer.
- An opening walled off from every outer line falls to the nearest inner line.
- Nearest-wins still holds within a ring.
- A tie within a ring still goes to the earlier line.
- A wall crossed by one line of each ring is measured by both.

Plus: build `Release2024` and `Release2025`, and an in-Revit pass for the first-run
creation (open a project that has neither style, show each pane, confirm the styles appear
in Line Styles and that a second refresh does not re-mark the document as modified).

## Docs to update

`docs/tools/auto-dimensions/README.md`, `docs/tools/topo-tools/README.md`, both
`backlog.md` files, and the two feature blurbs in `CLAUDE.md`.
