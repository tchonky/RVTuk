# DWG Exporter — backlog

## 🚀 New features (requested 2026-08-03)

- [x] **Different File Naming for different input** — shipped 2026-08-04: separate
  "Sheets" and "Views" naming dropdowns, applied per item, each with its own pattern and
  worked example below it, and `<View Name>` (the view's own name, the previous behaviour)
  as the views default. Laid out as two **rows** rather than the two columns the request
  suggested — the patterns are long enough that halving the width would wrap most of them
  in a 560px window. Say the word if you'd rather have columns.
  Also closes the 2026-07-16 caveat where a non-sheet current view got a rule-based PDF
  name and a view-name DWG name.
- [x] **Export from two or more models** — shipped 2026-08-04: a Models list of the open
  models; extra models match the active model's set and setups by name, and a missing
  setup either skips that model or is created in it (a missing *set*, and a read-only
  model, always skip). Unticking the extras is the "don't export from other models" option.
- [x] **Hability to choose different paths for pdfs and dwgs files.** — shipped
  2026-08-04: a "Separate folder for PDFs" checkbox revealing a second Location row.

All three: [spec](specs/2026-08-03-multi-model-and-naming-design.md),
[plan](plans/2026-08-03-multi-model-and-naming.md).

## 🚀 New features (requested 2026-07-16)

- [x] **Naming-rule editor** — shipped 2026-07-16 as a **hand-off**, not a custom
  editor ([spec](specs/2026-07-16-native-dialog-handoff-design.md)): "Edit…" posts
  Revit's own PDF Export dialog (`PostableCommand.ExportPDF`), whose pencil button is
  the real rule editor. The window closes first (posted commands run after the modal
  command ends); reopening re-reads setups and restores selections. Custom in-tool
  editors stay off the table unless the hop proves unusable.
- [x] **View/sheet set editor** — hand-off on the range row: posts **Publish Settings**
  (`PostableCommand.PublishSettings`, a dedicated set manager) and falls back to the
  PDF Export dialog (its pencil also manages sets) when Revit greys Publish Settings
  out for the current model. Naming rules can't move there — the rule editor exists
  only inside the PDF Export dialog.
- [x] **DWG export settings editor** — shipped as a hand-off straight to the native
  "Modify DWG/DXF Export Setup" dialog (`PostableCommand.ExportOptionsExportSetupsDWGOrDXF`).
- [x] **Export DWG + PDF together** — one command produces both file sets, same
  filenames (they already share the naming rules), same range, side by side. Shipped
  2026-07-16 ([spec](specs/2026-07-16-dwg-pdf-combined-export-design.md)): DWG/PDF
  format checkboxes, PDF always one file per sheet (Combine forced off), Revit itself
  evaluates the naming rule for PDFs. Caveat: for a non-sheet *current view*, the PDF
  name (rule-based) can differ from the DWG name (view name); sheets always pair.

## 🔬 To research (feasibility, requested 2026-07-16)

- [x] **Model-space export matching the sheet** — researched 2026-07-16, see
  [research/2026-07-16-model-space-and-layers.md](research/2026-07-16-model-space-and-layers.md).
  Verdict: impossible from Revit; AutoCAD's `EXPORTLAYOUT` does exactly this and is
  scriptable per batch (companion-script candidate below).
- [x] **Layer customisation beyond the native mapping** — researched 2026-07-16, same
  doc. Verdicts: per-wall-layer CAD layers — not achievable at export time (mapping is
  per category/subcategory; modifiers are whole-element); preserve coincident lines —
  already native in the DWG setup; nested-family-on-host-layer — solve in the family
  library (subcategory assignment) or via in-block layer remap in a CAD-side script.
- [ ] **Companion CAD-side script (umbrella follow-up)** — one accoreconsole batch
  emitted next to the exports covering the achievable wishes: stamp layout paper sizes,
  `EXPORTLAYOUT` flatten to model space, in-block layer remaps. Decide whether to build
  after reading the research doc.
- [ ] **Export Speed** - check if there is a way to improve the speed of exportation.
  DWG normally takes a lot of time. Maybe simplifying the DWG drawing could be a workaround.
  (Un-nested from the companion-script item above — it's its own question, not part of it.)
- [x] **Images inside the DWG as OLE, as a checkbox** — researched 2026-08-04, see
  [research/2026-08-04-embedding-images-in-dwg.md](research/2026-08-04-embedding-images-in-dwg.md).
  Verdict: **possible, but not from Revit** — it needs AutoCAD installed and an
  AutoCAD-side plugin that post-processes the exported DWGs. No Autodesk API can author an
  `Ole2Frame`'s payload, but you don't have to: let an AutoCAD command create the OLE
  object, then set the frame's `Position3d`/`ScaleWidth`/`ScaleHeight`/`Rotation` (all
  settable) to sit where the `RasterImage` was, and erase the original. DiRoots ProSheets
  does exactly this — its `DiRoots.ProSheets.Cad.dll` references `accoremgd`/`Acdbmgd` and
  its TypeRef table names `Ole2Frame`, `RasterImage`, `RasterImageDef` and
  `CommandMethodAttribute`. *(An earlier version of this entry said "impossible" — the API
  findings were right, the conclusion was not.)*
  **Not scheduled.** Cost is a hard AutoCAD dependency, a second deploy target
  (AutoCAD plugin + installer work), a visible AutoCAD session per run, and OLE's known
  plot-reliability problems. Try the title-block vectorisation below first.
- [ ] **eTransmit-style transmittal zip** — one archive per run holding the exported DWGs
  and everything they depend on (images, xref'd view drawings, resolvable SHX fonts) plus a
  `TRANSMITTAL.txt`, so the receiver gets no broken links or substituted text. Design
  approved 2026-08-04: [spec](specs/2026-08-04-transmittal-zip-design.md). Supersedes the
  raster-image-report idea — the zip covers it and more.

## Ideas / future

- Companion AutoCAD script for layout paper size: Revit's API can't set the exported
  layout's page setup (see README "Known limitation"), but we can measure the real
  title-block size (as the Rishui Zamin extractor already does) and emit one script
  alongside the DWGs that stamps every layout's paper size — exact ISO names for
  standard sheets, explicit width/height for custom ones (office is "mostly ISO, some
  custom"). Declined for now (2026-07-15) in favour of documenting the limitation.

- DXF output flavour: same window, list `ExportDXFSettings` alongside the DWG setups.

## To verify (in Revit, after deploy)

- Run the manual checklist in [plans/2026-07-15-dwg-exporter.md](plans/2026-07-15-dwg-exporter.md)
  Task 6 — especially the naming-rule **separator placement** check against the native
  PDF export's filenames (fix lives in `FileNameComposer.Compose` if they differ).
- 2026-08-03 batch (nothing below has been run in Revit yet):
  - [ ] A set mixing sheets and views, two different naming setups picked — each file is
        named by the rule for its own kind, and each row's example shows a file of that kind.
  - [ ] Same run with PDF ticked — every `.dwg` has a `.pdf` of the same basename, for
        views as well as sheets.
  - [ ] Views left on `<View Name>` — filenames unchanged from before this batch.
  - [ ] A views rule made only of sheet parameters — files fall back to view names and the
        summary says so, instead of the run aborting on duplicates.
  - [ ] "Separate folder for PDFs" — DWGs and PDFs land in their two folders; unticking it
        puts both back in one.
  - [ ] A second model whose set and setups have the same names — both models export.
  - [ ] A second model missing a setup, "Skip that model" — it is skipped and the summary
        names the missing setup.
  - [ ] Same, "Copy the setup into it" — it exports, and the setup is afterwards present in
        that model's own PDF Export / DWG setup dialog with its rule intact.
  - [ ] A second model open read-only — skipped with the read-only reason, never attempted.
  - [ ] Two models producing the same filename — the run aborts before writing and names
        both models.
  - [ ] "Current window" selected — the Models section is disabled.

## Done

- 2026-07-15 — implemented across all three layers + ribbon button; Core suite green
  (182 tests). Pending in-Revit verification.
- 2026-07-15 — design approved: PDF-setup naming rules, modal in-command dialog, no
  Options section ([spec](specs/2026-07-15-dwg-exporter-design.md)).
