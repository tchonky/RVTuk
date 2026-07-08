// RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RVTuk.Core.Database;
using RVTuk.UI.Controls;
using RVTuk.UI.Helpers;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
{
    public partial class InstructionsEditorWindow : Window
    {
        public InstructionsEditorViewModel ViewModel { get; }

        // ── Dark-theme brushes reused from code (Themes/DarkTheme.xaml) ──────────
        private static readonly SolidColorBrush AccentBrush     = Frozen("#4A90D9");
        private static readonly SolidColorBrush ControlBrush     = Frozen("#3C3C3C");
        private static readonly SolidColorBrush TableTextBrush    = Frozen("#E3E3E3");
        private static readonly SolidColorBrush TableBorderBrush  = Frozen("#4A4A4A");
        private static readonly SolidColorBrush TableAltRowBrush  = Frozen("#1F1F20");

        // ── Preview / Raw state ──────────────────────────────────────────────────
        private bool _isRawMode;

        // ── Selected-image / overlay state ──────────────────────────────────────
        private SingleChildAdorner? _overlayAdorner; // hosts OverlayCanvas in the Editor's AdornerLayer
        private AdornerLayer? _adornerLayer;
        private bool _overlayShown;
        private Image? _selectedImage;
        private Border? _selectedBorder;
        private double _dispW;   // current on-screen display width of the selected image
        private double _dispH;   // current on-screen display height (derived from aspect)
        private double _aspect;  // source PixelHeight / PixelWidth
        private bool _cropMode;
        private double _cropX, _cropY, _cropW, _cropH; // crop rect, in image display coords

        public InstructionsEditorWindow(
            FamilyBrowserItemViewModel item,
            string? currentXaml,
            string rfaFullPath,
            BrowserRepository repo)
        {
            InitializeComponent();

            ViewModel = new InstructionsEditorViewModel(
                item.Id, item.FileName, rfaFullPath, currentXaml, repo);

            ViewModel.CloseRequested += () => Dispatcher.Invoke(Close);

            DataContext = ViewModel;

            // Fix 3: handle Ctrl+V paste into the editor body
            Editor.PreviewKeyDown += Editor_PreviewKeyDown;

            // Drag-drop onto thumbnail
            ThumbnailImage.Drop     += ThumbnailImage_Drop;
            ThumbnailImage.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            // Drag-drop onto editor body
            Editor.Drop += Editor_Drop;

            // Select an image on click / deselect when clicking elsewhere. This is done at the
            // Editor level (a reliable tunnelling entry point) rather than via a handler on the
            // Image itself — see Image_PreviewMouseLeftButtonDown for why the per-image handler
            // never fires for an image embedded in an editable RichTextBox.
            Editor.PreviewMouseLeftButtonDown += Editor_PreviewMouseLeftButtonDown;

            // The overlay canvas lives in the Editor's AdornerLayer while an image is selected,
            // so detach it from the Grid now (it is re-hosted on demand in EnsureOverlayShown).
            (OverlayCanvas.Parent as Panel)?.Children.Remove(OverlayCanvas);

            // Keep the overlay glued to the image as the editor scrolls or resizes/reflows.
            Editor.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((s, e) => { if (_selectedImage != null) LayoutHandles(); }));
            Editor.SizeChanged += (s, e) => { if (_selectedImage != null) LayoutHandles(); };

            // Wire click-to-select on any images that arrived from stored XAML
            Loaded += (s, e) => { WireExistingImages(); UpdateToggleButtons(); };
        }

        private void ThumbMenuButton_Click(object sender, RoutedEventArgs e)
        {
            ThumbContextMenu.PlacementTarget = (UIElement)sender;
            ThumbContextMenu.DataContext     = ViewModel;
            ThumbContextMenu.IsOpen          = true;
        }

        private void ThumbnailImage_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0) LoadThumbnailFromFile(files[0]);
            }
            else if (e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                var bmp = (System.Drawing.Bitmap)e.Data.GetData(DataFormats.Bitmap);
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ViewModel.SetThumbnailFromBytes(ms.ToArray());
            }
        }

        // Fix 3: paste image from clipboard into editor body
        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (Clipboard.ContainsImage())
                {
                    var bmpSrc = Clipboard.GetImage();
                    if (bmpSrc != null)
                    {
                        InsertImageIntoEditor(BitmapSourceToPng(bmpSrc));
                        e.Handled = true; // prevent default paste
                    }
                }
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            // Paste image from clipboard onto thumbnail when thumbnail has mouse focus
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (ThumbnailImage.IsMouseOver && Clipboard.ContainsImage())
                {
                    var bmpSrc = Clipboard.GetImage();
                    if (bmpSrc != null)
                    {
                        ViewModel.SetThumbnailFromBytes(BitmapSourceToPng(bmpSrc));
                        e.Handled = true;
                    }
                }
            }
        }

        private void Editor_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
                        InsertImageIntoEditor(File.ReadAllBytes(file));
                }
                e.Handled = true;
            }
        }

        private void LoadThumbnailFromFile(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                ViewModel.SetThumbnailFromBytes(ConvertToPng(bytes));
            }
            catch (Exception ex) { MessageBox.Show($"Could not load image: {ex.Message}"); }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Preview / Raw toggle
        // ─────────────────────────────────────────────────────────────────────────
        private void PreviewMode_Click(object sender, RoutedEventArgs e) => SetRawMode(false);
        private void RawMode_Click(object sender, RoutedEventArgs e)     => SetRawMode(true);

        private void SetRawMode(bool raw)
        {
            if (raw == _isRawMode) return;

            if (raw)
            {
                DeselectImage();
                // Raw mode shows/edits Markdown, not the serialized XAML — the document is still
                // SAVED as XAML (see Save_Click); this only changes the human-editable view.
                RawEditor.Text            = MarkdownConverter.ToMarkdown(
                                                Editor.Document, RichTextBoxHelper.GetImageData) ?? string.Empty;
                RawEditor.Visibility      = Visibility.Visible;
                Editor.Visibility         = Visibility.Collapsed;
                FormattingButtons.IsEnabled = false;
            }
            else
            {
                SyncRawToEditor();
                Editor.Visibility         = Visibility.Visible;
                RawEditor.Visibility      = Visibility.Collapsed;
                FormattingButtons.IsEnabled = true;
            }

            _isRawMode = raw;
            UpdateToggleButtons();
        }

        // Parse the raw Markdown text back into the live document and re-wire click-to-select on
        // the resulting images. MarkdownConverter.Build emits bare Images (decoupled from this
        // window's selection/tagging), stashing the source base64 on Image.Tag; we copy that into
        // RichTextBoxHelper.ImageData so the images persist on Save, then WireExistingImages wraps
        // them in the selection Border and attaches the handler — exactly like XAML-loaded images.
        private void SyncRawToEditor()
        {
            Editor.Document = MarkdownConverter.Build(RawEditor.Text);
            HydrateImageDataFromTags();
            WireExistingImages();
        }

        // Copy the base64 that MarkdownConverter.Build parked on Image.Tag into the ImageData
        // attached property (the authoritative persisted-bytes store), then clear the Tag.
        private void HydrateImageDataFromTags()
        {
            foreach (var iuc in GetInlineUIContainers(Editor.Document).ToList())
            {
                var img = iuc.Child as Image ?? (iuc.Child as Border)?.Child as Image;
                if (img?.Tag is string b64 && b64.Length > 0)
                {
                    if (string.IsNullOrEmpty(RichTextBoxHelper.GetImageData(img)))
                        RichTextBoxHelper.SetImageData(img, b64);
                    img.Tag = null;
                }
            }
        }

        private void UpdateToggleButtons()
        {
            PreviewBtn.Background = _isRawMode ? ControlBrush : AccentBrush;
            RawBtn.Background     = _isRawMode ? AccentBrush  : ControlBrush;
        }

        // Insert an inline image wrapped in a selection Border (transparent until selected) and
        // tagged with its PNG bytes (base64) so it survives the XAML save/load round-trip.
        private void InsertImageIntoEditor(byte[] pngData)
        {
            try
            {
                var bmp = DecodePng(pngData);

                var image = new Image
                {
                    Source  = bmp,
                    Stretch = Stretch.Uniform,
                    Width   = Math.Min(400, bmp.PixelWidth)
                };
                RichTextBoxHelper.SetImageData(image, Convert.ToBase64String(pngData));

                var border = NewSelectionBorder();
                border.Child = image;
                AttachImageHandler(image);

                new InlineUIContainer(border, Editor.CaretPosition);
            }
            catch { }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // If the user is looking at raw source, fold their edits back into the document first.
            if (_isRawMode) SyncRawToEditor();

            var xaml = RichTextBoxHelper.SerializeDocument(Editor);
            ViewModel.ExecuteSave(xaml);
        }

        // Formatting toolbar handlers
        private void Bold_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
        private void Italic_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
        private void Underline_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Underline);
        private void H1_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 22.0);
        private void H2_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 16.0);
        private void List_Click(object sender, RoutedEventArgs e)
        {
            var para = Editor.CaretPosition.Paragraph;
            if (para != null)
            {
                var list = new List(new ListItem(para));
                Editor.Document.Blocks.Add(list);
            }
        }
        private void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
                { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() == true)
                InsertImageIntoEditor(File.ReadAllBytes(dlg.FileName));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Insert Table (Feature 3)
        // ─────────────────────────────────────────────────────────────────────────
        private void TableButton_Click(object sender, RoutedEventArgs e) => TablePopup.IsOpen = true;

        private void InsertTable_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;

            int rows = ParseClamped(RowsBox.Text, 3, 1, 50);
            int cols = ParseClamped(ColsBox.Text, 3, 1, 20);
            bool header = TableHeaderCheck.IsChecked == true;

            var table = BuildTable(rows, cols, header);

            var caretPara = Editor.CaretPosition?.Paragraph;
            if (caretPara != null && Editor.Document.Blocks.Contains(caretPara))
                Editor.Document.Blocks.InsertAfter(caretPara, table);
            else
                Editor.Document.Blocks.Add(table);
        }

        // Builds a native FlowDocument Table styled to match MarkdownConverter.AddTable:
        // bold header row on the Control brush, alternating body-row banding, bordered cells.
        private static Table BuildTable(int rows, int cols, bool header)
        {
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 8) };
            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());

            TableCell MakeCell(bool isHeader, Brush? rowBg)
            {
                var p = new Paragraph
                {
                    FontSize   = 12,
                    Foreground = TableTextBrush,
                    FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                    Margin     = new Thickness(0)
                };
                return new TableCell(p)
                {
                    Background      = isHeader ? ControlBrush : rowBg,
                    BorderBrush     = TableBorderBrush,
                    BorderThickness = new Thickness(1),
                    Padding         = new Thickness(6, 3, 6, 3)
                };
            }

            if (header)
            {
                var headerGroup = new TableRowGroup();
                var headerRow   = new TableRow();
                for (int c = 0; c < cols; c++)
                    headerRow.Cells.Add(MakeCell(isHeader: true, rowBg: null));
                headerGroup.Rows.Add(headerRow);
                table.RowGroups.Add(headerGroup);
                rows = Math.Max(0, rows - 1); // header consumes one of the requested rows
            }

            var bodyGroup = new TableRowGroup();
            for (int r = 0; r < rows; r++)
            {
                var row   = new TableRow();
                var rowBg = (r % 2 == 1) ? TableAltRowBrush : null;
                for (int c = 0; c < cols; c++)
                    row.Cells.Add(MakeCell(isHeader: false, rowBg: rowBg));
                bodyGroup.Rows.Add(row);
            }
            table.RowGroups.Add(bodyGroup);
            return table;
        }

        private static int ParseClamped(string text, int fallback, int lo, int hi)
        {
            if (!int.TryParse(text?.Trim(), out int v)) v = fallback;
            return Math.Max(lo, Math.Min(hi, v));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Image selection + overlay (Feature 2)
        // ─────────────────────────────────────────────────────────────────────────

        private static Border NewSelectionBorder() =>
            new Border { BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };

        private void AttachImageHandler(Image img)
        {
            // Kept for completeness/idempotency, but note this per-image handler generally does
            // NOT fire (see Image_PreviewMouseLeftButtonDown). Selection is driven from the
            // Editor-level tunnelling handler instead.
            img.PreviewMouseLeftButtonDown -= Image_PreviewMouseLeftButtonDown;
            img.PreviewMouseLeftButtonDown += Image_PreviewMouseLeftButtonDown;
        }

        // ROOT CAUSE of the "overlay never appears" bug: this handler, attached to the Image
        // instance, does not run for an Image embedded via InlineUIContainer inside an *editable*
        // RichTextBox. The RichTextBox's TextEditor treats the InlineUIContainer as a single
        // editable text object and processes the mouse input at the RichTextBox level for caret
        // placement, so the child Image's instance PreviewMouseLeftButtonDown is never invoked.
        // The previous design relied solely on this handler to open the overlay, so nothing ever
        // opened. Selection is now driven from Editor_PreviewMouseLeftButtonDown below, which is a
        // tunnelling handler on the RichTextBox itself and fires reliably before the editor does
        // its own processing. This method is left as a harmless backup for any host where it does
        // fire (SelectImage is idempotent).
        private void Image_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isRawMode) return;
            if (sender is Image img)
            {
                SelectImage(img);
                e.Handled = true;
            }
        }

        private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isRawMode) return;

            var img = FindImageFrom(e.OriginalSource as DependencyObject);
            if (img != null)
            {
                SelectImage(img);
                e.Handled = true; // select the image rather than dropping a caret on it
            }
            else
            {
                DeselectImage();
            }
        }

        // Walk up from the click's original source to the enclosing inline Image, if any.
        private static Image? FindImageFrom(DependencyObject? src)
        {
            while (src != null)
            {
                if (src is Image im) return im;
                if (src is Border b && b.Child is Image bi) return bi;

                if (src is Visual || src is System.Windows.Media.Media3D.Visual3D)
                    src = VisualTreeHelper.GetParent(src);
                else
                    src = LogicalTreeHelper.GetParent(src);
            }
            return null;
        }

        private void SelectImage(Image img)
        {
            if (!ReferenceEquals(img, _selectedImage))
                DeselectImage();

            if (img.Source is not BitmapSource src || src.PixelWidth == 0)
                return;

            _selectedImage = img;
            _selectedBorder = img.Parent as Border;
            if (_selectedBorder != null) _selectedBorder.BorderBrush = AccentBrush;

            _aspect = (double)src.PixelHeight / src.PixelWidth;
            _dispW  = img.ActualWidth > 0
                ? img.ActualWidth
                : (double.IsNaN(img.Width) ? src.PixelWidth : img.Width);
            _dispH  = _dispW * _aspect;
            img.Width = _dispW; // pin a concrete width so resize deltas accumulate cleanly

            EnsureOverlayShown();
            ExitToNormalOverlay(); // sets normal-mode tool visibility and lays out the handles
        }

        private void DeselectImage()
        {
            HideOverlay();
            _cropMode = false;
            if (_selectedBorder != null) _selectedBorder.BorderBrush = Brushes.Transparent;
            _selectedImage = null;
            _selectedBorder = null;
        }

        // Add the overlay adorner to the Editor's AdornerLayer (creating it lazily on first use,
        // once the visual tree — and thus the layer — exists).
        private void EnsureOverlayShown()
        {
            if (_overlayAdorner == null)
            {
                _adornerLayer = AdornerLayer.GetAdornerLayer(Editor);
                if (_adornerLayer == null) return; // not yet in the visual tree
                _overlayAdorner = new SingleChildAdorner(Editor, OverlayCanvas);
            }
            OverlayCanvas.Visibility = Visibility.Visible;
            if (!_overlayShown && _adornerLayer != null)
            {
                _adornerLayer.Add(_overlayAdorner);
                _overlayShown = true;
            }
        }

        private void HideOverlay()
        {
            if (_overlayShown && _adornerLayer != null && _overlayAdorner != null)
            {
                _adornerLayer.Remove(_overlayAdorner);
                _overlayShown = false;
            }
            OverlayCanvas.Visibility = Visibility.Collapsed;
        }

        // Restore the normal (non-crop) overlay: resize handles + size/crop/remove toolbar.
        private void ExitToNormalOverlay()
        {
            _cropMode = false;
            NormalTools.Visibility = Visibility.Visible;
            CropTools.Visibility   = Visibility.Collapsed;
            SetResizeHandlesVisible(true);
            SetCropVisible(false);
            LayoutHandles();
        }

        private void SetResizeHandlesVisible(bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            ResizeNW.Visibility = ResizeNE.Visibility = ResizeSW.Visibility = ResizeSE.Visibility = v;
        }

        private void SetCropVisible(bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            MaskTop.Visibility = MaskBottom.Visibility = MaskLeft.Visibility = MaskRight.Visibility = v;
            CropRect.Visibility = CropBody.Visibility = v;
            CropNW.Visibility = CropNE.Visibility = CropSW.Visibility = CropSE.Visibility = v;
        }

        // Place the corner handles (and, in crop mode, the crop chrome) over the selected image,
        // using coordinates relative to the Editor — the same space the overlay adorner renders in.
        private void LayoutHandles()
        {
            if (_selectedImage == null || _overlayAdorner == null || !_overlayShown) return;

            Point tl;
            try { tl = _selectedImage.TransformToAncestor(Editor).Transform(new Point(0, 0)); }
            catch { return; } // image not currently realized in the visual tree

            // Clip the overlay to the editor's viewport so handles don't spill over other chrome
            // when the image is scrolled partly out of view.
            OverlayCanvas.Clip = new RectangleGeometry(
                new Rect(0, 0, Editor.ActualWidth, Editor.ActualHeight));

            double ox = tl.X, oy = tl.Y, w = _dispW, h = _dispH;

            Place(ResizeNW, ox - 6,     oy - 6);
            Place(ResizeNE, ox + w - 6, oy - 6);
            Place(ResizeSW, ox - 6,     oy + h - 6);
            Place(ResizeSE, ox + w - 6, oy + h - 6);

            LayoutToolbar(ox, oy, w, h);
            if (_cropMode) LayoutCrop(ox, oy);
        }

        // Float the toolbar just above the image, flipping below it when there's no room on top.
        private void LayoutToolbar(double ox, double oy, double w, double h)
        {
            OverlayToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double th = OverlayToolbar.DesiredSize.Height;
            double top = oy - th - 6;
            if (top < 0) top = oy + h + 6;
            Canvas.SetLeft(OverlayToolbar, Math.Max(0, ox));
            Canvas.SetTop(OverlayToolbar, top);
        }

        // ── Resize handles ──────────────────────────────────────────────────────
        private void ResizeSE_DragDelta(object s, DragDeltaEventArgs e) => ResizeBy(e.HorizontalChange);
        private void ResizeNE_DragDelta(object s, DragDeltaEventArgs e) => ResizeBy(e.HorizontalChange);
        private void ResizeSW_DragDelta(object s, DragDeltaEventArgs e) => ResizeBy(-e.HorizontalChange);
        private void ResizeNW_DragDelta(object s, DragDeltaEventArgs e) => ResizeBy(-e.HorizontalChange);

        private void ResizeBy(double dx)
        {
            if (_selectedImage == null) return;
            _dispW = Math.Max(40, _dispW + dx);
            _dispH = _dispW * _aspect;
            _selectedImage.Width = _dispW;
            LayoutHandles();
        }

        // ── Size presets ──────────────────────────────────────────────────────────
        private void PresetS_Click(object s, RoutedEventArgs e)    => ApplyPreset(160);
        private void PresetM_Click(object s, RoutedEventArgs e)    => ApplyPreset(320);
        private void PresetL_Click(object s, RoutedEventArgs e)    => ApplyPreset(480);
        private void PresetFull_Click(object s, RoutedEventArgs e) => ApplyPreset(Math.Max(80, Editor.ActualWidth - 40));

        private void ApplyPreset(double width)
        {
            if (_selectedImage == null) return;
            _dispW = width;
            _dispH = _dispW * _aspect;
            _selectedImage.Width = _dispW;
            LayoutHandles();
        }

        // ── Crop ────────────────────────────────────────────────────────────────
        private void Crop_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedImage == null) return;
            _cropMode = true;
            _cropX = _dispW * 0.1; _cropY = _dispH * 0.1;
            _cropW = _dispW * 0.8; _cropH = _dispH * 0.8;

            NormalTools.Visibility = Visibility.Collapsed;
            CropTools.Visibility   = Visibility.Visible;
            SetResizeHandlesVisible(false);
            SetCropVisible(true);
            LayoutHandles();
        }

        private void CancelCrop_Click(object sender, RoutedEventArgs e) => ExitToNormalOverlay();

        private void ApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedImage?.Source is not BitmapSource src) { ExitToNormalOverlay(); return; }

            double sx = src.PixelWidth  / _dispW;
            double sy = src.PixelHeight / _dispH;

            int x = (int)Math.Round(_cropX * sx);
            int y = (int)Math.Round(_cropY * sy);
            int w = (int)Math.Round(_cropW * sx);
            int h = (int)Math.Round(_cropH * sy);

            x = (int)Clamp(x, 0, src.PixelWidth  - 1);
            y = (int)Clamp(y, 0, src.PixelHeight - 1);
            w = (int)Clamp(w, 1, src.PixelWidth  - x);
            h = (int)Clamp(h, 1, src.PixelHeight - y);

            try
            {
                var cropped = new CroppedBitmap(src, new Int32Rect(x, y, w, h));
                var png = BitmapSourceToPng(cropped);
                var bmp = DecodePng(png);

                _selectedImage.Source = bmp;
                // Critical: keep ImageData in sync so the crop survives Save → reopen.
                RichTextBoxHelper.SetImageData(_selectedImage, Convert.ToBase64String(png));

                _dispW = _cropW;                 // keep the cropped region at its current size
                _aspect = (double)bmp.PixelHeight / bmp.PixelWidth;
                _dispH = _dispW * _aspect;
                _selectedImage.Width = _dispW;
            }
            catch { }

            ExitToNormalOverlay();
        }

        private void CropNW_DragDelta(object s, DragDeltaEventArgs e)
        { _cropX += e.HorizontalChange; _cropY += e.VerticalChange; _cropW -= e.HorizontalChange; _cropH -= e.VerticalChange; ClampCrop(); LayoutHandles(); }
        private void CropNE_DragDelta(object s, DragDeltaEventArgs e)
        { _cropY += e.VerticalChange; _cropW += e.HorizontalChange; _cropH -= e.VerticalChange; ClampCrop(); LayoutHandles(); }
        private void CropSW_DragDelta(object s, DragDeltaEventArgs e)
        { _cropX += e.HorizontalChange; _cropW -= e.HorizontalChange; _cropH += e.VerticalChange; ClampCrop(); LayoutHandles(); }
        private void CropSE_DragDelta(object s, DragDeltaEventArgs e)
        { _cropW += e.HorizontalChange; _cropH += e.VerticalChange; ClampCrop(); LayoutHandles(); }

        private void CropBody_DragDelta(object s, DragDeltaEventArgs e)
        {
            _cropX += e.HorizontalChange;
            _cropY += e.VerticalChange;
            ClampCrop();
            LayoutHandles();
        }

        private void ClampCrop()
        {
            _cropW = Clamp(_cropW, 20, _dispW);
            _cropH = Clamp(_cropH, 20, _dispH);
            _cropX = Clamp(_cropX, 0, _dispW - _cropW);
            _cropY = Clamp(_cropY, 0, _dispH - _cropH);
        }

        private void LayoutCrop(double ox, double oy)
        {
            double x = ox + _cropX, y = oy + _cropY, w = _cropW, h = _cropH;

            // Masks over the four regions outside the crop rectangle.
            Place(MaskTop,    ox,       oy,       _dispW,               _cropY);
            Place(MaskBottom, ox,       y + h,    _dispW,               _dispH - (_cropY + _cropH));
            Place(MaskLeft,   ox,       y,        _cropX,               _cropH);
            Place(MaskRight,  x + w,    y,        _dispW - (_cropX + _cropW), _cropH);

            Place(CropRect, x, y, w, h);
            Place(CropBody, x, y, w, h);

            Place(CropNW, x - 6,     y - 6);
            Place(CropNE, x + w - 6, y - 6);
            Place(CropSW, x - 6,     y + h - 6);
            Place(CropSE, x + w - 6, y + h - 6);
        }

        // ── Remove ────────────────────────────────────────────────────────────────
        private void RemoveImage_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedImage == null) return;
            var container = GetInlineUIContainers(Editor.Document)
                .FirstOrDefault(c => ReferenceEquals((c.Child as Border)?.Child, _selectedImage)
                                  || ReferenceEquals(c.Child, _selectedImage));
            if (container?.Parent is Paragraph p) p.Inlines.Remove(container);
            DeselectImage();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Load-time image wiring
        // ─────────────────────────────────────────────────────────────────────────

        // Ensure every inline image is wrapped in a selection Border and has the click-to-select
        // handler attached. XamlWriter/XamlReader don't round-trip event handlers, so images
        // loaded from stored XAML need re-wiring; images stored before this feature (bare Image in
        // an InlineUIContainer) also get wrapped so they become selectable.
        private void WireExistingImages()
        {
            if (Editor.Document == null) return;
            foreach (var container in GetInlineUIContainers(Editor.Document).ToList())
            {
                if (container.Child is Image bare)
                {
                    container.Child = null;
                    var border = NewSelectionBorder();
                    border.Child = bare;
                    container.Child = border;
                    AttachImageHandler(bare);
                }
                else if (container.Child is Border b && b.Child is Image wrapped)
                {
                    b.BorderThickness = new Thickness(2);
                    if (b.BorderBrush == null) b.BorderBrush = Brushes.Transparent;
                    AttachImageHandler(wrapped);
                }
            }
        }

        private static IEnumerable<InlineUIContainer> GetInlineUIContainers(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is InlineUIContainer iuc) yield return iuc;
                if (child is DependencyObject dep)
                    foreach (var sub in GetInlineUIContainers(dep))
                        yield return sub;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static void Place(FrameworkElement el, double left, double top,
                                  double w = double.NaN, double h = double.NaN)
        {
            Canvas.SetLeft(el, left);
            Canvas.SetTop(el, top);
            if (!double.IsNaN(w)) el.Width  = Math.Max(0, w);
            if (!double.IsNaN(h)) el.Height = Math.Max(0, h);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            if (hi < lo) hi = lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static SolidColorBrush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private static BitmapImage DecodePng(byte[] pngData)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(pngData);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static byte[] ConvertToPng(byte[] rawBytes)
        {
            using var ms = new MemoryStream(rawBytes);
            using var bmp = System.Drawing.Image.FromStream(ms);
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
            return pngMs.ToArray();
        }

        private static byte[] BitmapSourceToPng(BitmapSource bmpSrc)
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmpSrc));
            encoder.Save(ms);
            return ms.ToArray();
        }
    }

    // A minimal adorner that hosts a single existing UIElement (the overlay Canvas) and stretches
    // it over the adorned element. This puts the image-selection chrome in the Editor's
    // AdornerLayer — part of the main visual tree, drawn above the RichTextBox — so its Thumbs and
    // buttons receive mouse input reliably. The hosted canvas positions its own children in
    // Editor-relative coordinates (see LayoutHandles).
    internal sealed class SingleChildAdorner : Adorner
    {
        private readonly UIElement _child;

        public SingleChildAdorner(UIElement adornedElement, UIElement child) : base(adornedElement)
        {
            _child = child;
            AddLogicalChild(child);
            AddVisualChild(child);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _child;

        protected override Size MeasureOverride(Size constraint)
        {
            _child.Measure(constraint);
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _child.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
