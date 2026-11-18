using System.Collections.Generic;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class ProjectComparerTests
{
    private static LibraryEntry Lib(long id, string path, string? version = null, bool ignored = false)
        => new() { Id = id, RelativePath = path, FileName = path.Substring(path.LastIndexOf('\\') + 1), Version = version, IsIgnored = ignored };

    private static ProjectFamilyInfo Proj(string name, string? version = null) => new() { Name = name, Version = version };

    private static ProjectComparison Compare(LibraryEntry[] library, params ProjectFamilyInfo[] project)
        => ProjectComparer.Compare(library, project);

    [Fact]
    public void Match_SingleCopyInProject_IsMatched()
    {
        var r = Compare(new[] { Lib(1, @"A\Door.rfa", "2") }, Proj("Door", "1"));
        Assert.Equal("Door", r.Matches[1].Name);
        Assert.Empty(r.Duplicates);
        Assert.Empty(r.ModelOnly);
    }

    [Fact]
    public void IgnoredRow_IsNeverMatched_AndItsFamilyIsModelOnly()
    {
        var r = Compare(new[] { Lib(1, @"_Archive\Door.rfa", "5", ignored: true) }, Proj("Door", "1"));
        Assert.Empty(r.Matches);
        Assert.Equal("Door", Assert.Single(r.ModelOnly).Name);
    }

    [Fact]
    public void IgnoredCopy_DoesNotMakeADuplicate()
    {
        var r = Compare(new[] { Lib(1, @"_Archive\Door.rfa", "5", ignored: true), Lib(2, @"A\Door.rfa", "2") }, Proj("Door", "1"));
        Assert.True(r.Matches.ContainsKey(2));
        Assert.False(r.Matches.ContainsKey(1));
        Assert.Empty(r.Duplicates);
    }

    [Fact]
    public void Duplicate_IsCaseInsensitiveAcrossFolders()
    {
        var r = Compare(new[] { Lib(1, @"B\Door.rfa"), Lib(2, @"A\door.rfa") }, Proj("Door"));
        Assert.Empty(r.Matches);
        Assert.Equal(new[] { @"A\door.rfa", @"B\Door.rfa" }, r.Duplicates[1]);
        Assert.Equal(new[] { @"A\door.rfa", @"B\Door.rfa" }, r.Duplicates[2]);
        Assert.Empty(r.ModelOnly);
    }

    [Fact]
    public void Duplicate_NotInProject_IsNotReported()
    {
        var r = Compare(new[] { Lib(1, @"A\Door.rfa", "5"), Lib(2, @"B\Door.rfa", "6") });
        Assert.Empty(r.Duplicates);
        Assert.Empty(r.SkippedDuplicates);
    }

    [Fact]
    public void SkippedDuplicates_OnlyWhenACopyIsNewer()
    {
        var newer = Compare(new[] { Lib(1, @"A\Door.rfa", "3"), Lib(2, @"B\Door.rfa", "5") }, Proj("Door", "3"));
        var s = Assert.Single(newer.SkippedDuplicates);
        Assert.Equal("Door", s.Name);
        Assert.Equal(new[] { @"A\Door.rfa", @"B\Door.rfa" }, s.RelativePaths);

        var older = Compare(new[] { Lib(1, @"A\Door.rfa", "3"), Lib(2, @"B\Door.rfa", "2") }, Proj("Door", "3"));
        Assert.Empty(older.SkippedDuplicates);
    }

    [Fact]
    public void FamilyNotInLibrary_IsModelOnly()
    {
        var r = Compare(new[] { Lib(1, @"A\Door.rfa") }, Proj("Window"));
        Assert.Equal("Window", Assert.Single(r.ModelOnly).Name);
    }

    [Fact]
    public void DuplicateProjectNames_FirstOccurrenceWins()
    {
        var r = Compare(new[] { Lib(1, @"A\Door.rfa") }, Proj("Door", "1"), Proj("door", "9"));
        Assert.Equal("1", r.Matches[1].Version);
    }
}
