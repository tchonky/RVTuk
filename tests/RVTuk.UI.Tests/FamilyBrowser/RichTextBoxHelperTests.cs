using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RVTuk.UI.Shared.Controls;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    public class RichTextBoxHelperTests
    {
        private const string StoredDeclaration = "=\"clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI\"";

        private static string Serialize(FlowDocument document)
        {
            string? xaml = RichTextBoxHelper.SerializeDocument(document);
            Assert.NotNull(xaml);
            return xaml;
        }

        private static string TextOf(FlowDocument document) =>
            new TextRange(document.ContentStart, document.ContentEnd).Text;

        [Fact]
        public void Serialize_WritesTheStoredDeclaration() => Sta.Run(() =>
        {
            string xaml = Serialize(InstructionFixtures.SampleDocument());

            Assert.Contains(StoredDeclaration, xaml);
            Assert.DoesNotContain("System.Private.CoreLib", xaml);
        });

        [Fact]
        public void Serialize_LeavesTheHoverStyleOut() => Sta.Run(() =>
        {
            string xaml = Serialize(InstructionFixtures.SampleDocument());

            Assert.DoesNotContain("<Style", xaml);
            Assert.DoesNotContain("Border.Style", xaml);
        });

        [Fact]
        public void Serialize_PutsStyleAndSourceBack() => Sta.Run(() =>
        {
            FlowDocument document = InstructionFixtures.SampleDocument();
            RichTextBoxHelper.RehydrateImages(document);
            Image image = Assert.Single(InstructionFixtures.Images(document));
            Border border = Assert.IsType<Border>(image.Parent);
            ImageSource? source = image.Source;
            Style style = border.Style;
            Assert.NotNull(source);
            Assert.NotNull(style);

            Serialize(document);

            Assert.Same(source, image.Source);
            Assert.Same(style, border.Style);
        });

        [Fact]
        public void Serialize_ThenParse_KeepsTextAndImage() => Sta.Run(() =>
        {
            string xaml = Serialize(InstructionFixtures.SampleDocument());

            Assert.True(RichTextBoxHelper.TryParseDocumentXaml(xaml, out FlowDocument document));
            Image image = Assert.Single(InstructionFixtures.Images(document));
            Assert.Equal(InstructionFixtures.OnePixelPng, RichTextBoxHelper.GetImageData(image));
            Assert.Contains("Sample text", TextOf(document));
        });

        [Fact]
        public void Parse_Failure_IsReported() => Sta.Run(() =>
        {
            const string xaml =
                "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><NoSuchElement /></FlowDocument>";

            Assert.False(RichTextBoxHelper.TryParseDocumentXaml(xaml, out FlowDocument document));
            Assert.Contains(RichTextBoxHelper.UnreadableNotice, TextOf(document));
        });

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Parse_Blank_IsAReadableEmptyDocument(string? xaml) => Sta.Run(() =>
        {
            Assert.True(RichTextBoxHelper.TryParseDocumentXaml(xaml, out FlowDocument document));
            Assert.Empty(document.Blocks);
        });

        [Fact]
        public void Parse_BrokenImageBytes_StillShowsTheText() => Sta.Run(() =>
        {
            string xaml = Serialize(InstructionFixtures.SampleDocument())
                .Replace(InstructionFixtures.OnePixelPng, "not-base64!");
            Assert.Contains("ImageData=\"not-base64!\"", xaml);

            Assert.True(RichTextBoxHelper.TryParseDocumentXaml(xaml, out FlowDocument document));
            Assert.Contains("Sample text", TextOf(document));
            Image image = Assert.Single(InstructionFixtures.Images(document));
            Assert.Null(image.Source);
        });

        [Fact]
        public void LegacyRow_SavedAgain_IsInTheStoredForm() => Sta.Run(() =>
        {
            Assert.True(RichTextBoxHelper.TryParseDocumentXaml(
                InstructionFixtures.Read("legacy-net48-1.txt"), out FlowDocument legacy));

            string xaml = Serialize(legacy);

            Assert.Contains(StoredDeclaration, xaml);
            Assert.DoesNotContain("RVTuk.UI.Controls;", xaml);
            Assert.DoesNotContain("<Style", xaml);
            Assert.Single(InstructionFixtures.Images(RichTextBoxHelper.ParseDocumentXaml(xaml)));
        });
    }
}
