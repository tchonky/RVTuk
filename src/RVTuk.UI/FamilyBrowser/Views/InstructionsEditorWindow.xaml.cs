// RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.UI.Shared.Controls;
using RVTuk.UI.Shared.Helpers;
using RVTuk.UI.FamilyBrowser.ViewModels;

namespace RVTuk.UI.FamilyBrowser.Views
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
        private string? _rawSnapshot; // Markdown as generated on entering Raw

        // ── Unsaved-changes tracking (see OnClosing) ─────────────────────────────
        private string? _baseline; // Snapshot() once loaded
        private bool _thumbChanged, _saved;

        // Image targeted by the right-click context menu.
        private Image? _menuTargetImage;

        // Corner-drag resize state (image being dragged, pointer + width at drag start).
        private Image? _resizeImage;
        private Point _resizeStart;
        private double _resizeStartWidth;
        private const double GripSize = 16; // px square at the image's bottom-right corner

        private const string ImageTip = "Drag the bottom-right corner to resize · right-click to resize, crop, or replace.";

        public InstructionsEditorWindow(
            FamilyBrowserItemViewModel item,
            string rfaFullPath,
            BrowserRepository repo)
        {
            InitializeComponent();

            ViewModel = new InstructionsEditorViewModel(
                item.Id, item.FileName, rfaFullPath, repo);

            ViewModel.CloseRequested += () => Dispatcher.Invoke(Close);
            ViewModel.PropertyChanged += (s, e) =>
                _thumbChanged |= e.PropertyName == nameof(InstructionsEditorViewModel.ThumbnailSource);

            DataContext = ViewModel;

            // Ctrl+Click on a link in the (editable) editor raises RequestNavigate.
            AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(RichTextBoxHelper.OpenLink));

            // Fix 3: handle Ctrl+V paste into the editor body
            Editor.PreviewKeyDown += Editor_PreviewKeyDown;

            // Right-click an inline image to format it (size / replace / crop / remove).
            Editor.ContextMenuOpening += Editor_ContextMenuOpening;

            // Drag-drop onto thumbnail
            ThumbnailImage.Drop     += ThumbnailImage_Drop;
            ThumbnailImage.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            // Drag-drop onto editor body (Preview events; the RichTextBox vetoes plain drops)
            Editor.PreviewDragOver += Editor_PreviewDragOver;
            Editor.PreviewDrop     += Editor_PreviewDrop;

            // Wire up any images that arrived from stored XAML, then remember the loaded state.
            // Requery: the toolbar's commands asked CanExecute before CommandTarget had resolved.
            Loaded += (s, e) =>
            {
                WireExistingImages(); UpdateToggleButtons(); _baseline = Snapshot();
                CommandManager.InvalidateRequerySuggested();
            };
        }

        // Any close but a successful Save (Cancel, Esc, title-bar X, the browser switching family)
        // asks before throwing changes away.
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!e.Cancel && !_saved && IsDirty() &&
                MessageBox.Show(this, $"Discard your unsaved changes to '{ViewModel.FamilyDisplayName}'?",
                    "RVTuk", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                e.Cancel = true;
        }

        private bool IsDirty() =>
            _thumbChanged || (_isRawMode && RawEditor.Text != _rawSnapshot) || Snapshot() != _baseline;

        // Document (as it would be saved) + tags.
        private string Snapshot() => RichTextBoxHelper.SerializeDocument(Editor) + "\0" + ViewModel.TagsText;

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
            // Ctrl+V with an image on the clipboard goes to the thumbnail whenever the writing area
            // isn't focused; when the editor has focus it falls through to Editor_PreviewKeyDown
            // (inline paste). Text paste into the Tags box is unaffected (no image on the clipboard).
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (!Editor.IsKeyboardFocusWithin && Clipboard.ContainsImage())
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

        // The RichTextBox's internal editor vetoes image drops in its own DragOver; handling the
        // tunnelling Preview events (and marking them handled) is what makes image drops work.
        // Everything else (moving text, text from other apps) is left to the editor.
        private void Editor_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!HasImagePayload(e.Data)) return;
            e.Effects = DragDropEffects.Copy;
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

        private void LoadThumbnailFromFile(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                ViewModel.SetThumbnailFromBytes(InstructionsEditorViewModel.ConvertToPng(bytes));
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
                // Raw mode shows/edits Markdown, not the serialized XAML — the document is still
                // SAVED as XAML (see Save_Click); this only changes the human-editable view.
                RawEditor.Text            = _rawSnapshot = MarkdownConverter.ToMarkdown(
                                                Editor.Document, RichTextBoxHelper.GetImageData);
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

        // Parse the raw Markdown back into the live document and re-wire its images. MarkdownConverter.Build
        // emits bare Images, stashing the source base64 on Image.Tag; we copy that into
        // RichTextBoxHelper.ImageData so the images persist on Save, then WireExistingImages wraps them.
        private void SyncRawToEditor()
        {
            // Raw text untouched: keep the document. Markdown can't carry image widths, RTL
            // paragraphs, underline or line breaks, so a rebuild would lose them.
            if (RawEditor.Text == _rawSnapshot) return;
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

        // Insert an inline image (any format; stored as PNG) wrapped in a hover Border and tagged
        // with its PNG bytes (base64) so it survives the XAML save/load round-trip.
        private void InsertImageIntoEditor(byte[] imageData)
        {
            try
            {
                var pngData = InstructionsEditorViewModel.ConvertToPng(imageData);
                var bmp = DecodePng(pngData);

                var image = new Image
                {
                    Source  = bmp,
                    Stretch = Stretch.Uniform,
                    Width   = Math.Min(400, bmp.PixelWidth)
                };
                WireImage(image);
                RichTextBoxHelper.SetImageData(image, Convert.ToBase64String(pngData));

                var border = NewSelectionBorder();
                border.Child = image;

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
            _saved = true;
            Close();
        }

        // Formatting toolbar handlers (B/I/U and List use the native EditingCommands in XAML)
        private void H1_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 22.0);
        private void H2_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 16.0);
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
        // Inline image: right-click context menu
        // ─────────────────────────────────────────────────────────────────────────

        private Border NewSelectionBorder() =>
            new Border { Style = (Style)Resources["ImageHoverBorder"] };

        // Handle ContextMenuOpening (not the mouse event): the RichTextBox raises its default
        // cut/copy/paste menu here, so marking the event handled is what suppresses it. We then
        // open our own image menu. OriginalSource can be a text run beside the image, so also fall
        // back to the element directly under the mouse.
        private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_isRawMode) return; // let the default editing menu show
            var img = FindImageFrom(e.OriginalSource as DependencyObject)
                      ?? FindImageFrom(Mouse.DirectlyOver as DependencyObject);
            if (img == null) return; // not on an image — allow the default editing menu

            _menuTargetImage = img;
            e.Handled = true; // suppress the RichTextBox's default cut/copy/paste menu
            var menu = (ContextMenu)Resources["ImageMenu"];
            menu.PlacementTarget = Editor;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
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
                var png = InstructionsEditorViewModel.ConvertToPng(File.ReadAllBytes(dlg.FileName));
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

        // ─────────────────────────────────────────────────────────────────────────
        // Inline image: corner-drag resize
        // ─────────────────────────────────────────────────────────────────────────

        // True when the pointer is inside the resize grip zone at the image's bottom-right.
        private static bool InGripZone(Image img, Point p) =>
            p.X >= img.ActualWidth - GripSize && p.Y >= img.ActualHeight - GripSize;

        private void Image_MouseMove(object sender, MouseEventArgs e)
        {
            var img = (Image)sender;
            if (ReferenceEquals(_resizeImage, img) && img.IsMouseCaptured)
            {
                // Measure against the editor, not the image: the image moves under the
                // pointer while it resizes, which would feed back into the delta.
                double delta = e.GetPosition(Editor).X - _resizeStart.X;
                img.Width = Math.Max(40, Math.Min(2000, _resizeStartWidth + delta));
                e.Handled = true;
            }
        }

        // Resize cursor over the grip and while dragging. QueryCursor, not the Cursor property:
        // a property value is saved with the document, so a mere hover would count as an edit.
        private void Image_QueryCursor(object sender, QueryCursorEventArgs e)
        {
            var img = (Image)sender;
            if (!ReferenceEquals(_resizeImage, img) && !InGripZone(img, e.GetPosition(img))) return;
            e.Cursor  = Cursors.SizeNWSE;
            e.Handled = true;
        }

        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var img = (Image)sender;
            if (!InGripZone(img, e.GetPosition(img))) return;
            _resizeImage      = img;
            _resizeStart      = e.GetPosition(Editor);
            _resizeStartWidth = double.IsNaN(img.Width) ? img.ActualWidth : img.Width;
            img.CaptureMouse();
            e.Handled = true;
        }

        private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var img = (Image)sender;
            if (!ReferenceEquals(_resizeImage, img)) return;
            img.ReleaseMouseCapture();
            _resizeImage = null;
            e.Handled = true;
        }

        private void Image_LostMouseCapture(object sender, MouseEventArgs e) =>
            _resizeImage = null;

        // Attach tooltip + resize handlers. Idempotent (remove-then-add) because images can be
        // re-wired, e.g. WireExistingImages running over a document that already went through
        // InsertImageIntoEditor.
        private void WireImage(Image img)
        {
            img.ToolTip = ImageTip;
            img.ClearValue(CursorProperty); // older saves stored the hover cursor
            img.QueryCursor         -= Image_QueryCursor;
            img.QueryCursor         += Image_QueryCursor;
            img.MouseMove           -= Image_MouseMove;
            img.MouseMove           += Image_MouseMove;
            img.MouseLeftButtonDown -= Image_MouseLeftButtonDown;
            img.MouseLeftButtonDown += Image_MouseLeftButtonDown;
            img.MouseLeftButtonUp   -= Image_MouseLeftButtonUp;
            img.MouseLeftButtonUp   += Image_MouseLeftButtonUp;
            img.LostMouseCapture    -= Image_LostMouseCapture;
            img.LostMouseCapture    += Image_LostMouseCapture;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Load-time image wiring
        // ─────────────────────────────────────────────────────────────────────────

        // Ensure every inline image is wrapped in a selection Border. XamlWriter/XamlReader don't
        // round-trip event handlers, so images loaded from stored XAML need re-wrapping; images
        // stored before this feature (bare Image in an InlineUIContainer) also get wrapped.
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
                    WireImage(bare);
                }
                else if (container.Child is Border b && b.Child is Image wrapped)
                {
                    b.Style = (Style)Resources["ImageHoverBorder"];
                    WireImage(wrapped);
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

        private static byte[] BitmapSourceToPng(BitmapSource bmpSrc)
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmpSrc));
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
