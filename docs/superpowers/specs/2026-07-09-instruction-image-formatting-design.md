# Instructions Editor — Image Formatting via Right-Click (+ drag-drop fix)

**Date:** 2026-07-09
**Status:** Proposed
**Area:** Family Management — Instructions Editor (VISION pillar 1)
**Branch:** `instruction-image-formatting`

> Follow-up to the 2026-07-07 Family Browser redesign (commit `0ec5192`), which removed the
> separate Gallery in favour of images embedded inline in the rich-text instructions. That makes
> inline-image editing a load-bearing feature — and today it is broken/fragile (see Problem). This
> spec replaces the mechanism.

---

## Problem

Inline-image editing in `InstructionsEditorWindow` currently works through a **selection-driven
floating overlay**: left-clicking an image is meant to summon a `Canvas` of resize/crop `Thumb`
handles plus a small toolbar (S/M/L/Full/Crop/Remove), hosted in the `RichTextBox`'s
`AdornerLayer` via a `SingleChildAdorner`. Two concrete failures, both reported in real use:

1. **Drag-drop onto the editor body does nothing.** The `RichTextBox`'s internal `TextEditor`
   vetoes file drops before the bubbling `Editor.Drop` handler runs, so nothing is inserted. (The
   *thumbnail* drop works because it targets a plain `Image` with no competing internal handler.)

2. **No crop/resize affordance.** The overlay is undiscoverable (it needs a click, not a hover,
   and users don't know that) and unreliable — the code comments document a long fight with WPF
   (`Image.PreviewMouseLeftButtonDown` never firing for an `InlineUIContainer` child, adorner
   layout re-derived on every scroll/reflow, toolbar flip math). It fights left-click, which the
   text editor treats as sacred (caret placement).

## Decision

Replace the overlay entirely with a **right-click `ContextMenu`** for all discrete commands, move
**crop into a self-contained modal dialog**, and **fix editor drag-drop**. Delete the whole
overlay/adorner/thumb apparatus (~200 lines). Chosen over "augment the overlay" and "keep inline
crop handles" because it removes the fragile machinery rather than patching it.

### Why right-click is the robust foundation

- `ContextMenu` is a first-class WPF service that opens reliably over an inline image — no
  AdornerLayer coordinate math, no scroll/reflow re-layout.
- Right-click is free; left-click stays caret placement, so we stop fighting the `TextEditor`.
- It is the conventional pattern for formatting an embedded image (Word-style).
- Crop and free-drag resize are the only direct-manipulation gestures a menu can't express: size
  presets replace resize, and crop moves to a modal where the same handle logic works without a
  RichTextBox underneath it.

---

## Design

### 1. Right-click menu (entry point)

A single `ContextMenu` is opened from an **Editor-level** `PreviewMouseRightButtonUp` handler —
the same reliable tunnelling pattern the old code used for left-click selection:

```
Editor.PreviewMouseRightButtonUp:
    img = FindImageFrom(e.OriginalSource)          // existing helper, walks up to the inline Image
    if img != null:
        _menuTargetImage = img
        ImageMenu.PlacementTarget = Editor
        ImageMenu.Placement = MousePoint
        ImageMenu.IsOpen = true
        e.Handled = true                            // suppress the RichTextBox's own cut/copy menu
    // else: leave default behaviour (caret / text menu)
```

State is one field, `Image? _menuTargetImage`; every command below operates on it. No global
"selected image", no overlay. Menu items:

- **Size ▸** (submenu, §2)
- **Replace image…** (§3)
- **Crop…** (§4)
- **Remove image** (§5)

The `ContextMenu` is defined once in `Window.Resources` and reused; it is not per-image, so there
is nothing to attach/detach as images come and go.

### 2. Size submenu

Reuses today's preset widths, driven by the existing `ApplyPreset(double width)` (sets
`image.Width`; aspect ratio holds via `Stretch="Uniform"`, no `Height` set):

| Item | Width |
|---|---|
| Small | 160 |
| Medium | 320 |
| Large | 480 |
| Full width | `Math.Max(80, Editor.ActualWidth - 40)` |
| **Custom width…** | numeric prompt (see below) |

**Custom width…** opens a small inline `Popup` — mirroring the existing **Insert-Table** popup
(`TablePopup`) already in this window — with a numeric field (pre-filled with the current width,
clamped to a sane range e.g. 40–2000) + **Apply**. On Apply it calls `ApplyPreset(value)`. Reusing
the local popup pattern means **no new window file**. This is the only exact-size path now that
free-drag resize is gone.

### 3. Replace image…

Opens an `OpenFileDialog` (`*.png;*.jpg;*.jpeg;*.bmp`), converts to PNG (`ConvertToPng`), decodes
(`DecodePng`), and swaps **both** `_menuTargetImage.Source` and its `ImageData` (§8). Width is
preserved (it's a replace, not a resize).

### 4. Crop… — modal `ImageCropWindow`

A new small modal `Window` (`Views/ImageCropWindow.xaml` + `.xaml.cs`):

```
┌ Crop image ─────────────────────┐
│   ░░░░░░░░░░░░░░░░░░░░░░░░░░░░    │   image scaled to fit the window
│   ░░┌──────────────┐░░░░░░░░░    │   crop rect: 4 corner Thumbs + body drag
│   ░░│              │░░░░░░░░░░    │   dimmed mask over the excluded region
│   ░░└──────────────┘░░░░░░░░░    │   freeform (no aspect lock)
│                    [Cancel] [Apply] │
└─────────────────────────────────┘
```

- **Input:** the target image's current `BitmapSource`.
- **Interaction:** the crop-rectangle chrome (masks, dashed `CropRect`, four corner `Thumb`s,
  body-drag `Thumb`, `ClampCrop`) is lifted almost verbatim from today's overlay — it was never the
  problem; hosting it over a `RichTextBox` was. In a plain `Canvas` in a normal `Window` it just
  works.
- **Apply:** runs the existing crop math (display→pixel scale, `CroppedBitmap` over an
  `Int32Rect`, clamped to bounds) and exposes the result as `BitmapSource? Result` /
  `DialogResult = true`.
- **Caller** (`Crop_Click`): if `ShowDialog() == true`, set `_menuTargetImage.Source = Result`,
  re-encode to PNG, update `ImageData` (§8), and set `Width` to the image's current on-screen width
  clamped to the cropped pixel width, so the cropped region keeps a sensible size.

Freeform only for now; aspect-ratio presets (1:1, 16:9) are out of scope (see Future).

### 5. Remove image

Reuses today's removal logic, retargeted from `_selectedImage` to `_menuTargetImage`: find the
`InlineUIContainer` whose wrapped `Image` is the target and remove it from its `Paragraph`.

### 6. Hover affordance (answers "nothing shows on hover")

Each inline image stays wrapped in the existing selection `Border`, now repurposed as a **hover
cue** rather than a selection highlight:

- On `IsMouseOver`, the `Border` shows a thin accent outline (`Brush.Accent`); transparent
  otherwise. Implemented with a `Style` + `Trigger` applied to the wrapper `Border` (no code-behind
  per image).
- Each image gets `ToolTip = "Right-click to resize, crop, or replace."`

So an image visibly signals it's interactive without requiring a click.

### 7. Drag-drop into the editor (independent bug fix)

Replace the bubbling `Editor.Drop` wiring with the tunnelling pair the `RichTextBox` actually
respects:

- `Editor.PreviewDragOver`: set `e.Effects = Copy` when the payload is `FileDrop` (image
  extensions) **or** `Bitmap`; else `None`. `e.Handled = true`.
- `Editor.PreviewDrop`: insert the image(s) and `e.Handled = true`. Handles both:
  - `FileDrop` — each `*.png/.jpg/.jpeg/.bmp` file → `InsertImageIntoEditor`.
  - `Bitmap` — image dragged from a browser/other app → encode to PNG → insert.
- Insert **at the drop point**: move the caret via `Editor.GetPositionFromPoint(e.GetPosition(Editor), true)` before inserting, so images land where dropped rather than at the old caret.

Ctrl+V paste (`Editor_PreviewKeyDown`) and the thumbnail drop are unchanged.

### 8. Persistence invariant (unchanged, called out because it's the fragile part)

Images survive save/reload through `RichTextBoxHelper`:

- Each `Image` carries its PNG as base64 in the `ImageData` attached property.
- `SerializeDocument` nulls stream-backed `Source`s (unserializable), saves XAML, restores them.
- `ParseDocumentXaml` → `RehydrateImages` rebuilds `Source` from `ImageData` on load.

**Every mutation above (resize, replace, crop) must keep `ImageData` in sync** — resize doesn't
change bytes, but replace and crop both re-encode and re-set `ImageData`. `RichTextBoxHelper` is
**not modified** by this work. The key manual check is the save→reopen round-trip.

### 9. Deleted (the overlay apparatus)

From `InstructionsEditorWindow.xaml`: the entire `<Canvas x:Name="OverlayCanvas">` block (masks,
`CropRect`, `CropBody`, all `Crop*`/`Resize*` `Thumb`s, `OverlayToolbar`) and the `HandleThumb`
`Style`.

From `InstructionsEditorWindow.xaml.cs`: `SingleChildAdorner`; `_overlayAdorner`/`_adornerLayer`/
`_overlayShown`/`_selectedImage`/`_selectedBorder`/`_dispW`/`_dispH`/`_aspect`/`_cropMode`/crop-rect
fields; `EnsureOverlayShown`, `HideOverlay`, `LayoutHandles`, `LayoutToolbar`, `LayoutCrop`,
`SetResizeHandlesVisible`, `SetCropVisible`, `ExitToNormalOverlay`, `SelectImage`, `DeselectImage`,
`Editor_PreviewMouseLeftButtonDown`, `Image_PreviewMouseLeftButtonDown`, `AttachImageHandler`, all
`Resize*_DragDelta` / `Crop*_DragDelta` handlers, `Crop_Click`/`ApplyCrop_Click`/`CancelCrop_Click`
(logic moves into `ImageCropWindow`). Left-click reverts to plain caret placement.

### 10. Kept / reused

`InsertImageIntoEditor`, `NewSelectionBorder` (now for hover), `WireExistingImages` (re-wires the
hover style + ensures the wrapper `Border`, drops the dead click handler), `MarkdownConverter`,
`Editor_PreviewKeyDown` (Ctrl+V), `ApplyPreset`, `BitmapSourceToPng`/`DecodePng`/`ConvertToPng`,
the crop math (moved), and all of `RichTextBoxHelper`.

---

## Error handling

- Bad/undecodable image bytes (drop, paste, replace): swallow per-image (as today's
  `try/catch` in `InsertImageIntoEditor`), never fail the whole document.
- Crop dialog with a zero/degenerate rect: `ClampCrop` enforces a 20px minimum; `Int32Rect` is
  clamped to source bounds before `CroppedBitmap`.
- Custom width: non-numeric or out-of-range input is rejected/clamped in the prompt; no-op on
  Cancel.

## Testing / verification

RVTuk has no automated WPF UI harness (Core's xunit suite can't drive `RichTextBox`), so
verification is **manual in Revit**, following the repo's existing practice (close Revit 2024 →
elevated `.\Deploy.ps1 2024` → restart → Browse Library → Edit Info on a family). Checklist:

1. **Insert** via Add Image button, Ctrl+V paste, and **drag-drop a file onto the editor body**
   (the bug) — all land an image, drag-drop lands it at the drop point.
2. **Hover** an image → accent outline + tooltip appear.
3. **Right-click** → menu opens over the image.
4. **Size ▸** each preset resizes; **Custom width…** applies an exact width.
5. **Crop…** → modal opens, drag handles/body, Apply crops; Cancel leaves the image untouched.
6. **Replace…** swaps the image; **Remove** deletes it.
7. **Round-trip:** Save → reopen the editor → every image, at its last size/crop, is still there
   (the historically fragile path).
8. Regression: left-click still places a caret; Raw/Preview toggle still round-trips images.

Pre-code gate: `dotnet build RVTuk.sln -c Release2024` and `-c Release2025` clean; Core tests
unaffected (no Core changes).

## Files touched

| File | Change |
|---|---|
| `Views/InstructionsEditorWindow.xaml` | remove `OverlayCanvas` + `HandleThumb`; add `ImageMenu` `ContextMenu`, hover `Style`, and a Custom-width `Popup` (mirrors `TablePopup`) |
| `Views/InstructionsEditorWindow.xaml.cs` | delete overlay apparatus; add right-click menu + Size/Replace/Crop/Remove handlers; drag-drop fix; hover wiring |
| `Views/ImageCropWindow.xaml` / `.xaml.cs` | **new** (only new file) — modal freeform crop dialog (reuses crop chrome + math) |
| `Controls/RichTextBoxHelper.cs` | **unchanged** (persistence invariant) |

## Out of scope / future

- Aspect-ratio crop presets (1:1, 16:9), rotate/flip, alt-text/captions.
- Any change to how images persist (`ImageData`/XAML) — deliberately untouched.
- Image editing anywhere other than the Instructions Editor.
