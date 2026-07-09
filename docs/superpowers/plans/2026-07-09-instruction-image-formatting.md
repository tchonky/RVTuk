# Instructions Editor Image Formatting — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fragile AdornerLayer image-selection overlay in the Instructions Editor with a right-click context menu (size / replace / crop / remove), a modal crop dialog, a hover affordance, and a working editor drag-drop.

**Architecture:** All work is in `RVTuk.UI` (WPF), except one pure geometry helper extracted into `RVTuk.Core` so the only arithmetic-heavy piece (crop rect → source-pixel rect) is unit-tested. The overlay/adorner/thumb apparatus is deleted; discrete commands move to a `ContextMenu` opened from a reliable Editor-level right-click handler; crop moves to a self-contained modal `Window` where the same handle logic works without a `RichTextBox` underneath it. Image persistence (`ImageData` base64 + XAML round-trip in `RichTextBoxHelper`) is deliberately untouched.

**Tech Stack:** C# (net48 / net8.0-windows multi-target), WPF, xunit (`RVTuk.Core.Tests`), `Microsoft.Data.Sqlite` (unrelated here). Design spec: `docs/superpowers/specs/2026-07-09-instruction-image-formatting-design.md`.

## Global Constraints

- **Branch:** work happens on `instruction-image-formatting` (already checked out, based on `master` @ `0ec5192`).
- **Build configs:** `Release2024` → `net48`; `Release2025` → `net8.0-windows`. There is no `Debug`/`Release`. Per-task UI gate builds `RVTuk.UI`; the final task builds the whole solution for **both** configs.
- **Layering:** `RVTuk.Core` must stay free of WPF and Revit types (no `System.Windows.*`). `RVTuk.UI` must stay free of Revit types. Never break this.
- **No automated UI harness:** the repo has no way to unit-test a `RichTextBox`/`Window`. UI tasks are gated on a **clean build**; behaviour is confirmed by the **manual Revit checklist** (Task 6), which the user runs on a Revit machine via `Deploy.ps1`. Only Task 1 is true TDD.
- **Persistence invariant:** do **not** modify `Controls/RichTextBoxHelper.cs`. Every image mutation must keep the `RichTextBoxHelper.ImageData` attached property in sync with `Image.Source` (resize doesn't change bytes; replace and crop re-encode to PNG and re-set `ImageData`).
- **Comment style:** minimal comments — a short "why" only where non-obvious; no narration of "what".
- **Commits:** conventional-commit subjects; end every commit message with:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `src/RVTuk.Core/Util/ImageCropGeometry.cs` | Pure crop-rect → source-pixel-rect math + `PixelRect` value | **Create** |
| `tests/RVTuk.Core.Tests/Util/ImageCropGeometryTests.cs` | Unit tests for the above | **Create** |
| `src/LibraryBrowser/RVTuk.UI/Views/ImageCropWindow.xaml` (+ `.xaml.cs`) | Modal freeform crop dialog (only new UI window) | **Create** |
| `src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml` | Remove overlay canvas + handle style; add image `ContextMenu`, hover `Style`, Custom-width `Popup` | **Modify** |
| `src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs` | Delete overlay code; add right-click menu handlers, hover wiring, drag-drop fix | **Modify** |
| `src/LibraryBrowser/RVTuk.UI/Controls/RichTextBoxHelper.cs` | (persistence) | **Untouched** |

Dependency order: **Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6.**

---

### Task 1: Pure crop geometry helper (Core, TDD)

**Files:**
- Create: `src/RVTuk.Core/Util/ImageCropGeometry.cs`
- Test: `tests/RVTuk.Core.Tests/Util/ImageCropGeometryTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct PixelRect(int X, int Y, int Width, int Height)` in `RVTuk.Core.Util`.
  - `static PixelRect ImageCropGeometry.ToPixelRect(int sourcePixelWidth, int sourcePixelHeight, double displayWidth, double displayHeight, double cropX, double cropY, double cropWidth, double cropHeight)` in `RVTuk.Core.Util`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/Util/ImageCropGeometryTests.cs`:

```csharp
using RVTuk.Core.Util;
using Xunit;

namespace RVTuk.Core.Tests.Util;

public class ImageCropGeometryTests
{
    [Fact]
    public void DisplayEqualsSource_FullCrop_ReturnsFullRect()
    {
        var r = ImageCropGeometry.ToPixelRect(800, 600, 800, 600, 0, 0, 800, 600);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.Equal(800, r.Width);
        Assert.Equal(600, r.Height);
    }

    [Fact]
    public void ScaledDownDisplay_MapsCropBackToSourcePixels()
    {
        // Source 800x600 shown at 400x300 (2x). Crop centre 200x150 at (100,75) display coords.
        var r = ImageCropGeometry.ToPixelRect(800, 600, 400, 300, 100, 75, 200, 150);
        Assert.Equal(200, r.X);
        Assert.Equal(150, r.Y);
        Assert.Equal(400, r.Width);
        Assert.Equal(300, r.Height);
    }

    [Fact]
    public void CropPastRightBottomEdge_ClampsWidthHeightToBounds()
    {
        var r = ImageCropGeometry.ToPixelRect(100, 100, 100, 100, 90, 90, 50, 50);
        Assert.Equal(90, r.X);
        Assert.Equal(90, r.Y);
        Assert.Equal(10, r.Width);
        Assert.Equal(10, r.Height);
    }

    [Fact]
    public void DegenerateCrop_ClampsToMinimumOnePixel()
    {
        var r = ImageCropGeometry.ToPixelRect(100, 100, 100, 100, 0, 0, 0, 0);
        Assert.Equal(1, r.Width);
        Assert.Equal(1, r.Height);
    }

    [Fact]
    public void NonPositiveDisplay_ReturnsFullSourceRect()
    {
        var r = ImageCropGeometry.ToPixelRect(100, 80, 0, 0, 10, 10, 20, 20);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.Equal(100, r.Width);
        Assert.Equal(80, r.Height);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --nologo`
Expected: FAIL to compile — `ImageCropGeometry` / `PixelRect` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/Util/ImageCropGeometry.cs`:

```csharp
using System;

namespace RVTuk.Core.Util
{
    public readonly record struct PixelRect(int X, int Y, int Width, int Height);

    // Maps a crop rectangle expressed in on-screen display coordinates back to integer
    // source-image pixel coordinates, clamped so the result always stays inside the image.
    public static class ImageCropGeometry
    {
        public static PixelRect ToPixelRect(
            int sourcePixelWidth, int sourcePixelHeight,
            double displayWidth, double displayHeight,
            double cropX, double cropY, double cropWidth, double cropHeight)
        {
            if (sourcePixelWidth <= 0 || sourcePixelHeight <= 0)
                return new PixelRect(0, 0, Math.Max(0, sourcePixelWidth), Math.Max(0, sourcePixelHeight));
            if (displayWidth <= 0 || displayHeight <= 0)
                return new PixelRect(0, 0, sourcePixelWidth, sourcePixelHeight);

            double sx = sourcePixelWidth / displayWidth;
            double sy = sourcePixelHeight / displayHeight;

            int x = (int)Math.Round(cropX * sx);
            int y = (int)Math.Round(cropY * sy);
            int w = (int)Math.Round(cropWidth * sx);
            int h = (int)Math.Round(cropHeight * sy);

            x = Clamp(x, 0, sourcePixelWidth - 1);
            y = Clamp(y, 0, sourcePixelHeight - 1);
            w = Clamp(w, 1, sourcePixelWidth - x);
            h = Clamp(h, 1, sourcePixelHeight - y);
            return new PixelRect(x, y, w, h);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (hi < lo) hi = lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --nologo`
Expected: PASS — all 5 new tests green, prior 154 still green (159 total).

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/Util/ImageCropGeometry.cs tests/RVTuk.Core.Tests/Util/ImageCropGeometryTests.cs
git commit -F - <<'EOF'
feat(core): add ImageCropGeometry for crop-rect -> pixel-rect mapping

Pure, unit-tested helper (display coords -> clamped source-pixel Int32 rect) so the
crop dialog's arithmetic is verified outside WPF.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 2: Modal crop dialog (`ImageCropWindow`)

**Files:**
- Create: `src/LibraryBrowser/RVTuk.UI/Views/ImageCropWindow.xaml`
- Create: `src/LibraryBrowser/RVTuk.UI/Views/ImageCropWindow.xaml.cs`

**Interfaces:**
- Consumes: `RVTuk.Core.Util.ImageCropGeometry.ToPixelRect(...)`, `PixelRect` (Task 1).
- Produces: `ImageCropWindow(BitmapSource source)` ctor; `BitmapSource? Result { get; }`; `ShowDialog()` returns `true` when the user applied a crop. (Consumed by Task 3.)

- [ ] **Step 1: Create the window XAML**

Create `src/LibraryBrowser/RVTuk.UI/Views/ImageCropWindow.xaml`:

```xml
<Window x:Class="RVTuk.UI.Views.ImageCropWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Crop image" SizeToContent="WidthAndHeight"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <Style x:Key="CropHandle" TargetType="{x:Type Thumb}">
                <Setter Property="Width"  Value="12"/>
                <Setter Property="Height" Value="12"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="{x:Type Thumb}">
                            <Rectangle Fill="{StaticResource Brush.Accent}" Stroke="White" StrokeThickness="1"/>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ResourceDictionary>
    </Window.Resources>

    <Window.Background><StaticResource ResourceKey="Brush.Bg"/></Window.Background>
    <Window.Foreground><StaticResource ResourceKey="Brush.Text"/></Window.Foreground>

    <DockPanel Margin="10">
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal"
                    HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button Content="Cancel" Width="70" Margin="0,0,8,0" IsCancel="True" Click="Cancel_Click"/>
            <Button Content="Apply"  Width="70" IsDefault="True"  Click="Apply_Click"/>
        </StackPanel>

        <Canvas x:Name="CropCanvas">
            <Image x:Name="Preview" Stretch="Fill"/>
            <Rectangle x:Name="MaskTop"    Fill="#99000000"/>
            <Rectangle x:Name="MaskBottom" Fill="#99000000"/>
            <Rectangle x:Name="MaskLeft"   Fill="#99000000"/>
            <Rectangle x:Name="MaskRight"  Fill="#99000000"/>
            <Rectangle x:Name="CropRect" Stroke="White" StrokeThickness="1"
                       StrokeDashArray="4 2" Fill="{x:Null}"/>
            <Thumb x:Name="CropBody" Cursor="SizeAll" DragDelta="CropBody_DragDelta">
                <Thumb.Template>
                    <ControlTemplate TargetType="{x:Type Thumb}">
                        <Rectangle Fill="Transparent"/>
                    </ControlTemplate>
                </Thumb.Template>
            </Thumb>
            <Thumb x:Name="CropNW" Style="{StaticResource CropHandle}" Cursor="SizeNWSE" DragDelta="CropNW_DragDelta"/>
            <Thumb x:Name="CropNE" Style="{StaticResource CropHandle}" Cursor="SizeNESW" DragDelta="CropNE_DragDelta"/>
            <Thumb x:Name="CropSW" Style="{StaticResource CropHandle}" Cursor="SizeNESW" DragDelta="CropSW_DragDelta"/>
            <Thumb x:Name="CropSE" Style="{StaticResource CropHandle}" Cursor="SizeNWSE" DragDelta="CropSE_DragDelta"/>
        </Canvas>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Create the window code-behind**

Create `src/LibraryBrowser/RVTuk.UI/Views/ImageCropWindow.xaml.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using RVTuk.Core.Util;

namespace RVTuk.UI.Views
{
    public partial class ImageCropWindow : Window
    {
        private readonly BitmapSource _source;
        private readonly double _dispW;
        private readonly double _dispH;
        private double _cropX, _cropY, _cropW, _cropH;

        public BitmapSource? Result { get; private set; }

        public ImageCropWindow(BitmapSource source)
        {
            InitializeComponent();
            _source = source;

            // Fit the preview into a working box, preserving aspect ratio.
            const double max = 520;
            double aspect = (double)source.PixelHeight / source.PixelWidth;
            _dispW = Math.Min(max, source.PixelWidth);
            _dispH = _dispW * aspect;
            if (_dispH > max) { _dispH = max; _dispW = _dispH / aspect; }

            Preview.Source = source;
            Preview.Width  = _dispW;
            Preview.Height = _dispH;
            CropCanvas.Width  = _dispW;
            CropCanvas.Height = _dispH;

            _cropX = _dispW * 0.1; _cropY = _dispH * 0.1;
            _cropW = _dispW * 0.8; _cropH = _dispH * 0.8;
            Loaded += (s, e) => LayoutCrop();
        }

        private void CropNW_DragDelta(object s, DragDeltaEventArgs e)
        { _cropX += e.HorizontalChange; _cropY += e.VerticalChange; _cropW -= e.HorizontalChange; _cropH -= e.VerticalChange; Clamp(); LayoutCrop(); }
        private void CropNE_DragDelta(object s, DragDeltaEventArgs e)
        { _cropY += e.VerticalChange; _cropW += e.HorizontalChange; _cropH -= e.VerticalChange; Clamp(); LayoutCrop(); }
        private void CropSW_DragDelta(object s, DragDeltaEventArgs e)
        { _cropX += e.HorizontalChange; _cropW -= e.HorizontalChange; _cropH += e.VerticalChange; Clamp(); LayoutCrop(); }
        private void CropSE_DragDelta(object s, DragDeltaEventArgs e)
        { _cropW += e.HorizontalChange; _cropH += e.VerticalChange; Clamp(); LayoutCrop(); }
        private void CropBody_DragDelta(object s, DragDeltaEventArgs e)
        { _cropX += e.HorizontalChange; _cropY += e.VerticalChange; Clamp(); LayoutCrop(); }

        private void Clamp()
        {
            _cropW = Math.Max(20, Math.Min(_cropW, _dispW));
            _cropH = Math.Max(20, Math.Min(_cropH, _dispH));
            _cropX = Math.Max(0, Math.Min(_cropX, _dispW - _cropW));
            _cropY = Math.Max(0, Math.Min(_cropY, _dispH - _cropH));
        }

        private void LayoutCrop()
        {
            Place(CropRect, _cropX, _cropY, _cropW, _cropH);
            Place(CropBody, _cropX, _cropY, _cropW, _cropH);
            Place(MaskTop,    0,               0,               _dispW,                     _cropY);
            Place(MaskBottom, 0,               _cropY + _cropH, _dispW,                     _dispH - (_cropY + _cropH));
            Place(MaskLeft,   0,               _cropY,          _cropX,                     _cropH);
            Place(MaskRight,  _cropX + _cropW, _cropY,          _dispW - (_cropX + _cropW), _cropH);
            Place(CropNW, _cropX - 6,          _cropY - 6);
            Place(CropNE, _cropX + _cropW - 6, _cropY - 6);
            Place(CropSW, _cropX - 6,          _cropY + _cropH - 6);
            Place(CropSE, _cropX + _cropW - 6, _cropY + _cropH - 6);
        }

        private static void Place(FrameworkElement el, double left, double top,
                                  double w = double.NaN, double h = double.NaN)
        {
            Canvas.SetLeft(el, left);
            Canvas.SetTop(el, top);
            if (!double.IsNaN(w)) el.Width  = Math.Max(0, w);
            if (!double.IsNaN(h)) el.Height = Math.Max(0, h);
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var r = ImageCropGeometry.ToPixelRect(
                _source.PixelWidth, _source.PixelHeight, _dispW, _dispH,
                _cropX, _cropY, _cropW, _cropH);
            try
            {
                Result = new CroppedBitmap(_source, new Int32Rect(r.X, r.Y, r.Width, r.Height));
                DialogResult = true;
            }
            catch { DialogResult = false; }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/LibraryBrowser/RVTuk.UI/RVTuk.UI.csproj -c Release2024 --nologo`
Expected: `Build succeeded.` `0 Error(s)`.

*(No unit test — this is a WPF window. Its arithmetic is covered by Task 1; interaction is verified manually in Task 6.)*

- [ ] **Step 4: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/Views/ImageCropWindow.xaml src/LibraryBrowser/RVTuk.UI/Views/ImageCropWindow.xaml.cs
git commit -F - <<'EOF'
feat(ui): add modal ImageCropWindow crop dialog

Freeform crop over a plain Canvas/Window (no RichTextBox AdornerLayer), using the
tested ImageCropGeometry for the final pixel rect. Returns the cropped BitmapSource.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 3: Swap the overlay for a right-click context menu

Replaces the whole selection/overlay apparatus in one reviewer gate. After this task, right-clicking an image gives Size / Replace / Crop / Remove; left-click is plain caret placement again.

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml`
- Modify: `src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs`

**Interfaces:**
- Consumes: `ImageCropWindow` (Task 2); existing `FindImageFrom`, `GetInlineUIContainers`, `InsertImageIntoEditor`, `NewSelectionBorder`, `ConvertToPng`, `DecodePng`, `BitmapSourceToPng`, `RichTextBoxHelper.SetImageData`.
- Produces: field `Image? _menuTargetImage`; handler `Editor_PreviewMouseRightButtonUp`; a rewritten `ApplyPreset(double)` that sets `_menuTargetImage.Width`.

- [ ] **Step 1: Edit the XAML — remove overlay, add menu + custom-width popup**

In `InstructionsEditorWindow.xaml`:

1. **Delete** the `HandleThumb` `Style` (the `<Style x:Key="HandleThumb" …>` block in `Window.Resources`).
2. **Delete** the entire selected-image overlay block: the comment banner plus `<Canvas x:Name="OverlayCanvas"> … </Canvas>` (masks, `CropRect`, `CropBody`, all `Crop*`/`Resize*` thumbs, and `OverlayToolbar`).
3. In `Window.Resources` (after `<BooleanToVisibilityConverter x:Key="BoolVis"/>`), **add** the image context menu:

```xml
<ContextMenu x:Key="ImageMenu">
    <MenuItem Header="Size">
        <MenuItem Header="Small"         Click="SizeSmall_Click"/>
        <MenuItem Header="Medium"        Click="SizeMedium_Click"/>
        <MenuItem Header="Large"         Click="SizeLarge_Click"/>
        <MenuItem Header="Full width"    Click="SizeFull_Click"/>
        <Separator/>
        <MenuItem Header="Custom width&#x2026;" Click="SizeCustom_Click"/>
    </MenuItem>
    <MenuItem Header="Replace image&#x2026;" Click="ReplaceImage_Click"/>
    <MenuItem Header="Crop&#x2026;"          Click="CropImage_Click"/>
    <Separator/>
    <MenuItem Header="Remove image"          Click="RemoveImage_Click"/>
</ContextMenu>
```

4. Next to the existing `TablePopup` (just before `</Grid>` at the end), **add** the custom-width popup:

```xml
<Popup x:Name="CustomWidthPopup" StaysOpen="False" AllowsTransparency="True" Placement="Mouse">
    <Border Background="{StaticResource Brush.Panel}" BorderBrush="{StaticResource Brush.Border}"
            BorderThickness="1" CornerRadius="2" Padding="10">
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="Width" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBox x:Name="CustomWidthBox" Width="60" TextAlignment="Center"/>
            <Button Content="Apply" Width="56" Margin="6,0,0,0" Click="ApplyCustomWidth_Click"/>
        </StackPanel>
    </Border>
</Popup>
```

- [ ] **Step 2: Edit the code-behind — delete overlay members**

In `InstructionsEditorWindow.xaml.cs`, **delete** all of the following (grouped):

- **Fields:** `_overlayAdorner`, `_adornerLayer`, `_overlayShown`, `_selectedImage`, `_selectedBorder`, `_dispW`, `_dispH`, `_aspect`, `_cropMode`, `_cropX`, `_cropY`, `_cropW`, `_cropH`.
- **Ctor lines:**
  - `Editor.PreviewMouseLeftButtonDown += Editor_PreviewMouseLeftButtonDown;`
  - `(OverlayCanvas.Parent as Panel)?.Children.Remove(OverlayCanvas);`
  - the `Editor.AddHandler(ScrollViewer.ScrollChangedEvent, …)` statement
  - `Editor.SizeChanged += (s, e) => { if (_selectedImage != null) LayoutHandles(); };`
- **Methods:** `Image_PreviewMouseLeftButtonDown`, `Editor_PreviewMouseLeftButtonDown`, `AttachImageHandler`, `SelectImage`, `DeselectImage`, `EnsureOverlayShown`, `HideOverlay`, `ExitToNormalOverlay`, `SetResizeHandlesVisible`, `SetCropVisible`, `LayoutHandles`, `LayoutToolbar`, `LayoutCrop`, `ResizeSE_DragDelta`, `ResizeNE_DragDelta`, `ResizeSW_DragDelta`, `ResizeNW_DragDelta`, `ResizeBy`, `PresetS_Click`, `PresetM_Click`, `PresetL_Click`, `PresetFull_Click`, `Crop_Click`, `CancelCrop_Click`, `ApplyCrop_Click`, `CropNW_DragDelta`, `CropNE_DragDelta`, `CropSW_DragDelta`, `CropSE_DragDelta`, `CropBody_DragDelta`, `ClampCrop`, the old `RemoveImage_Click`, the `Place` helper, and the `Clamp(double,double,double)` helper.
- **Class:** the entire `internal sealed class SingleChildAdorner : Adorner { … }` at the bottom of the file.

**Keep** (do not delete): `FindImageFrom`, `NewSelectionBorder`, `GetInlineUIContainers`, `InsertImageIntoEditor`, `WireExistingImages`, `Frozen`, `DecodePng`, `ConvertToPng`, `BitmapSourceToPng`, and everything about Preview/Raw, Tables, thumbnail, and Save.

- [ ] **Step 3: Edit the code-behind — fix three now-broken references**

1. In `SetRawMode(bool raw)`, inside the `if (raw)` branch, **delete** the line `DeselectImage();` (it was the first statement there).
2. In `InsertImageIntoEditor`, **delete** the line `AttachImageHandler(image);`.
3. In `WireExistingImages`, **delete** both `AttachImageHandler(bare);` and `AttachImageHandler(wrapped);` lines (leave the surrounding border-wrapping logic intact).

- [ ] **Step 4: Edit the code-behind — add the menu field, handlers, and rewritten `ApplyPreset`**

Add the field near the top of the class (with the other private fields):

```csharp
private Image? _menuTargetImage;
```

Wire the right-click handler in the constructor (next to where the deleted left-button wiring was, after `Editor.PreviewKeyDown += Editor_PreviewKeyDown;`):

```csharp
Editor.PreviewMouseRightButtonUp += Editor_PreviewMouseRightButtonUp;
```

Add these members (place them in the image region of the file):

```csharp
private void Editor_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
{
    if (_isRawMode) return;
    var img = FindImageFrom(e.OriginalSource as DependencyObject);
    if (img == null) return;

    _menuTargetImage = img;
    var menu = (ContextMenu)Resources["ImageMenu"];
    menu.PlacementTarget = Editor;
    menu.Placement = PlacementMode.MousePoint;
    menu.IsOpen = true;
    e.Handled = true; // suppress the RichTextBox's own cut/copy/paste menu
}

// Sets the on-screen width; aspect ratio holds via the image's Stretch=Uniform (no Height set).
private void ApplyPreset(double width)
{
    if (_menuTargetImage != null) _menuTargetImage.Width = width;
}

private void SizeSmall_Click(object s, RoutedEventArgs e)  => ApplyPreset(160);
private void SizeMedium_Click(object s, RoutedEventArgs e) => ApplyPreset(320);
private void SizeLarge_Click(object s, RoutedEventArgs e)  => ApplyPreset(480);
private void SizeFull_Click(object s, RoutedEventArgs e)   => ApplyPreset(Math.Max(80, Editor.ActualWidth - 40));

private void SizeCustom_Click(object s, RoutedEventArgs e)
{
    double cur = _menuTargetImage?.Width ?? double.NaN;
    CustomWidthBox.Text = double.IsNaN(cur) ? "320" : ((int)Math.Round(cur)).ToString();
    CustomWidthPopup.IsOpen = true;
    CustomWidthBox.Focus();
    CustomWidthBox.SelectAll();
}

private void ApplyCustomWidth_Click(object s, RoutedEventArgs e)
{
    CustomWidthPopup.IsOpen = false;
    if (int.TryParse(CustomWidthBox.Text?.Trim(), out int w))
        ApplyPreset(Math.Max(40, Math.Min(2000, w)));
}

private void ReplaceImage_Click(object s, RoutedEventArgs e)
{
    if (_menuTargetImage == null) return;
    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
    if (dlg.ShowDialog() != true) return;
    try
    {
        var png = ConvertToPng(File.ReadAllBytes(dlg.FileName));
        _menuTargetImage.Source = DecodePng(png);
        RichTextBoxHelper.SetImageData(_menuTargetImage, Convert.ToBase64String(png));
    }
    catch { }
}

private void CropImage_Click(object s, RoutedEventArgs e)
{
    if (_menuTargetImage?.Source is not BitmapSource src) return;
    var dlg = new ImageCropWindow(src) { Owner = this };
    if (dlg.ShowDialog() == true && dlg.Result != null)
    {
        var png = BitmapSourceToPng(dlg.Result);
        var bmp = DecodePng(png);
        _menuTargetImage.Source = bmp;
        RichTextBoxHelper.SetImageData(_menuTargetImage, Convert.ToBase64String(png));
        double curW = _menuTargetImage.Width;
        if (double.IsNaN(curW) || curW <= 0) curW = bmp.PixelWidth;
        _menuTargetImage.Width = Math.Min(curW, bmp.PixelWidth);
    }
}

private void RemoveImage_Click(object s, RoutedEventArgs e)
{
    if (_menuTargetImage == null) return;
    var container = GetInlineUIContainers(Editor.Document)
        .FirstOrDefault(c => ReferenceEquals((c.Child as Border)?.Child, _menuTargetImage)
                          || ReferenceEquals(c.Child, _menuTargetImage));
    if (container?.Parent is Paragraph p) p.Inlines.Remove(container);
    _menuTargetImage = null;
}
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build src/LibraryBrowser/RVTuk.UI/RVTuk.UI.csproj -c Release2024 --nologo`
Expected: `Build succeeded.` `0 Error(s)`. (If you see "does not contain a definition for `OverlayCanvas`/`NormalTools`/…", a code reference to a deleted x:Named element remains — remove it.)

- [ ] **Step 6: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs
git commit -F - <<'EOF'
feat(ui): replace image overlay with a right-click context menu

Delete the AdornerLayer selection overlay (thumbs, masks, floating toolbar, adorner)
and drive image commands from an Editor-level right-click ContextMenu: Size
(S/M/L/Full/Custom width), Replace, Crop (modal ImageCropWindow), Remove. Left-click
is plain caret placement again.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 4: Hover affordance

Makes an inline image visibly interactive (the reported "nothing shows on hover") without a click.

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml`
- Modify: `src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs`

**Interfaces:**
- Consumes: `NewSelectionBorder`, `InsertImageIntoEditor`, `WireExistingImages` (Task 3 state).
- Produces: keyed `Style` `ImageHoverBorder`; `NewSelectionBorder` becomes an instance method applying that style; images get a right-click tooltip.

- [ ] **Step 1: Add the hover style to XAML**

In `InstructionsEditorWindow.xaml` `Window.Resources` (after the `ImageMenu` from Task 3), add:

```xml
<Style x:Key="ImageHoverBorder" TargetType="Border">
    <Setter Property="BorderThickness" Value="2"/>
    <Setter Property="BorderBrush" Value="Transparent"/>
    <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
            <Setter Property="BorderBrush" Value="{StaticResource Brush.Accent}"/>
        </Trigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 2: Apply the style + tooltip in code-behind**

1. Change `NewSelectionBorder` from `static` to an instance method that applies the style:

```csharp
private Border NewSelectionBorder() =>
    new Border { Style = (Style)Resources["ImageHoverBorder"] };
```

2. In `InsertImageIntoEditor`, after `var image = new Image { … };`, add the tooltip:

```csharp
image.ToolTip = "Right-click to resize, crop, or replace.";
```

3. In `WireExistingImages`, set the tooltip on each wired image. In the `if (container.Child is Image bare)` branch add `bare.ToolTip = "Right-click to resize, crop, or replace.";` after re-wrapping, and in the `else if (… is Border b && b.Child is Image wrapped)` branch add `b.Style = (Style)Resources["ImageHoverBorder"]; wrapped.ToolTip = "Right-click to resize, crop, or replace.";` (replace the branch's manual `BorderThickness`/`BorderBrush` assignments with the style).

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/LibraryBrowser/RVTuk.UI/RVTuk.UI.csproj -c Release2024 --nologo`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs
git commit -F - <<'EOF'
feat(ui): hover affordance for inline instruction images

The wrapper border shows an accent outline on hover and each image gets a
"Right-click to resize, crop, or replace" tooltip, so the image reads as
interactive without a click.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 5: Fix drag-drop into the editor body

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs`

**Interfaces:**
- Consumes: `InsertImageIntoEditor`, `BitmapSourceToPng`.
- Produces: `Editor_PreviewDragOver`, `Editor_PreviewDrop`, and static helpers `HasImagePayload`, `IsImageFile`.

- [ ] **Step 1: Rewire the constructor**

In the ctor, **replace** the line `Editor.Drop += Editor_Drop;` with:

```csharp
Editor.PreviewDragOver += Editor_PreviewDragOver;
Editor.PreviewDrop     += Editor_PreviewDrop;
```

- [ ] **Step 2: Replace the drop handler**

**Delete** the existing `Editor_Drop` method and add:

```csharp
private static bool IsImageFile(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
}

private static bool HasImagePayload(IDataObject data)
{
    if (data.GetDataPresent(DataFormats.Bitmap)) return true;
    if (data.GetDataPresent(DataFormats.FileDrop))
        return ((string[])data.GetData(DataFormats.FileDrop)).Any(IsImageFile);
    return false;
}

// The RichTextBox's internal editor vetoes drops in its own DragOver; handling the
// tunnelling Preview events (and marking them handled) is what makes file drops work.
private void Editor_PreviewDragOver(object sender, DragEventArgs e)
{
    e.Effects = HasImagePayload(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
    e.Handled = true;
}

private void Editor_PreviewDrop(object sender, DragEventArgs e)
{
    if (!HasImagePayload(e.Data)) return;

    // Drop where the pointer is, not at the previous caret.
    var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor), true);
    if (pos != null) Editor.CaretPosition = pos;

    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        foreach (var file in (string[])e.Data.GetData(DataFormats.FileDrop))
            if (IsImageFile(file)) InsertImageIntoEditor(File.ReadAllBytes(file));
    }
    else
    {
        var raw = e.Data.GetData(DataFormats.Bitmap);
        if (raw is BitmapSource bs)
            InsertImageIntoEditor(BitmapSourceToPng(bs));
        else if (raw is System.Drawing.Bitmap gdi)
        {
            using var ms = new MemoryStream();
            gdi.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            InsertImageIntoEditor(ms.ToArray());
        }
    }
    e.Handled = true;
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/LibraryBrowser/RVTuk.UI/RVTuk.UI.csproj -c Release2024 --nologo`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs
git commit -F - <<'EOF'
fix(ui): make image drag-drop onto the instructions editor work

Handle the tunnelling PreviewDragOver/PreviewDrop on the RichTextBox (the internal
text editor vetoes drops in its own DragOver), accept image files and dragged
bitmaps, and insert at the drop point.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 6: Full-solution verification + manual Revit checklist

**Files:** none (verification only; commit only if a fix is needed).

- [ ] **Step 1: Build both configs clean**

Run: `dotnet build RVTuk.sln -c Release2024 --nologo`
Then: `dotnet build RVTuk.sln -c Release2025 --nologo`
Expected: both `Build succeeded.` `0 Error(s)` (pre-existing nullable/NU1902 warnings are fine).

- [ ] **Step 2: Run the Core test suite**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --nologo`
Expected: PASS — 159 tests (154 prior + 5 from Task 1).

- [ ] **Step 3: Manual verification in Revit (requires a Revit 2024 machine)**

This is the real behavioural gate; it cannot be automated. Close Revit 2024 → run elevated `.\Deploy.ps1 2024` → restart Revit → Browse Library → open **Edit Info** on a family, then confirm:

1. **Insert** an image three ways: the **Add Image** button, **Ctrl+V** paste, and **drag-drop an image file onto the editor body** (the bug) — all insert; the drag lands the image at the drop point.
2. **Hover** an image → accent outline + tooltip appear.
3. **Right-click** an image → menu opens over it; left-click still just places a caret.
4. **Size ▸** Small/Medium/Large/Full each resize; **Custom width…** popup applies an exact width.
5. **Crop…** → modal opens; drag corners/body; **Apply** crops, **Cancel** leaves the image unchanged.
6. **Replace image…** swaps the picture; **Remove image** deletes it.
7. **Round-trip (key test):** **Save** → reopen Edit Info → every image is present at its last size/crop. Toggle **Raw**/**Preview** → images still round-trip.

- [ ] **Step 4 (only if Step 3 finds a defect): fix, rebuild, recommit**

Fix the specific file, rerun Step 1–2, and commit with a `fix(ui): …` message ending in the `Co-Authored-By` trailer. Otherwise this task has no commit.

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- §1 right-click menu → Task 3 (`Editor_PreviewMouseRightButtonUp` + `ImageMenu`).
- §2 size submenu incl. Custom width → Task 3 (`SizeSmall/Medium/Large/Full_Click`, `SizeCustom`/`ApplyCustomWidth` + `CustomWidthPopup`).
- §3 replace → Task 3 (`ReplaceImage_Click`).
- §4 crop modal → Task 2 (`ImageCropWindow`) + Task 1 (`ImageCropGeometry`) + Task 3 (`CropImage_Click`).
- §5 remove → Task 3 (`RemoveImage_Click`).
- §6 hover affordance → Task 4.
- §7 drag-drop fix → Task 5.
- §8 persistence invariant → honoured everywhere (`SetImageData` after replace/crop; `RichTextBoxHelper` untouched); verified by Task 6 Step 3.7.
- §9 deletions → Task 3 Steps 1–2.
- §10 kept/reused → Task 3 "Keep" list.

**2. Placeholder scan** — no TBD/TODO/"handle edge cases"/"similar to"; every code step shows complete code; deletion steps name exact members.

**3. Type consistency** — `ImageCropGeometry.ToPixelRect` signature and `PixelRect` field names (`X/Y/Width/Height`) are identical in Task 1 (definition), Task 1 tests, and Task 2 (`Apply_Click`). `ImageCropWindow(BitmapSource)` ctor + `Result`/`ShowDialog()` in Task 2 match their use in Task 3 `CropImage_Click`. `_menuTargetImage`, `ApplyPreset(double)`, `FindImageFrom`, `GetInlineUIContainers`, `BitmapSourceToPng`, `DecodePng`, `ConvertToPng`, `RichTextBoxHelper.SetImageData` are used consistently with their existing/defined signatures.

---

## Execution Handoff

Two execution options:

1. **Subagent-Driven (recommended)** — a fresh subagent per task, with review between tasks.
2. **Inline Execution** — execute the tasks in this session with checkpoints for review.
