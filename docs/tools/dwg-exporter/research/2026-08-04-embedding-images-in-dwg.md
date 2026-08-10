# Can the exported DWG carry its images inside the file?

**Date:** 2026-08-04 (verdict corrected the same day — see "Correction" below)
**Question:** the exported DWGs reference raster images as external files. Can the images
travel *inside* the DWG instead — as embedded data, or as OLE objects — ideally behind a
checkbox in the export dialog?

**Verdict: yes, but not from Revit alone.** It requires AutoCAD installed and an
AutoCAD-side plugin that post-processes the exported DWGs. DiRoots ProSheets ships exactly
this, and inspecting its assemblies showed how.

## Correction

This document first concluded "impossible". That was wrong. The API findings in sections
1–4 are accurate and still stand — **you cannot author an `Ole2Frame`'s payload through any
Autodesk API** — but the conclusion drawn from them did not follow. You do not have to
author the OLE object yourself: you can let AutoCAD's own command create it and then
*reposition* it through the very properties that are settable. That is what ProSheets does.
Section 6 has the evidence.

## 1. Revit exposes no such option

The complete DWG export option surface, dumped from the Revit 2024 reference assembly
(`Nice3point.Revit.Api.RevitAPI` 2024.3.40), across all three classes in the chain:

| Class | Properties |
|-------|-----------|
| `DWGExportOptions` | `MergedViews` |
| `ACADExportOptions` | `ACAPreference`, `ExportingAreas`, `ExportOfSolids`, `FileVersion`, `HatchBackgroundColor`, `LineScaling`, `LinetypesFileName`, `MarkNonplotLayers`, `NonplotSuffix`, `SharedCoords`, `TargetUnit`, `TextTreatment`, `UseHatchBackgroundColor` |
| `BaseExportOptions` | `Colors`, `HatchPatternsFileName`, `HideReferencePlane`, `HideScopeBox`, `HideUnreferenceViewTags`, `LayerMapping`, `PreserveCoincidentLines`, `PropOverrides`, and the font/layer/linetype/pattern tables |

Nothing image-related. A search of the whole API for `Embed` and `Raster` returns only
schedule/deck embedding, the image element's own parameters (`RASTER_SYMBOL_FILENAME` and
friends), and `PDFExportOptions.AlwaysUseRaster`. So whatever we do has to happen **after**
Revit has written the DWG.

## 2. The DWG format keeps raster images outside by default

A raster image in DWG is an `IMAGE` entity pointing at an `IMAGEDEF` object that stores a
**file path**; the pixels live outside the file. This is why AutoCAD has no "bind" for
raster images the way it does for DWG xrefs. An OLE object is the other mechanism, and it
*does* hold its data inside the drawing.

## 3. `Ole2Frame`'s payload is read-only

From `C:\Program Files\Autodesk\AutoCAD 2025\acdbmgd.dll`, **every settable property** on
`Ole2Frame`:

```
Position3d  Position2d  OutputQuality  AutoOutputQuality  Rotation
WcsWidth    WcsHeight   ScaleWidth     ScaleHeight        LockAspect
```

`OleObject` has a getter and no setter; `LinkName`, `LinkPath`, `IsLinked`, `Type` and
`UserType` have no setters at all. **This is placement and sizing — which turns out to be
exactly what you need once AutoCAD has made the object for you.**

## 4. The COM API and Civil 3D add nothing

- `IAcadOle` exposes `OleItemType`, `OlePlotQuality`, `OleSourceApp` (read/write), but
  `IAcadBlock`/`IAcadModelSpace`/`IAcadPaperSpace` have `AddRaster(...)` and **no `AddOle`**.
- `AeccDbMgd.dll` (Civil 3D, 9117 types) has no OLE or raster entity API at all. Civil 3D
  is a vertical on top of AutoCAD; generic entities still go through `acdbmgd`.

## 5. Therefore: creation must come from an AutoCAD command, not from the API

Pasting a bitmap from the Windows clipboard into AutoCAD creates an OLE object, and
`PASTECLIP` can be driven from `SendCommand`; `INSERTOBJ` does it via a dialog. These run
inside a full AutoCAD session — `accoreconsole` is headless and OLE needs a UI container.

## 6. Evidence: how DiRoots ProSheets does it

ProSheets is installed on this machine
(`C:\ProgramData\Autodesk\Revit\Addins\<year>\DiRoots.ProSheets2.1.2.0\`). Its CAD assembly
`DiRoots.ProSheets.Cad.dll` references only Autodesk assemblies — **no ODA/Teigha or other
third-party DWG SDK**:

```
REF accoremgd 23.0.0.0
REF Acdbmgd   23.0.0.0
```

The assembly is obfuscated (its own type names are mangled, string literals encrypted), but
an obfuscator cannot rename types referenced from *other* assemblies — those must stay
resolvable at runtime. Its TypeRef table contains:

```
Autodesk.AutoCAD.DatabaseServices.Ole2Frame
Autodesk.AutoCAD.DatabaseServices.RasterImage
Autodesk.AutoCAD.DatabaseServices.RasterImageDef
Autodesk.AutoCAD.DatabaseServices.BlockTableRecord / Transaction / ObjectId
Autodesk.AutoCAD.ApplicationServices.Document / DocumentCollection / Core.Application
Autodesk.AutoCAD.ApplicationServices.CommandEventHandler
Autodesk.AutoCAD.DatabaseServices.ObjectEventHandler
Autodesk.AutoCAD.Runtime.CommandMethodAttribute
Autodesk.AutoCAD.Runtime.LispFunctionAttribute
```

Public types include `ProSheets.Cad.AutoCadApp` and `ProSheets.Cad.CadInstallationChecker` —
so it is an in-process AutoCAD plugin, and it checks that AutoCAD is installed before
offering the feature.

**Proven:** it runs inside full AutoCAD, registers commands and LISP functions, and touches
both `RasterImage`/`RasterImageDef` and `Ole2Frame`.

**Inferred** (the exact command is hidden by string encryption): for each `RasterImage` in
the exported DWG, read its path and placement; have AutoCAD create an OLE object from that
image file; catch the new entity via the command/object events they subscribe to; set the
resulting `Ole2Frame`'s `Position3d` / `ScaleWidth` / `ScaleHeight` / `Rotation` to match;
erase the original `RasterImage`; save.

## What it would cost us to do the same

- **A hard AutoCAD dependency.** The feature can only work where AutoCAD is installed, and
  must degrade cleanly where it isn't — ProSheets ships a `CadInstallationChecker` for
  exactly this.
- **A second deploy target.** RVTuk currently ships Revit DLLs only. This adds an
  AutoCAD-side plugin, its own loading mechanism, and installer work in `Deploy.ps1` and
  `Build-Installer.ps1`.
- **A full, likely visible AutoCAD session** per run, driven through its command layer —
  slow, and (if clipboard-based) it takes over machine-global state.
- **Quality and plotting caveats remain.** OLE objects rasterise at a fixed output quality
  and are long known for plotting unreliably; `OlePlotQuality` / `AcOleQuality` preferences
  exist because of it. Accepted trade-off if the goal is a single self-contained file.

## Cheaper alternatives, still worth considering first

- **If the images are logos or title-block graphics** — the same few repeating on every
  sheet — redraw them as filled regions and lines in the **title-block family**. They then
  export as native DWG geometry: nothing to embed, no quality loss, no AutoCAD dependency,
  and it plots correctly everywhere.
- **For anything that must simply look right on its own**, the PDF the tool already exports
  embeds its images, with basenames matching the DWGs.
