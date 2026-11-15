using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RVTuk.UI.Shared.Controls;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    internal static class InstructionFixtures
    {
        // A valid 1x1 PNG. The sanitizer script writes the same bytes into the legacy fixtures.
        public const string OnePixelPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

#if NETFRAMEWORK
        public const string Runtime = "net48";
#else
        public const string Runtime = "net8";
#endif

        public static string Read(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "FamilyBrowser", "Fixtures", name));

        // The shape the editor produces: text, then an image inside a hover-styled border.
        public static FlowDocument SampleDocument()
        {
            var image = new Image { Width = 40, Stretch = Stretch.Uniform };
            RichTextBoxHelper.SetImageData(image, OnePixelPng);
            var paragraph = new Paragraph(new Run("Sample text"));
            paragraph.Inlines.Add(new InlineUIContainer(new Border { Style = HoverStyle(), Child = image }));
            return new FlowDocument(paragraph);
        }

        // InstructionsEditorWindow.xaml's "ImageHoverBorder", built in code.
        public static Style HoverStyle()
        {
            var style = new Style(typeof(Border));
            style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2)));
            style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9))));
            style.Triggers.Add(hover);
            return style;
        }

        // Every Image in the logical tree under root.
        public static List<Image> Images(DependencyObject root)
        {
            var images = new List<Image>();
            Collect(root);
            return images;

            void Collect(DependencyObject node)
            {
                if (node is Image image) images.Add(image);
                foreach (var child in LogicalTreeHelper.GetChildren(node))
                    if (child is DependencyObject dependencyObject) Collect(dependencyObject);
            }
        }
    }
}
