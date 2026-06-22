// ReviTchucky.UI/Views/InstructionsEditorWindow.xaml.cs
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReviTchucky.Core.Database;
using ReviTchucky.UI.Controls;
using ReviTchucky.UI.ViewModels;

namespace ReviTchucky.UI.Views
{
    public partial class InstructionsEditorWindow : Window
    {
        public InstructionsEditorViewModel ViewModel { get; }

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

        // Insert a plain inline image tagged with its PNG bytes (base64) so it survives the
        // XAML save/load round-trip. A Grid+Button overlay would not serialize (the Button's
        // Click handler is dropped and the bare ✕ would persist as content); instead, delete an
        // image by placing the caret after it and pressing Backspace — the container removes as one.
        private void InsertImageIntoEditor(byte[] pngData)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(pngData);
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                var image = new Image { Source = bmp, MaxWidth = 400, Stretch = Stretch.Uniform };
                RichTextBoxHelper.SetImageData(image, Convert.ToBase64String(pngData));

                new InlineUIContainer(image, Editor.CaretPosition);
            }
            catch { }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
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
        private void Caption_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb &&
                tb.Tag is long id &&
                DataContext is ViewModels.InstructionsEditorViewModel vm)
            {
                vm.SaveCaption(id, tb.Text);
            }
        }

        private void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
                { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() == true)
                InsertImageIntoEditor(File.ReadAllBytes(dlg.FileName));
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
}
