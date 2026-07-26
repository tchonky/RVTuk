using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// The three toolbar toggles are independent AND constraints (each pressed toggle narrows the
// list). The model/library pair are *inclusive* membership filters — "in the model" and "in
// the library" — so pressing both yields the intersection: families that exist in both.
public class SourceToggleFilterTests
{
    [Theory]
    // No toggles pressed: nothing is hidden, whatever the status or favourite flag.
    [InlineData(VersionStatus.None, false, true)]
    [InlineData(VersionStatus.UpToDate, false, true)]
    [InlineData(VersionStatus.UpdateAvailable, true, true)]
    [InlineData(VersionStatus.ModelOnly, false, true)]
    public void NoTogglesPressed_ShowsEverything(VersionStatus status, bool isFavorite, bool expected)
    {
        Assert.Equal(expected, SourceToggleFilter.Matches(status, isFavorite,
            showModel: false, showLibrary: false, showFavorites: false));
    }

    [Theory]
    // "In the model": model-only rows and in-both rows; hides library-only (None).
    [InlineData(VersionStatus.None, false)]
    [InlineData(VersionStatus.UpToDate, true)]
    [InlineData(VersionStatus.UpdateAvailable, true)]
    [InlineData(VersionStatus.ModelOnly, true)]
    public void ModelToggle_ShowsFamiliesLoadedInTheModel(VersionStatus status, bool expected)
    {
        Assert.Equal(expected, SourceToggleFilter.Matches(status, isFavorite: false,
            showModel: true, showLibrary: false, showFavorites: false));
    }

    [Theory]
    // "In the library": library-only rows and in-both rows; hides model-only.
    [InlineData(VersionStatus.None, true)]
    [InlineData(VersionStatus.UpToDate, true)]
    [InlineData(VersionStatus.UpdateAvailable, true)]
    [InlineData(VersionStatus.ModelOnly, false)]
    public void LibraryToggle_ShowsFamiliesInTheLibrary(VersionStatus status, bool expected)
    {
        Assert.Equal(expected, SourceToggleFilter.Matches(status, isFavorite: false,
            showModel: false, showLibrary: true, showFavorites: false));
    }

    [Theory]
    // Both pressed: the intersection — only families that exist in both the model and the
    // library. This is the view the old OR/union semantics could not express.
    [InlineData(VersionStatus.None, false)]
    [InlineData(VersionStatus.UpToDate, true)]
    [InlineData(VersionStatus.UpdateAvailable, true)]
    [InlineData(VersionStatus.ModelOnly, false)]
    public void ModelAndLibraryToggles_ShowOnlyFamiliesInBoth(VersionStatus status, bool expected)
    {
        Assert.Equal(expected, SourceToggleFilter.Matches(status, isFavorite: false,
            showModel: true, showLibrary: true, showFavorites: false));
    }

    [Theory]
    // Favourites alone: every favourite regardless of where it lives, and nothing else.
    [InlineData(VersionStatus.None, true, true)]
    [InlineData(VersionStatus.UpToDate, true, true)]
    [InlineData(VersionStatus.ModelOnly, true, true)]
    [InlineData(VersionStatus.None, false, false)]
    [InlineData(VersionStatus.UpToDate, false, false)]
    [InlineData(VersionStatus.UpdateAvailable, false, false)]
    [InlineData(VersionStatus.ModelOnly, false, false)]
    public void FavoritesToggle_ShowsOnlyFavorites(VersionStatus status, bool isFavorite, bool expected)
    {
        Assert.Equal(expected, SourceToggleFilter.Matches(status, isFavorite,
            showModel: false, showLibrary: false, showFavorites: true));
    }

    [Theory]
    // Favourites combined with a source toggle narrows (AND), it does not union: a
    // favourite that is not in the model stays hidden, and a non-favourite in the model
    // stays hidden too.
    [InlineData(VersionStatus.UpToDate, true, true)]
    [InlineData(VersionStatus.ModelOnly, true, true)]
    [InlineData(VersionStatus.None, true, false)]
    [InlineData(VersionStatus.UpToDate, false, false)]
    public void FavoritesPlusModelToggle_Intersects(VersionStatus status, bool isFavorite, bool expected)
    {
        Assert.Equal(expected, SourceToggleFilter.Matches(status, isFavorite,
            showModel: true, showLibrary: false, showFavorites: true));
    }

    [Theory]
    // All three pressed: favourites that exist in both the model and the library.
    [InlineData(VersionStatus.UpToDate, true, true)]
    [InlineData(VersionStatus.UpdateAvailable, true, true)]
    [InlineData(VersionStatus.UpdateAvailable, false, false)]
    [InlineData(VersionStatus.None, true, false)]
    [InlineData(VersionStatus.ModelOnly, true, false)]
    public void AllTogglesPressed_FavoritesInBoth(VersionStatus status, bool isFavorite, bool expected)
    {
        Assert.Equal(expected, SourceToggleFilter.Matches(status, isFavorite,
            showModel: true, showLibrary: true, showFavorites: true));
    }
}
