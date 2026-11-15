using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    /// <summary>The checked-in fixtures are what they claim to be.</summary>
    public class InstructionFixtureTests
    {
        // Every text a fixture may hold between tags: the placeholder text, and the values the
        // serialised hover style carries. Anything else could be a real instruction.
        private static readonly string[] PlaceholderTexts =
            { "Sample text", "#FF4A90D9", "#00FFFFFF", "True", "2,2,2,2" };

        private static readonly Regex ElementTag = new Regex("<[^<>]*>");
        private static readonly Regex TagOrToolTip = new Regex("(?<!\\w)(Tag|ToolTip)=\"([^\"]*)\"");
        private static readonly Regex ImageData = new Regex("ImageData=\"([^\"]*)\"");

        // Every fixture on disk, not a list of names, so a new fixture is checked without anyone
        // remembering to add it here. The repository is treated as public: no office text may get in.
        [Fact]
        public void EveryFixture_HoldsOnlyPlaceholders()
        {
            string[] files = Directory.GetFiles(InstructionFixtures.Folder, "*.txt");
            Assert.True(files.Length >= 6, $"{files.Length} fixtures found in {InstructionFixtures.Folder}");

            var problems = new List<string>();
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);

                int nonAscii = Array.FindIndex(File.ReadAllBytes(file), b => b > 0x7F);
                if (nonAscii >= 0) problems.Add($"{name}: a non-ASCII byte at offset {nonAscii}");

                string xaml = File.ReadAllText(file);
                foreach (string text in ElementTag.Split(xaml))
                    if (!string.IsNullOrWhiteSpace(text) && Array.IndexOf(PlaceholderTexts, text) < 0)
                        problems.Add($"{name}: text \"{Shorten(text)}\"");
                foreach (Match match in TagOrToolTip.Matches(xaml))
                    if (match.Groups[2].Value != "sample")
                        problems.Add($"{name}: {match.Groups[1].Value}=\"{Shorten(match.Groups[2].Value)}\"");
                foreach (Match match in ImageData.Matches(xaml))
                    if (match.Groups[1].Value != InstructionFixtures.OnePixelPng)
                        problems.Add($"{name}: an ImageData that is not the 1x1 PNG");
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

            static string Shorten(string value) => value.Length <= 40 ? value : value.Substring(0, 40) + "...";
        }

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
