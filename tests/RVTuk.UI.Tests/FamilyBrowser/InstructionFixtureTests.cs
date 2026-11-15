using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    /// <summary>The checked-in fixtures are what they claim to be.</summary>
    public class InstructionFixtureTests
    {
        [Theory]
        [InlineData("legacy-net48-1.txt")]
        [InlineData("legacy-net48-2.txt")]
        public void LegacyFixtures_DeclareTheOldNamespace(string name)
        {
            Assert.Contains("xmlns:rvtuic=\"clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI\"",
                InstructionFixtures.Read(name));
        }

        [Theory]
        [InlineData("legacy-net48-1.txt")]
        [InlineData("legacy-net48-2.txt")]
        public void LegacyFixtures_HoldNoRealImage(string name)
        {
            string xaml = InstructionFixtures.Read(name);

            var images = Regex.Matches(xaml, "ImageData=\"([^\"]*)\"").Cast<Match>().ToList();
            Assert.NotEmpty(images);
            Assert.All(images, image => Assert.Equal(InstructionFixtures.OnePixelPng, image.Groups[1].Value));
            Assert.True(xaml.Length < 6000, $"{name} is {xaml.Length} chars");
        }

        [Fact]
        public void RawNet8Fixture_DeclaresCoreLib()
        {
            Assert.Contains("assembly=System.Private.CoreLib", InstructionFixtures.Read("raw-net8.txt"));
        }

        [Fact]
        public void RawNet48Fixture_DeclaresMscorlib()
        {
            string xaml = InstructionFixtures.Read("raw-net48.txt");

            Assert.Contains("assembly=mscorlib", xaml);
            Assert.DoesNotContain("System.Private.CoreLib", xaml);
        }
    }
}
