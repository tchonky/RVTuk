using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace RVTuk.UI.Shared.Controls
{
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty DocumentXamlProperty =
            DependencyProperty.RegisterAttached(
                "DocumentXaml",
                typeof(string),
                typeof(RichTextBoxHelper),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.None,
                    OnDocumentXamlChanged));

        public static string? GetDocumentXaml(DependencyObject obj)
            => (string?)obj.GetValue(DocumentXamlProperty);

        public static void SetDocumentXaml(DependencyObject obj, string? value)
            => obj.SetValue(DocumentXamlProperty, value);

        // Renders a markdown string (e.g. Help panel content fetched over HTTP, or a fallback
        // string) into the RichTextBox via MarkdownConverter.Build, following the exact same
        // attached-property pattern as DocumentXaml above.
        public static readonly DependencyProperty MarkdownSourceProperty =
            DependencyProperty.RegisterAttached(
                "MarkdownSource",
                typeof(string),
                typeof(RichTextBoxHelper),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.None,
                    OnMarkdownSourceChanged));

        public static string? GetMarkdownSource(DependencyObject obj)
            => (string?)obj.GetValue(MarkdownSourceProperty);

        public static void SetMarkdownSource(DependencyObject obj, string? value)
            => obj.SetValue(MarkdownSourceProperty, value);

        // Base64-encoded PNG bytes for an inline image. XamlWriter.Save cannot serialize a
        // BitmapImage built from a MemoryStream (it has no UriSource), and attempting it throws,
        // which previously caused SerializeDocument to lose the entire document. We instead carry
        // the bytes in this plain string property — which serializes fine — and rebuild the live
        // Image.Source from it on load.
        public static readonly DependencyProperty ImageDataProperty =
            DependencyProperty.RegisterAttached(
                "ImageData",
                typeof(string),
                typeof(RichTextBoxHelper),
                new FrameworkPropertyMetadata(null));

        public static string? GetImageData(DependencyObject obj)
            => (string?)obj.GetValue(ImageDataProperty);

        public static void SetImageData(DependencyObject obj, string? value)
            => obj.SetValue(ImageDataProperty, value);

        private static bool _updating;

        private static void OnDocumentXamlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (_updating || d is not RichTextBox rtb) return;
            _updating = true;
            try
            {
                rtb.Document = ParseDocumentXaml(e.NewValue as string);
            }
            finally
            {
                _updating = false;
            }
        }

        // Parse a XAML string into a FlowDocument (rehydrating inline image sources from their
        // ImageData), returning an empty document on null/empty/parse-failure. Shared by the
        // DocumentXaml attached-property callback and the editor's Raw→Preview toggle so both use
        // identical parse-and-rehydrate semantics.
        public static FlowDocument ParseDocumentXaml(string? xaml)
        {
            if (string.IsNullOrWhiteSpace(xaml))
                return new FlowDocument();
            try
            {
                var doc = (FlowDocument)XamlReader.Parse(xaml);
                RehydrateImages(doc);
                return doc;
            }
            catch
            {
                return new FlowDocument();
            }
        }

        private static bool _updatingMarkdown;

        private static void OnMarkdownSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (_updatingMarkdown || d is not RichTextBox rtb) return;
            _updatingMarkdown = true;
            try
            {
                var markdown = e.NewValue as string;
                rtb.Document = string.IsNullOrEmpty(markdown)
                    ? new FlowDocument()
                    : RVTuk.UI.Shared.Helpers.MarkdownConverter.Build(markdown);
            }
            finally
            {
                _updatingMarkdown = false;
            }
        }

        // Call this to read the current document back as a XAML string
        public static string? SerializeDocument(RichTextBox rtb)
        {
            if (rtb.Document == null) return null;

            // BitmapImage sources built from a stream are not XAML-serializable, so temporarily
            // null them (the bytes live on in ImageData) and restore them afterward so the live
            // editor is left untouched.
            var images = new List<Image>();
            CollectImages(rtb.Document, images);
            var savedSources = new ImageSource?[images.Count];
            for (int i = 0; i < images.Count; i++)
            {
                savedSources[i] = images[i].Source;
                images[i].Source = null;
            }
            try
            {
                return XamlWriter.Save(rtb.Document);
            }
            catch { return null; }
            finally
            {
                for (int i = 0; i < images.Count; i++)
                    images[i].Source = savedSources[i];
            }
        }

        // Rebuild each Image.Source from its base64 ImageData after a document is parsed from XAML.
        public static void RehydrateImages(FlowDocument doc)
        {
            var images = new List<Image>();
            CollectImages(doc, images);
            foreach (var img in images)
            {
                if (img.Source != null) continue;
                var data = GetImageData(img);
                if (string.IsNullOrEmpty(data)) continue;
                try
                {
                    var bytes = Convert.FromBase64String(data!);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new MemoryStream(bytes);
                    bmp.CacheOption  = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    img.Source = bmp;
                }
                catch { /* leave the single image blank rather than fail the whole document */ }
            }
        }

        private static void CollectImages(DependencyObject node, List<Image> images)
        {
            if (node is Image image) images.Add(image);
            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject dep) CollectImages(dep, images);
        }

        // Hyperlink.RequestNavigate handler for windows showing instructions. Opens only web and
        // mail links: instructions are shared office content, so file:/relative links are ignored.
        public static void OpenLink(object sender, RequestNavigateEventArgs e)
        {
            var uri = e.Uri;
            if (uri != null && uri.IsAbsoluteUri &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto))
            {
                try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                catch { /* no handler for the scheme, etc. */ }
            }
            e.Handled = true;
        }
    }
}
