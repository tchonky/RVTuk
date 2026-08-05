# Can the exported DWG carry its images inside the file?

**Date:** 2026-08-04
**Question:** the exported DWGs reference raster images as external files. Can the images
travel *inside* the DWG instead — as embedded data, or as OLE objects — ideally behind a
checkbox in the export dialog?

**Verdict: no.** Not from Revit, not from the AutoCAD API, and not from a headless batch
script. There is nothing for a checkbox to switch.

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
friends), and `PDFExportOptions.AlwaysUseRaster`. The native DWG export dialog offers no
such setting either — this is not an API gap over a UI feature.

## 2. The DWG format keeps raster images outside by design

A raster image in DWG is an `IMAGE` entity pointing at an `IMAGEDEF` object that stores a
**file path**; the pixels live outside the file. This is why AutoCAD has no "bind" for
raster images the way it does for DWG xrefs, and why Autodesk's answer for packaging is
eTransmit, which copies the referenced files alongside.

## 3. OLE objects exist, but cannot be authored programmatically

`Ole2Frame` is a real entity type with a public parameterless constructor. Dumped from
`C:\Program Files\Autodesk\AutoCAD 2025\acdbmgd.dll`, **every settable property** on it is:

```
Position3d  Position2d  OutputQuality  AutoOutputQuality  Rotation
WcsWidth    WcsHeight   ScaleWidth     ScaleHeight        LockAspect
```

Geometry and render quality only. The payload members are read-only — `OleObject` has a
getter and no setter, and `LinkName`, `LinkPath`, `IsLinked`, `Type` and `UserType` have no
setters at all. You can construct an OLE frame, place it, scale it and rotate it, and never
give it content. The managed API can *read* OLE objects; it cannot author one.

That leaves the interactive path only: `INSERTOBJ` / `OLEOBJECT` opens the Windows "Insert
Object" dialog, or clipboard → `PASTESPEC`. Both need a GUI and a human, once per image.
`accoreconsole` — the batch engine the companion-script backlog item is built around — is
headless and can drive neither.

Even done by hand the result is poor for an issued set: OLE objects are long known for
plotting at the wrong quality or not plotting at all, they are Windows-only, and
non-Autodesk consumers render them badly.

## What to do instead

- **For a genuinely self-contained file, use the PDF the tool already exports.** PDF
  embeds its images, and the `.pdf` basenames already pair with the `.dwg` ones.
- **For DWG handover, the images must travel alongside.** The buildable mitigation is to
  report them: `ImageInstance` elements can be collected per exported view and their paths
  read via `GetExternalFileReference()`, so a run can say which views carry images and
  which files must go with the set. See the backlog.

## Don't retry this

As with the Massing & Site tab placement, this is settled. If the requirement returns, the
answer is PDF or an eTransmit-style bundle of DWG + image files — not embedding.
