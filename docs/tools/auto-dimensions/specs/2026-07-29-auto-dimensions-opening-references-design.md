# Auto Dimensions — occlusion instead of reach, and a chosen dimension type

**Date:** 2026-07-29
**Status:** Design, approved
**Supersedes:** the `OpeningReachMillimetres` setting introduced in
[2026-07-23-auto-dimensions-scope-pane-design.md](2026-07-23-auto-dimensions-scope-pane-design.md)

## Why

Two unrelated complaints about the same half of the tool — how openings are dimensioned.

**The reach setting earns its keep no longer.** It was introduced because an opening is measured
across its width, so it qualifies on walls running *along* a reference line, and a line never
touches such a wall. With no intersection to key on, "how far away may that wall sit" stood in for
"crossing", and the number was handed to the user because it is drafting convention rather than
geometry. Since then an opening goes to the single nearest line that qualifies, which settles the
question the setting was really being asked — *which* string dimensions this door — and leaves the
number doing only the crude part of the job. It is also the reason for the open bug at
[backlog.md](../backlog.md): a generous reach on a facade line reaches straight past the facade
into the parallel interior wall behind it.

**Openings do not report their height.** Revit's linear dimension types carry a *Show Opening
Height* switch that prints a door's or window's height under the width. The tool never chooses a
dimension type — `NewDimension` takes whatever type each target view happens to default to — so
the switch is out of reach, and a fan-out across several views is not even styled consistently.

## Change A — a wall between, not a distance

An opening qualifies for a reference line when all four hold:

1. its host wall is near-parallel to the line (`WallCrossingFinder.ParallelToleranceDegrees`,
   unchanged);
2. its centre projects strictly inside the line's span (unchanged);
3. **no other parallel wall stands between its host wall and the line** (new);
4. among the lines it qualifies for, this one is the nearest (unchanged).

Rule 3 replaces the reach test outright. There is no distance limit left: a door 30 m from the
line is dimensioned when nothing parallel stands in the way. That is the trade for deleting the
number, and it is the right one — a facade string should reach its whole facade and stop at the
first wall behind it, which is what "nearest wall wins" says and what a millimetre figure could
only approximate.

### Blocking, precisely

A wall segment blocks an opening from a line when:

- it is near-parallel to the line, by the same tolerance rule 1 uses;
- it lies on the **same side** of the line as the opening — signed perpendicular offsets share a
  sign;
- it is **strictly nearer** the line than the opening, by more than `BlockingEpsilon` (1e-6, in
  Revit's internal feet — the same order as the file's existing `BoundaryEpsilon`, and ample: a
  wall and its own opening differ by rounding alone, both having passed through the same link
  transform);
- its projection onto the line **covers the opening's station** — the opening's parameter `t`
  falls strictly inside the wall's own `[t0, t1]`.

The opening's own host wall excludes itself with no special case: a door's `LocationPoint` sits on
its host's location curve, so the two offsets are equal and "strictly nearer" fails. A wall the
line crosses is never parallel, so it never blocks. A wall that stops short of the opening's
station does not block, which is what lets a line see through a gap in a facade.

### What the matcher receives

`CandidateMatcher.FindMatchIndices` and `FindMatchIndicesForLines` drop the `alongsideReach`
parameter and take `IReadOnlyList<WallCandidate> occluders` instead. `TryGetAlongside` loses its
distance test and hands back a *signed* offset; ownership still compares magnitudes.

Occluders are deliberately a separate list rather than the `Crossing` entries of `Segments`,
because the two answer different questions — *what do we dimension* versus *what stands in the
way* — and the answers differ whenever the user unticks Walls.

### Walls are collected even when Walls is unticked

`DimensionCandidateSet` gains `Occluders`, filled from every straight wall reaching the view's cut
plane, host and linked, in host coordinates — **regardless of the checked categories**. Unticking
"Walls" must mean "do not dimension walls", not "pretend walls are not there"; otherwise it
silently changes which *openings* match.

Two details follow from that:

- `AddWalls` is restructured to always enumerate and always append to `Occluders`, appending to
  `Items`/`Segments`/`MatchModes` only when `DimensionCategories.Walls` is checked.
- `ExcludedNotCut` is only incremented for a wall when Walls **is** checked. That tally reports
  things the user asked for and did not get; a wall collected purely to occlude was never asked
  for.

### Removed

`ScopeDefaults` held nothing but `OpeningReachMillimetres` and goes with it. `DimensionRunner.RunPair`
and `CreateDimensionsEventHandler` lose the parameter and the millimetres-to-feet conversion. The
pane loses the "Openings within ___ mm of the line" row and its tooltip.

## Change B — the dimension type is chosen, and openings reference only their jambs

### The type

`AutoDimensionsScope` carries the project's linear dimension types as
`DimensionTypeInfo(long Id, string Name)`, alphabetical. A new `DimensionTypeFinder` collects
`DimensionType` elements whose `StyleType` is `DimensionStyleType.Linear`; should that filter come
back empty, it falls back to every `DimensionType` in the document rather than presenting an empty
list.

`ScopeSelection` swaps `OpeningReachMillimetres` for `DimensionTypeId`. Extensible storage has no
`long` field, so it is persisted as a string holding the invariant value — the convention the view
ids already follow. `ScopeSelectionStore` takes a new schema GUID and the name
`…ScopeSelectionV3`; a schema is immutable once registered, so a changed field means a new GUID.
Any V2 selection is orphaned and the pane falls back to its defaults once, exactly as the V1→V2
bump did.

`DimensionRunner.RunPair` takes a `DimensionType?` and uses the four-argument `NewDimension`
overload — **including inside `RemoveCoincidentReferences`**, which recreates the dimension and
would otherwise silently drop back to the view's default type. Null, or an id that no longer
resolves, falls back to today's three-argument call.

The pane replaces the reach row with a dimension-type dropdown in the same slot, restoring the
persisted id when it still exists and otherwise selecting the first entry. Ticking *Show Opening
Height* on the chosen type stays the user's business: the tool never edits a type it did not
create, and an office's dimension styles are not ours to change.

### The references

An opening contributes **exactly two references, its `Left` and its `Right`**. No other
`FamilyInstanceReferenceType` is ever consulted — not `CenterLeftRight`, not `Front`/`Back`, not
`Strong`/`Weak` — and no more than two references per opening ever reach the `ReferenceArray`.
This is what makes the dimension a jamb-to-jamb measure of one opening, which is the shape
*Show Opening Height* recognises; a stray centre or strong reference turns one clean segment into
two meaningless ones.

This is already what `TryAppendReferences` does. The change is to state it as an invariant the code
enforces and a test can defend, rather than a property it happens to have. Where either list comes
back empty the opening is skipped and counted in `ExcludedNoReferences`, unchanged.

`Left` and `Right` here are the reference plane's **Is Reference** property, not its name: a plane
named "Left" but flagged *Not a Reference* contributes nothing, and a plane named "Jamb A" flagged
`Left` does.

Should a family expose several `Left` (or `Right`) references — `GetReferences` returns a list, and
nested families are the likely source — the first is used, as today. Expected to be a list of one
in practice; choosing between several would need each reference's position, which family-instance
references do not readily give up, so it is left alone rather than guessed at.

### The unverified part

That *Show Opening Height* fires on `GetReferences(Left/Right)` references is an assumption. The
published guidance is that the trigger is a dimension placed on the references of a family
carrying a height parameter, which these are, but linked models are a documented sore spot. It is
a verification step in the plan, not a claim in this document.

## Tests

Core, `CandidateMatcherTests` — the reach cases become occlusion cases:

- an opening in the nearest parallel wall is matched;
- an opening with a parallel wall between it and the line is not;
- a parallel wall on the *other* side of the line does not block;
- a parallel wall that stops short of the opening's station does not block;
- a wall the line crosses never blocks;
- an opening's own host wall does not block it;
- a distant opening with clear sight is matched — the case the old reach test rejected;
- the existing multi-line ownership cases still hold, now with occluders present.

The Revit-side pieces (collection, reference resolution, dimension-type application) have no test
harness, as with the rest of `RVTuk.Revit`; they are covered by the in-Revit verification pass.

## Out of scope

- Editing or creating dimension types. The user ticks *Show Opening Height* themselves.
- Choosing between several `Left`/`Right` references on one family.
- Ceilings and floors as reference categories; the v2 item is untouched.
