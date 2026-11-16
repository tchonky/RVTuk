# RVTuk — Backlog (toolkit-wide)

Toolkit-wide items only: installer, deploy, ribbon/host, cross-tool concerns.
**Per-tool backlogs** (bugs, improvements, ideas for one tool) live next to each tool:

- [Family Browser](tools/family-browser/backlog.md)
- [Rishui Zamin](tools/rishui-zamin/backlog.md)
- [Auto Dimensions](tools/auto-dimensions/backlog.md)
- [Neo Properties](tools/neo-properties/backlog.md)

For the product vision and roadmap see [`../VISION.md`](../VISION.md).
Check off with `[x]` and the commit hash when shipped.

---

## ▶ Status (read me first)

- Branch: **`master`** (the old `family-explorer` branch work is merged and shipped).
- **v1 launch surface:** Family Browser, Rishui Zamin (Area Calc), DWG Export and Auto
  Dimensions are registered on the one "RVTuk" ribbon panel; Neo Properties is
  code-complete but hidden behind `RegisterNeoProperties` in
  `src/RVTuk.Revit/Application.cs`.
- Working style: replies terse; **minimal code comments**; run git for the user
  (git novice) and explain simply; move items to the right backlog's Done section
  with the commit hash as they ship.

## 🐞 Bugs / things to fix

- [~] When closed, Revit crashes (access violation `c0000005` in `siappdll.dll`).
      **Diagnosed: NOT RVTuk.** `siappdll.dll` / `3DxRevit.dll` is the 3Dconnexion
      SpaceMouse driver. The crash is on a non-main native thread during Revit
      shutdown; every RVTuk journal entry is a normal startup/command event. Fix is on
      3Dconnexion's side: update or temporarily disable the 3Dconnexion add-in /
      SpaceMouse driver. (CER dump: `…\Local\Autodesk\CER\92ed161c…\29`.)
- [ ] NuGet restore reports known advisories for two packages (seen 2026-11-16):
      `SQLitePCLRaw.lib.e_sqlite3` 2.1.6 (NU1903, high — comes with `Microsoft.Data.Sqlite`
      8.0.29) and `OpenMcdf` 3.1.2 (NU1902, moderate — pinned to match what other Revit
      add-ins preloaded, which no longer holds: pyRevit preloads 3.1.4). Decide: a newer `Microsoft.Data.Sqlite`, and whether the OpenMCDF
      pin can move (see the add-in split spec, "Known risks carried forward").

## ✨ Improvements

*(none tracked)*

## ✅ Done

- [x] `Deploy.ps1`: per-version resilience (skip a locked/open Revit year), version
  filter, colored summary (`621f880`).
- [x] Standalone `RVTukSetup.exe` installer for other computers (`49abd6e`).
- [x] Tool-first repo reorganization
  (see [toolkit/specs/2026-07-15-repo-reorganization-design.md](toolkit/specs/2026-07-15-repo-reorganization-design.md)).
