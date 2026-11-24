using System;
using System.Collections.Generic;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// The browser's search box: categories OR, tags AND, words AND, favourites AND. The Project and
// Library built-in tags replace the old ⬅/➡ toggles, so their truth table is carried over here.
public class FamilyFilterTests
{
    private static readonly HashSet<string> NoCategories = new(StringComparer.Ordinal);

    private static bool Show(VersionStatus status, params string[] requiredTags) =>
        FamilyFilter.Matches("Door", "Doors", FamilyFilter.TagsOf(null, status, fixRequested: false), false,
            NoCategories, requiredTags, Array.Empty<string>(), favoritesOnly: false);

    [Theory]
    [InlineData(VersionStatus.None)]
    [InlineData(VersionStatus.UpToDate)]
    [InlineData(VersionStatus.UpdateAvailable)]
    [InlineData(VersionStatus.ModelOnly)]
    [InlineData(VersionStatus.DuplicateInLibrary)]
    public void NothingChosen_ShowsEverything(VersionStatus status) => Assert.True(Show(status));

    [Theory]
    // "Project": loaded in the open project — everything except library-only rows.
    [InlineData(VersionStatus.None, false)]
    [InlineData(VersionStatus.UpToDate, true)]
    [InlineData(VersionStatus.UpdateAvailable, true)]
    [InlineData(VersionStatus.ModelOnly, true)]
    [InlineData(VersionStatus.DuplicateInLibrary, true)]
    public void ProjectTag_ShowsFamiliesLoadedInTheProject(VersionStatus status, bool expected) =>
        Assert.Equal(expected, Show(status, BuiltInTags.Project));

    [Theory]
    // "Library": in the library — everything except model-only rows.
    [InlineData(VersionStatus.None, true)]
    [InlineData(VersionStatus.UpToDate, true)]
    [InlineData(VersionStatus.UpdateAvailable, true)]
    [InlineData(VersionStatus.ModelOnly, false)]
    [InlineData(VersionStatus.DuplicateInLibrary, true)]
    public void LibraryTag_ShowsFamiliesInTheLibrary(VersionStatus status, bool expected) =>
        Assert.Equal(expected, Show(status, BuiltInTags.Library));

    [Theory]
    // Both: tags are AND, so only families that are in both places.
    [InlineData(VersionStatus.None, false)]
    [InlineData(VersionStatus.UpToDate, true)]
    [InlineData(VersionStatus.UpdateAvailable, true)]
    [InlineData(VersionStatus.ModelOnly, false)]
    [InlineData(VersionStatus.DuplicateInLibrary, true)]
    public void ProjectAndLibrary_ShowOnlyFamiliesInBoth(VersionStatus status, bool expected) =>
        Assert.Equal(expected, Show(status, BuiltInTags.Project, BuiltInTags.Library));

    [Fact]
    public void BuiltInTags_MatchWhateverTheCase() =>
        Assert.True(Show(VersionStatus.UpToDate, "project", "LIBRARY"));

    [Fact]
    public void FixRequested_IsATagOnlyWhileARequestIsOpen()
    {
        Assert.Contains(BuiltInTags.FixRequested, FamilyFilter.TagsOf(null, VersionStatus.None, fixRequested: true));
        Assert.DoesNotContain(BuiltInTags.FixRequested, FamilyFilter.TagsOf(null, VersionStatus.None, fixRequested: false));
    }

    [Fact]
    public void OwnTagsCombineWithBuiltInsAsAnd()
    {
        var tags = FamilyFilter.TagsOf("interior, steel", VersionStatus.None, false);
        Assert.True(FamilyFilter.HasAllTags(tags, new[] { "interior", BuiltInTags.Library }));
        Assert.False(FamilyFilter.HasAllTags(tags, new[] { "interior", BuiltInTags.Project }));
        Assert.False(FamilyFilter.HasAllTags(tags, new[] { "interior", "glass" }));
    }

    [Fact]
    public void ChosenTag_MustMatchWholeTag_NotPartOfOne()
    {
        var tags = FamilyFilter.TagsOf("interior", VersionStatus.None, false);
        Assert.False(FamilyFilter.HasAllTags(tags, new[] { "inter" }));
    }

    [Fact]
    public void Categories_AreOr_AndNoneTickedMeansAll()
    {
        var ticked = new HashSet<string> { "Doors", "Windows" };
        Assert.True(FamilyFilter.MatchesCategory("Doors", ticked));
        Assert.True(FamilyFilter.MatchesCategory("Windows", ticked));
        Assert.False(FamilyFilter.MatchesCategory("Furniture", ticked));
        Assert.True(FamilyFilter.MatchesCategory("Furniture", new HashSet<string>()));
    }

    [Theory]
    // Every word must appear in the name or in any tag, in any order.
    [InlineData("door single", true)]
    [InlineData("single DOOR", true)]
    [InlineData("door interior", true)]   // one word in the name, one in a tag
    [InlineData("door lib", true)]        // built-in tags are searchable words too
    [InlineData("door glass", false)]
    public void Words_AllMustAppearInNameOrTags(string text, bool expected)
    {
        var tags = FamilyFilter.TagsOf("interior, דלת פנים", VersionStatus.None, false);
        Assert.Equal(expected, FamilyFilter.MatchesText("KK_Door_Single_90", tags, FamilyFilter.Tokens(text)));
    }

    [Fact]
    public void HebrewTag_MatchesItsWords()
    {
        var tags = FamilyFilter.TagsOf("interior, דלת פנים", VersionStatus.None, false);
        Assert.True(FamilyFilter.MatchesText("KK_Door_Single_90", tags, FamilyFilter.Tokens("דלת")));
    }

    [Fact]
    public void Favourites_NarrowTheList()
    {
        var tags = FamilyFilter.TagsOf(null, VersionStatus.None, false);
        Assert.False(FamilyFilter.Matches("Door", "Doors", tags, false, NoCategories,
            Array.Empty<string>(), Array.Empty<string>(), favoritesOnly: true));
        Assert.True(FamilyFilter.Matches("Door", "Doors", tags, true, NoCategories,
            Array.Empty<string>(), Array.Empty<string>(), favoritesOnly: true));
    }

    [Theory]
    [InlineData("interior, Project, library", "interior")]
    [InlineData("  a ,, b , A ", "a, b")]
    [InlineData("Fix requested", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CleanStoredTags_DropsBuiltInsBlanksAndRepeats(string? typed, string? expected) =>
        Assert.Equal(expected, FamilyFilter.CleanStoredTags(typed));
}
