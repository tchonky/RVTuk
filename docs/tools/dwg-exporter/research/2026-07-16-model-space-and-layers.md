# Research: model-space sheet export & layer customisation beyond native

**Date:** 2026-07-16 · **Status:** findings, no implementation decision yet.
Answers the two 🔬 items in [../backlog.md](../backlog.md).

## Q1 — Can the DWG's *Model* tab show the sheet as it looks in Revit?

**No — not from Revit.** The export API composes only the paper-space layout like the
sheet; model space receives the source views' geometry (spread out or as xrefs,
depending on the setup), never arranged like the sheet. Nothing in `DWGExportOptions`
or elsewhere in the API touches model-space composition.

**But AutoCAD has a purpose-built command for exactly this:**
[`EXPORTLAYOUT`](https://help.autodesk.com/view/ACD/2025/ENU/?guid=GUID-8A07D165-559E-4DF2-B2A9-42BD6AE2DB70)
flattens the current layout — visible objects, trimmed to viewport edges, transformed
and scaled to paper coordinates — into the model space of a **new** drawing
([details](https://help.autodesk.com/view/ACD/2025/ENU/?guid=GUID-653D3843-AFE5-4569-959F-E06F3866D7D7)).
Caveats: one layout per run; an object visible in several viewports becomes several
objects; 3D geometry stays 3D (may need `FLATTEN` after).

**Feasible automation:** a batch script for **AutoCAD Core Console**
(`accoreconsole.exe`, ships with every AutoCAD) that opens each exported `.dwg`, runs
`EXPORTLAYOUT`, and saves `<name>-Model.dwg` (or replaces model space in place). RVTuk
could emit this script next to the exports — the same delivery vehicle as the
paper-size-stamping idea already in the backlog. Requires AutoCAD on the machine that
runs the script, which the office has.

## Q2 — Layer customisation beyond the native export setup

### 2a. Separate CAD layers per wall layer (structure / finish / …)

**Not feasible at export level.** The mapping table works per **category/subcategory**
([layer mapping](https://help.autodesk.com/cloudhelp/2022/ENU/Revit-DocumentPresent/files/GUID-3541F82B-9E25-47C5-BE52-3F6C6F40DA02.htm)),
and the lines *inside* a cut wall (between structure/substrate/finish) are not
subcategories — they're host-layer function line styles, indistinguishable in the
export. [Layer modifiers](https://forums.autodesk.com/t5/community-blog-aec-english/using-layer-modifiers-for-a-more-accurate-dwg-export-in-revit/ba-p/13031440)
(Workset, Phase Status, Custom 1–3, …) work at **whole-element** granularity — they can
split wall *types* onto different layers
([Autodesk's wall-type recipe](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-export-a-DWG-from-Revit-with-separate-layers-based-on-different-Wall-Types.html)),
never layers within one wall. Post-processing can't help either: once exported, the
lines carry no memory of which wall function they came from.
*Long-shot worth a 30-minute Revit test:* the "create new layers for overrides" export
option gives overridden elements their own layers — if host-layer function lines are
overridden in V/G, they might land on such a layer, but at best one generic override
layer, not per-function. Expectations low.

### 2b. Preserve coincident lines

**Already native** — the "Preserve coincident lines" checkbox in the DWG export setup
(General tab; API `PreserveCoincidentLines`). It combines freely with any mapping;
nothing to build. (This was also the reason the tool's Options section was dropped —
the chosen setup already carries it.)

### 2c. Nested families on the host family's layer (door + nested detail family)

Families export as **blocks** on the host category's layer; inside the block, entity
layers follow the *subcategory* assignments made in the family editor, and layer
modifiers (except Phase Status, Underlay, View Type) don't apply inside blocks
([family layers article](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-export-a-DWG-from-Revit-with-different-layers-based-on-Family-elements.html),
[subcategories-in-blocks discussion](https://www.revitforum.org/forum/revit-architecture-forum-rac/architecture-and-general-revit-questions/38538-exporting-objects-to-dwg-subcategories-without-exploding)).

Two real fixes:
1. **Family authoring (clean, recommended):** in the door family, assign the nested
   detail family's geometry to a Doors subcategory — it then exports on the door layer
   with no tooling at all. A library-hygiene task, not an exporter feature.
2. **Post-process script:** an accoreconsole LISP pass that remaps entity layers inside
   chosen block definitions (e.g. "everything on layer X inside door blocks → door
   layer"). Feasible, blunt, and shares the same companion-script vehicle as Q1.

## Conclusion

Every "beyond native" wish that's actually achievable converges on **one future
sub-project: a companion CAD-side script** (accoreconsole batch emitted next to the
exports) that can do any combination of: stamp layout paper sizes, flatten layouts to
model space (`EXPORTLAYOUT`), and remap in-block layers. Nothing here is achievable
from the Revit API side. The per-wall-layer wish is not achievable at all at export
time; the coincident-lines wish already works natively; the nested-family wish is best
solved in the family library.
