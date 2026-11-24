using System.Linq;
using RVTuk.UI.FamilyBrowser.ViewModels;
using RVTuk.UI.FamilyBrowser.Views;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    // The icons are SVG path data pasted into WPF: a command WPF's path syntax can't read would
    // only show up as a crash when the Family Browser opens, so every one is parsed here.
    public class TablerIconsTests
    {
        [Fact]
        public void EveryIcon_ParsesAsAWpfGeometry()
        {
            foreach (var name in TablerIcons.PathData.Keys)
            {
                var g = TablerIcons.Get(name);
                Assert.NotNull(g);
                Assert.False(g!.IsEmpty(), name);
                Assert.True(g.Bounds.Width <= 24 && g.Bounds.Height <= 24, name); // drawn on the 24×24 canvas
            }
        }

        [Fact]
        public void EveryCategoryMapping_PointsToAnIcon() =>
            Assert.All(TablerIcons.CategoryIcon.Values, v => Assert.True(TablerIcons.PathData.ContainsKey(v), v));

        [Theory]
        [InlineData("Doors", "door")]
        [InlineData("doors", "door")]
        [InlineData("Multi-Category Tags", "tag")]
        [InlineData("Lighting Fixture Tags", "tag")]
        [InlineData("Something New", "box")]
        [InlineData("", "box")]
        [InlineData(null, "box")]
        public void Category_GetsItsIcon(string? category, string expected) =>
            Assert.Equal(expected, TablerIcons.IconNameForCategory(category));

        [Theory]
        [InlineData("ארון גבוה — tall cabinet", true)]
        [InlineData("  \"60\" ארון", true)]          // digits and punctuation are not letters
        [InlineData("Tall cabinet — ארון גבוה", false)]
        [InlineData("", false)]
        public void RequestText_RunsRightToLeftWhenItStartsWithHebrew(string text, bool expected) =>
            Assert.Equal(expected, RequestItemViewModel.StartsRightToLeft(text));
    }
}
