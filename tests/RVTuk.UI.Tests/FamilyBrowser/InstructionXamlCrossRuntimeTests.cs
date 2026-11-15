using System.Windows.Controls;
using System.Windows.Documents;
using RVTuk.UI.Shared.Controls;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    /// <summary>
    /// Every stored form, written by either runtime, opens on this one with its image; what the
    /// helper saves now is in the stored form.
    /// </summary>
    public class InstructionXamlCrossRuntimeTests
    {
        [Theory]
        [InlineData("legacy-net48-1.txt")]
        [InlineData("legacy-net48-2.txt")]
        [InlineData("raw-net48.txt")]
        [InlineData("raw-net8.txt")]
        [InlineData("saved-net48.txt")]
        [InlineData("saved-net8.txt")]
        public void Fixture_OpensWithItsImage(string fixture) => Sta.Run(() =>
        {
            FlowDocument document = RichTextBoxHelper.ParseDocumentXaml(InstructionFixtures.Read(fixture));
            Image image = Assert.Single(InstructionFixtures.Images(document));
            Assert.Equal(InstructionFixtures.OnePixelPng, RichTextBoxHelper.GetImageData(image));
            Assert.NotNull(image.Source);
        });

        [Theory]
        [InlineData("saved-net48.txt")]
        [InlineData("saved-net8.txt")]
        public void SavedFixtures_AreInTheStoredForm(string fixture)
        {
            string xaml = InstructionFixtures.Read(fixture);

            Assert.Contains("=\"clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI\"", xaml);
            Assert.DoesNotContain("System.Private.CoreLib", xaml);
            Assert.DoesNotContain("<Style", xaml);
        }

        [Fact]
        public void SavedFixture_SavedAgain_IsByteIdentical() => Sta.Run(() =>
        {
            // The stored form must come through the split unchanged: what this runtime saved,
            // opened and saved again, is the same text to the last byte.
            string stored = InstructionFixtures.Read($"saved-{InstructionFixtures.Runtime}.txt");
            Assert.True(RichTextBoxHelper.TryParseDocumentXaml(stored, out FlowDocument document));

            Assert.Equal(stored, RichTextBoxHelper.SerializeDocument(document));
        });
    }
}
