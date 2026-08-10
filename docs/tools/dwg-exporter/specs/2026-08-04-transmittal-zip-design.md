# eTransmit-style transmittal zip — design

**Date:** 2026-08-04
**Status:** approved design
**Parent tool:** DWG Exporter (`DwgExporter`)

## Problem

A DWG exported from Revit is not self-contained. Raster images are external references, and
when the DWG setup has `MergedViews` off, Revit exports the views on a sheet as separate
xref'd drawings. Send someone the `.dwg` files alone and they open with broken links, and
with substituted text wherever the setup maps to an SHX font they don't have.

The user wants one archive per run holding the drawings and everything they depend on —
what AutoCAD's eTransmit produces for a drawing set.

Chosen over embedding images into the DWG as OLE, which is possible but needs an
AutoCAD-side plugin and a hard AutoCAD dependency
([research](../research/2026-08-04-embedding-images-in-dwg.md)).

## The constraint that shapes everything

**We cannot parse DWG.** Real eTransmit interrogates a finished drawing for its references;
we have no DWG reader and are not adding one. So the bundle's contents cannot be discovered
from the drawings — they have to be observed as the export produces them.

## Decisions

1. **Snapshot diff, not a glob.** The output folder is enumerated before the export and
   again after; every file that is new, or whose size or last-write time changed, is the
   run's output. This catches whatever Revit emits — images, and the xref'd view drawings
   whose names Revit invents — without knowing their names in advance. A glob for known
   extensions was rejected for exactly that reason: it would miss the xref case, which is
   the main thing the user is trying to fix.
2. **One archive per run**, written into the DWG output folder as
   `<set name>_<yyyy-MM-dd_HHmm>.zip`. Multi-model runs already share one output folder, so
   one zip covers them, and an image used by several models is stored once. For a
   current-window run the set name is empty, so the active model's title is used instead.
   The name is passed through `FileNameComposer.Sanitize`.
3. **Entries are stored flat**, no directory structure. A DWG references its images and
   xrefs by bare filename, so flattening is what makes them resolve when the receiver
   unzips. All produced files come from one folder, so basenames cannot collide.
4. **`.pdf` is excluded by extension**, so a run that writes PDFs into the same folder does
   not contaminate the CAD bundle. The archive itself is excluded too.
5. **Loose files are kept.** The zip is written alongside what it contains. A zip failure
   can then never cost the export, and a drawing can still be opened without unzipping.
6. **SHX fonts are copied, TrueType is only listed.** Each `DestinationFontName` in the
   chosen DWG setup's `ExportFontTable` is looked for as `<name>` and `<name>.shx` under the
   AutoCAD font folders found on the machine (`C:\Program Files\Autodesk\*\Fonts`). A hit is
   copied in; a miss is reported as a font the receiver must already have. TrueType fonts
   are not copied because redistributing them generally breaches their licence — and a
   receiver almost always has the common ones. The missing-SHX case is the real "wrong
   fonts" problem, and that is the one this covers.
7. **A `TRANSMITTAL.txt` goes in the archive**, listing what the bundle is and — more
   importantly — what it does *not* contain, so the receiver knows what to install.
8. **Zipping never fails the export.** It runs after a successful export; any error is
   reported as an error line with the drawings intact on disk. A file locked by another
   application is skipped and named in the report rather than aborting the archive.

## Changes by layer

### Core (`src/RVTuk.Core/DwgExporter/`)

| File | Responsibility |
|------|----------------|
| `FolderSnapshot.cs` *(new)* | `FileStamp` (`Path`, `Size`, `ModifiedUtc`) plus `FolderSnapshot.Diff(before, after)` → the paths in `after` that are absent from `before` or differ in size or timestamp. Pure: the caller enumerates the directory and hands in two lists. |
| `Transmittal.cs` *(new)* | `TransmittalKind` (`Drawing`, `Image`, `Font`, `Other`), `TransmittalEntry` (`SourcePath`, `EntryName`, `Kind`), `ResolvedFont` (`Name`, `FilePath` — null when not found), `TransmittalContents` (`Entries`, `FontsNotIncluded`, `Warnings` as a mutable `List<string>` the caller can add to), `TransmittalInfo` (`CreatedUtc`, `SheetSetName`, `ModelTitles`, `DwgSetupName`, `SheetNamingSetupName`, `ViewNamingSetupName`). |
| `TransmittalBuilder.cs` *(new)* | `Build(producedFiles, fonts, archivePath)` → `TransmittalContents`. Applies decisions 3, 4 and 6: drops `.pdf` and the archive path, classifies by extension (`.dwg` → Drawing; `.png/.jpg/.jpeg/.bmp/.tif/.tiff/.gif` → Image; else Other), turns resolved fonts into Font entries and unresolved ones into `FontsNotIncluded`. Pure. |
| `TransmittalReport.cs` *(new)* | `Render(info, contents)` → the `TRANSMITTAL.txt` text. Pure. |
| `TransmittalWriter.cs` *(new)* | `Write(archivePath, contents, reportText)` → warnings. The only piece that touches disk: creates the archive with `System.IO.Compression`, adding each entry and the report. A file that cannot be read is skipped and named in the returned warnings, which the runner puts in the run summary (they arrive too late for the report — see Error handling). |
| `DwgExportTypes.cs` | `DwgExportRequest` gains `CreateTransmittalZip` (bool, default false). |
| `DwgExportSettingsStore.cs` | Carries the new flag. |

`RVTuk.Core.csproj` gains, for `net48` only, `<Reference Include="System.IO.Compression" />`
and `<Reference Include="System.IO.Compression.FileSystem" />` — framework assemblies,
following the existing `System.Drawing` pattern. net8 has both in-box.

### Revit (`src/RVTuk.Revit/DwgExporter/`)

| File | Responsibility |
|------|----------------|
| `FontCollector.cs` *(new)* | `FontFolders()` → the `Fonts` directories under `C:\Program Files\Autodesk\*`. `Collect(doc, dwgSetupName)` → `IReadOnlyList<ResolvedFont>` plus warnings: reads `GetDWGExportOptions().GetExportFontTable()`, walks `GetValues()` for each `DestinationFontName`, and resolves it against those folders. When no font folder exists at all, that is a warning in its own right rather than a silently empty list. |
| `DwgExportRunner.cs` | Takes the before-snapshot at the start of `Run` and the after-snapshot once every model has exported, then builds and writes the archive. Adds the archive path — or the failure — to the result. |

### UI (`src/RVTuk.UI/DwgExporter/`)

A `Bundle DWGs into a zip (eTransmit-style)` checkbox in the Location section, enabled only
when the DWG format is ticked (a PDF-only run has nothing to bundle). The archive path is
reported in the run summary.

### Config

`DwgExportCreateZip` (bool) — `false`, the absent-key value, means no archive, which is
today's behaviour. Same net48 uninitialized-object rule as every other key.

## Data flow

```
Run starts
  → enumerate output folder            → before[]
  → export every model (unchanged)
  → enumerate output folder            → after[]
  → FolderSnapshot.Diff(before, after) → produced[]
  → FontCollector.Collect(...)         → fonts[] + warnings[]
  → TransmittalBuilder.Build(produced, fonts, archivePath)
  → TransmittalReport.Render(info, contents)
  → TransmittalWriter.Write(archivePath, contents, reportText)
  → archive path (or error) into the run summary
```

## Error handling

- Export succeeds, zip fails → an error line naming the reason; drawings remain on disk.
- A produced file is locked → skipped and named **in the run summary only**. The report is
  rendered before the writer runs, so it cannot know about a lock discovered at write time;
  warnings known earlier (the font ones) do reach the report. Not worth a second pass to
  unify — the summary is what the user is looking at when the run ends.
- The diff is empty → no archive is written, and the summary says so.
- No AutoCAD font folder on the machine → no SHX is copied; the report states plainly that
  fonts could not be collected, rather than implying none were needed.
- The checkbox is off → nothing changes anywhere, including no snapshots taken.

## Testing

**Core (`tests/RVTuk.Core.Tests/DwgExporter/`)**

- `FolderSnapshotTests` — a new path is reported; a path whose size changed is reported; one
  whose timestamp changed is reported; an untouched path is not; a path present before and
  absent after is not (nothing was produced).
- `TransmittalBuilderTests` — `.pdf` excluded; the archive path excluded; extensions map to
  the right `TransmittalKind`; entry names are bare filenames; a resolved font becomes a
  `Font` entry; an unresolved one lands in `FontsNotIncluded` and not in `Entries`.
- `TransmittalReportTests` — the report names the models and the setups, groups entries by
  kind, lists fonts that were *not* included with the reason, and states when no font source
  was found.
- `TransmittalWriterTests` — writes a real archive into a temp folder and reads it back: the
  entries and `TRANSMITTAL.txt` are present with flat names; a missing source file is
  skipped and returned as a warning rather than throwing.
- `DwgExportSettingsStoreTests` — the new flag defaults off and round-trips.

**In Revit (manual)** — a set whose sheets contain raster images; a run with the DWG setup's
`MergedViews` off, confirming the xref'd view drawings land in the archive; unzipping
somewhere else and opening a drawing with no broken links; a setup mapping to an SHX font,
confirming it is collected; the same on a machine without AutoCAD, confirming the report
says so.

> **Confirmed by the user (2026-08-04):** Revit writes the exported images into the same
> folder as the DWG, so the snapshot picks them up with no path resolution needed. Still
> worth ticking off in the checklist, but it is no longer a design risk.

## Out of scope

- Per-drawing archives. One bundle per run was chosen; a `.zip` per DWG can be added later
  if a recipient ever needs them separately.
- PDFs in the archive. They are self-contained and usually travel separately.
- Deleting the loose files after zipping.
- Reading a finished DWG to discover its true references — that needs a DWG parser.
