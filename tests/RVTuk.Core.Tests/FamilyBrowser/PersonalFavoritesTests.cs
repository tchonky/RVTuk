using System;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class PersonalFavoritesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rvtuk_fav_" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "sub", "favorites.txt");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void MissingFile_StartsEmpty()
    {
        Assert.False(new PersonalFavorites(FilePath).Contains(@"Doors\A.rfa"));
    }

    [Fact]
    public void Favourites_SurviveARestart()
    {
        var favs = new PersonalFavorites(FilePath);
        Assert.True(favs.Set(@"Doors\A.rfa", true));
        favs.Set(@"Doors\B.rfa", true);
        favs.Set(@"Doors\B.rfa", false);

        var reloaded = new PersonalFavorites(FilePath);

        Assert.True(reloaded.Contains(@"Doors\A.rfa"));
        Assert.False(reloaded.Contains(@"Doors\B.rfa"));
    }

    [Fact]
    public void Paths_MatchRegardlessOfCaseAndSlashDirection()
    {
        var favs = new PersonalFavorites(FilePath);
        favs.Set(@"Doors\Single Door.rfa", true);

        Assert.True(favs.Contains("doors/single door.RFA"));
    }

    [Fact]
    public void EmptyPath_IsNeverAFavourite()
    {
        var favs = new PersonalFavorites(FilePath);

        Assert.False(favs.Set(string.Empty, true));
        Assert.False(favs.Contains(string.Empty));
    }
}
