# Scan Checkboxes (Thumbnails / Parameters) — Design

**Date:** 2026-07-01
**Status:** Approved (design)
**Area:** Family Explorer / Library Indexer
**Branch:** `scan-checkboxes`

> Supersedes the two-button part of
> [`2026-06-30-config-hub-deep-scan-design.md`](2026-06-30-config-hub-deep-scan-design.md)
> (Goal 3 / Component 4). Everything else in that spec — the Config hub window, ribbon button,
> ignored-subfolder skip, curated-data preservation — is unchanged and still applies.

## Problem / motivation

The Config window's two deep-scan buttons ("Scan New & Changed" / "Re-scan All Families") both
always re-extract thumbnail **and** parameters together for any family that "needs extraction,"
where that need is decided only by comparing the file's size/date against the DB row. Two
problems, both already tracked in `docs/BACKLOG.md`:

1. **No way to scan just one facet** (line 72: *"Option to deep-scan just thumbnails (fast)
   and/or just parameters (slow)"*). Parameter extraction requires opening the family in Revit
   (slow); thumbnail extraction is a plain file read (fast). Bundling them means a user who only
   wants fresh thumbnails pays the slow Revit cost anyway.
2. **A family the Family Browser's fast "Sync" already touched is invisible to the deep scan**
   (lines 69-71): `Sync` (`BrowserRepository.UpsertFamily`) writes the file's real size/date with
   no extracted metadata, so the deep scan's size/date check sees it as "unchanged" and skips it
   forever — it never gets a category, parameters, or thumbnail. This is why "Re-scan All
   Families" (force everything regardless of change) exists at all: it's the only way today to
   pick up such a family, at the cost of re-reading the whole library.

## Goals

1. Replace the two Config-window scan buttons with **one Scan button** plus two checkboxes:
   **Update thumbnails** and **Update parameters**.
2. Decouple the "needs extraction" decision per facet: a family needs its thumbnail refreshed if
   the file changed **or it has no thumbnail row yet**; independently, it needs its parameters
   refreshed if the file changed **or it has no parameters yet**. This fixes problem 2 above as a
   side effect — a Sync-only family has no thumbnail/parameter rows, so the first facet-scan
   after a Sync will pick it up regardless of its size/date matching.
3. With neither checkbox checked, Scan does a **filenames-only sync**: add rows for new files,
   remove rows for files no longer present. No Revit engine call.
4. With **Update thumbnails** checked (parameters unchecked), stay off the Revit main thread
   entirely — thumbnail extraction is a plain OLE/PNG read, not a Revit API call.
5. With **Update parameters** checked, extract category + parameters via the Revit engine (the
   slow path) — with or without thumbnails alongside.

## Non-goals

- No "force re-extract everything regardless of change" mode. The old "Re-scan All Families" only
  existed to work around problem 2; decoupled missing-data detection covers that case without a
  separate button. (If a genuine "re-pull everything, even unchanged" need shows up later — e.g.
  after fixing an extractor bug that produced *wrong but non-empty* data — that's a future,
  separately-scoped addition, not part of this change.)
- No change to the Family Browser's own "Sync" button/pipeline (`BrowserRepository.UpsertFamily`)
  — unrelated code path, stays as is.
- No change to curated-data preservation, ignored-subfolder handling, or the per-family "Rescan"
  button in the Family Browser detail pane — all unaffected.
- No new confirmation dialog. The old "Re-scan All" warning existed because forcing a full
  re-extraction was slow *and* indiscriminate; the new model only ever touches families that are
  actually missing or changed, so there's nothing surprising to confirm.

## Design

### Component 1 — `FamilyIndexer.Scan` (`RVTuk.Core`)

Signature changes from `bool forceReextractAll` to two flags; return type is unchanged
(`IReadOnlyList<ExtractionWorkItem>`):

```csharp
public IReadOnlyList<ExtractionWorkItem> Scan(
    Action<string, int, int> progressCallback,
    CancellationToken cancellationToken,
    bool includeThumbnails,
    bool includeParameters)
```

Per file, compute two independent booleans instead of one `needsExtraction`:

```csharp
bool fileChanged = existing == null
    || existing.FileSize != fileSize
    || Math.Abs((existing.ModifiedDate - modifiedDate).TotalSeconds) > 1;

bool needsThumbnail  = includeThumbnails  && (fileChanged || !hasThumbnail.Contains(existing?.Id ?? 0));
bool needsParameters = includeParameters && (fileChanged || !paramsExtracted.Contains(existing?.Id ?? 0));
```

`hasThumbnail` is a `HashSet<long>` of family Ids preloaded once per scan (new
`IndexRepository.GetFamilyIdsWithThumbnail()`, `SELECT DISTINCT FamilyId FROM Thumbnail`),
mirroring the existing `GetAllRelativePaths()` preload — keeps the loop free of per-file queries.
A thumbnail extraction that legitimately fails (returns null, e.g. the pre-existing OLE-read bug
in `docs/BACKLOG.md`) leaves no `Thumbnail` row, so that family is retried on every future
thumbnails-enabled scan — acceptable since a thumbnail read is a cheap local file parse, not a
Revit call, and retrying means a fix (or an upgraded family) gets picked up automatically.

`paramsExtracted` is **not** driven by row-count in the `Parameters` table, because a family can
legitimately have zero extractable parameters — using row existence as the signal would make such
a family "need parameters" forever, and re-extraction here means reopening it in Revit every scan
(the expensive path this whole feature exists to avoid paying needlessly). Instead, add a new
`Families.ParametersExtracted INTEGER NOT NULL DEFAULT 0` column, set to `1` unconditionally
inside `UpdateFamilyMetadata` (regardless of how many parameters were found), and preload the
`HashSet<long>` via `IndexRepository.GetFamilyIdsWithParametersExtracted()`
(`SELECT Id FROM Families WHERE ParametersExtracted = 1`).

Three outcomes per file:

- **Neither needed** (includes the "no checkboxes" case, and any unchanged/complete family):
  `IndexRepository.UpsertFamilyFileInfo(relativePath, fileName, fileSize, modifiedDate)` — a new
  method that writes the row (name/size/date, `ON CONFLICT DO UPDATE`) with the file's **real**
  size/date directly (nothing to extract afterward, so no sentinel/resumability dance needed).
  No work item queued.
- **Needs thumbnail only**: extract the PNG synchronously right here (as today,
  `ThumbnailExtractor.ExtractFromRfa`, no Revit needed) and commit it **immediately** via a new
  `IndexRepository.UpdateThumbnailOnly(familyId, png, revitYear, fileSize, modifiedDate)` — real
  size/date written in the same transaction as the thumbnail. No work item queued (no Phase 2 —
  this family never touches Revit's main thread).
- **Needs parameters** (with or without thumbnail): same as today's path —
  `InsertFamily` (sentinel size/date), extract the thumbnail here only `if (needsThumbnail)`
  (otherwise leave it null on the work item so `UpdateFamilyMetadata`'s existing
  `if (thumbnailPng != null)` guard leaves the existing Thumbnail row untouched), queue an
  `ExtractionWorkItem` for Phase 2. The real size/date commit stays deferred to
  `UpdateFamilyMetadata`, preserving today's cancel-leaves-row-stale resumability for the slow
  Revit-bound path.

`DeleteStaleEntries(scannedPaths)` keeps running unconditionally at the end, same as today, so
"neither checkbox" still prunes deleted files.

A new `ThumbnailOnlyCount` instance property (alongside the existing `SkippedLongPath` /
`SkippedIgnored`) counts thumbnail-only commits, for the completion summary dialog.

### Component 2 — `IndexRepository` (`RVTuk.Core`)

Four additions:
- **Migration:** `Families.ParametersExtracted INTEGER NOT NULL DEFAULT 0`, added in
  `MigrateSchema()` following the file's existing `ALTER TABLE … ADD COLUMN` pattern (see e.g. the
  `RevitYear`/`Tags`/`IsFavorite` migrations already there). Existing rows default to `0`
  (unextracted) — correct, since none of them have gone through the new column-setting code path
  yet, so the very next `includeParameters` scan naturally re-checks everything once.
- `GetFamilyIdsWithThumbnail()` (`SELECT DISTINCT FamilyId FROM Thumbnail`) and
  `GetFamilyIdsWithParametersExtracted()` (`SELECT Id FROM Families WHERE ParametersExtracted = 1`)
  — single-query preloads returning `HashSet<long>`.
- `UpsertFamilyFileInfo(relativePath, fileName, fileSize, modifiedDateUtc)` — filenames-only
  upsert with real size/date, mirroring `BrowserRepository.UpsertFamily`'s SQL shape.
- `UpdateThumbnailOnly(familyId, thumbnailPng, revitYear, modifiedDate, fileSize)` — single
  transaction: `INSERT OR REPLACE INTO Thumbnail`, plus `UPDATE Families SET RevitYear=…,
  ModifiedDate=…, FileSize=…, IndexedDate=…` (`Category`/`Parameters`/`ParametersExtracted`
  untouched).
- `UpdateFamilyMetadata` gains one line: its existing `catCmd` UPDATE also sets
  `ParametersExtracted = 1`, unconditionally (independent of whether the parameter list ended up
  empty).

### Component 3 — `IndexLibraryCommand.RunDeepScan` → `RunScan` (`RVTuk.Revit`)

Renamed, signature becomes `(UIApplication uiApp, AppConfig config, bool includeThumbnails, bool
includeParameters)`. Phase 2 (the `ExternalEvent` ping-pong loop) now only runs when
`workItems.Count > 0`, which is only possible when `includeParameters` was requested — so a
thumbnails-only or filenames-only scan finishes without ever raising the `IndexingExternalEvent`.
`IndexLibraryCommand.Execute` (the unused legacy `IExternalCommand.Execute`, not wired to any
ribbon button today — confirmed via `Application.cs`) is updated to compile
(`includeThumbnails: true, includeParameters: true`) but otherwise left alone; out of scope to
remove dead code here.

### Component 4 — `ConfigWindow` + `ConfigViewModel` (`RVTuk.UI`)

Replace the two-button `StackPanel` with one **Scan** button plus two `CheckBox`es bound to new
`ScanThumbnails` / `ScanParameters` bool properties on `ConfigViewModel` (default unchecked, i.e.
filenames-only is the default action — matches "press the button with nothing checked" being the
baseline case in the request).

`ConfigViewModel` constructor changes from two `Action` params (`scanNewAndChanged`, `rescanAll`)
to one: `Action<bool, bool> scan`. `RescanAllCommand` and `ConfirmAndRescanAll` are removed (no
confirmation dialog per Non-goals); `ScanCommand` = `new RelayCommand(() =>
_scan(ScanThumbnails, ScanParameters), () => IsConfigured)`.

Updated description text explains the three modes plainly (filenames only / + thumbnails / +
parameters, slower).

### Component 5 — `OpenConfigCommand` (`RVTuk.Revit`)

Replace the two `Action` delegates with one:
```csharp
Action<bool, bool> scan = (includeThumbnails, includeParameters) =>
    IndexLibraryCommand.RunScan(uiApp, ConfigManager.LoadConfig(), includeThumbnails, includeParameters);
```

## Data flow

```
Config window
  └─ Scan button (ScanCommand)
       └─ scan(ScanThumbnails, ScanParameters)
            └─ IndexLibraryCommand.RunScan(uiApp, config, includeThumbnails, includeParameters)
                 └─ FamilyIndexer.Scan(progress, ct, includeThumbnails, includeParameters)
                      ├─ neither needed        → UpsertFamilyFileInfo (no Revit)
                      ├─ thumbnail only needed → extract PNG + UpdateThumbnailOnly (no Revit)
                      └─ parameters needed     → work item queued
                           └─ Phase 2 (only if any work items): IndexingExternalEvent ping-pong
                                → Extractor.ExtractMetadata → UpdateFamilyMetadata
```

## Threading

Unchanged from today except that Phase 2 (the only Revit-main-thread work) is now conditionally
skipped entirely when no work items were queued — i.e. whenever `includeParameters` is false, or
true but nothing actually needed it. Thumbnail-only commits happen on the background `ThreadPool`
work item, same thread Phase 1 always ran on.

## Persistence

One new column: `Families.ParametersExtracted INTEGER NOT NULL DEFAULT 0`, added via the existing
`MigrateSchema()` `ALTER TABLE` pattern. Thumbnail detection reuses the existing `Thumbnail` table
(no column needed there — see Component 2 for why the two facets differ).

## Error handling

Unchanged: scan errors surface through the existing try/catch → `TaskDialog`; the completion
summary dialog gains a line for thumbnail-only updates when `ThumbnailOnlyCount > 0`.

## Testing

- **Unit (`FamilyIndexer.Scan`)**, replacing/extending `FamilyIndexerTests.cs`:
  - neither flag set → files are added/pruned (`GetAllRelativePaths` reflects the folder) but no
    work items, and no `Thumbnail`/`Parameters` rows are created.
  - `includeThumbnails:true, includeParameters:false` on a brand-new file → thumbnail committed
    directly (no work item returned), real size/date written immediately.
  - `includeParameters:true, includeThumbnails:false` on a brand-new file → work item queued with
    `ThumbnailPng == null`; after simulating `UpdateFamilyMetadata`, no `Thumbnail` row exists.
  - both set → today's existing behavior (work item carries the thumbnail).
  - **the Sync-bug fix**: a family upserted via `UpsertFamilyFileInfo` (real size/date, no
    thumbnail/parameters) is still picked up by a subsequent `includeThumbnails:true` and/or
    `includeParameters:true` scan even though its size/date match.
  - a family with a thumbnail already but no parameters: `includeThumbnails:true` alone returns no
    work item for it; `includeParameters:true` alone does.
  - **zero-parameter families don't loop forever**: after `UpdateFamilyMetadata` commits with an
    *empty* parameter list (a family that genuinely has none), a subsequent `includeParameters:true`
    scan returns no work item for it — proves detection uses `ParametersExtracted`, not parameter
    row count.
- **Manual in-Revit:** Scan with no checkboxes only adds/prunes; thumbnails-only visibly updates
  previews without any Revit "processing family" delay; parameters-only is the slow path and
  updates category/parameter data; both together matches today's full deep scan.

## Open questions

None blocking.
