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

## 4. The AutoCAD COM API says the same thing, more plainly

From `Autodesk.AutoCAD.Interop.Common.dll` / `Autodesk.AutoCAD.Interop.dll` (AutoCAD 2025):

- `IAcadOle` exists, exposing `OleItemType`, `OlePlotQuality`, `OleSourceApp` — read **and**
  write. So an OLE object already in a drawing can be inspected and adjusted.
- `IAcadBlock` / `IAcadModelSpace` / `IAcadPaperSpace` expose
  `AddRaster(path, insertionPoint, scale, rotation)` — which creates an **external**
  reference, exactly what we already get.
- There is **no `AddOle`** on any of them.

Autodesk ships an insertion method for the external form and none for the embedded form.

## 5. Civil 3D adds nothing here

Checked `C:\Program Files\Autodesk\AutoCAD 2025\C3D\AeccDbMgd.dll` (9117 types, so the
assembly loaded fully): no `Ole2*`, no raster-image, no embed-entity API at all. Expected —
Civil 3D is a vertical on top of AutoCAD, adding `Autodesk.Civil.*` types for alignments,
surfaces, corridors and pipe networks. Generic DWG entities still go through `acdbmgd`,
where the OLE payload is read-only. Civil 3D sits beside that layer rather than extending
it, so it cannot author what AutoCAD itself cannot.

## The one remaining route, and why it was rejected

Pasting a bitmap from the Windows clipboard into AutoCAD *does* create an OLE object, and
`PASTECLIP` can be driven through `SendCommand`. So a post-process script could launch a
**visible** AutoCAD (not `accoreconsole` — headless, and OLE needs a UI container), open
each exported DWG, and for each image push it to the clipboard and paste, then set the
frame's position and scale, which *are* settable.

Rejected because:

- it needs a full AutoCAD running visibly, driven through its GUI, one round-trip per image
  per drawing;
- the clipboard is global machine state — nobody can use the machine during a run, and
  anything else that copies mid-run corrupts the output;
- it is a new deploy surface (an AutoCAD-side plugin or COM host) for a toolkit that ships
  Revit DLLs only;
- and the result still plots unreliably. That `OlePlotQuality` and `AcOleQuality`
  preferences exist at all is Autodesk conceding OLE plotting is a problem area.

That is a lot of fragility to arrive at output that may not plot, for an issued set.

## What to do instead

- **If the images are logos or title-block graphics** — the common case, and the same few
  images repeat on every sheet — the fix is upstream, and it is better than embedding would
  have been: redraw them as filled regions and lines in the **title-block family**. They
  then export as native DWG geometry, there is nothing left to embed, the files shrink, and
  they plot correctly everywhere. A one-time job on a handful of families.
- **If they are genuine rasters** (site photos, survey scans, renders) they cannot be
  vectorised and will always be external files beside the DWG. Use the PDF for anything
  that must look right on its own, and hand the image files over with the DWG set.
- **For DWG handover generally**, the buildable mitigation is to report them:
  `ImageInstance` elements can be collected per exported view and their paths read via
  `GetExternalFileReference()`, so a run says which views carry images and which files must
  travel with the set. See the backlog.

## Don't retry this

As with the Massing & Site tab placement, this is settled. If the requirement returns, the
answer is PDF or an eTransmit-style bundle of DWG + image files — not embedding.
