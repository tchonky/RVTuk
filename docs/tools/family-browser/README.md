# Family Browser

**What it is:** the family-management tool — indexes the firm's `.rfa` library
(category, parameters, thumbnails) into a shared SQLite database and gives a
searchable/filterable dark-themed window over it, with load/update into the active
project, per-family rich-text instructions, tags, favourites, and custom thumbnails.
Settings (library root, ignored subfolders/patterns, deep scan) and Help/About live
inside the browser window itself (footer toggles).

**Status:** shipped (v1 launch surface).

**Names:** code `FamilyBrowser`; ribbon button "Family Browser" (internal id
`BrowseLibrary`). Older docs say "Family Explorer" / "Library Browser" — same tool.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/FamilyBrowser/` (Config, Database, Extraction, Models, Util) |
| UI    | `src/RVTuk.UI/FamilyBrowser/` (Views, ViewModels) |
| Revit | `src/RVTuk.Revit/FamilyBrowser/` (Commands, ExternalEvents, Extraction) |
| Tests | `tests/RVTuk.Core.Tests/FamilyBrowser/` |

## Docs

- [design.md](design.md) — living design (window layout, filters, settings panel)
- [backlog.md](backlog.md) — bugs / improvements / ideas / done
- [specs/](specs/) and [plans/](plans/) — dated historical designs and implementation plans
