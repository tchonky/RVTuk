using System;
using System.Collections.Generic;
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class FolderSnapshotTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);

    private static FileStamp Stamp(string path, long size = 100, int minutesLate = 0)
        => new FileStamp { Path = path, Size = size, ModifiedUtc = T0.AddMinutes(minutesLate) };

    [Fact]
    public void Diff_ReportsAFileThatDidNotExistBefore()
    {
        var before = new List<FileStamp> { Stamp(@"D:\out\old.dwg") };
        var after = new List<FileStamp> { Stamp(@"D:\out\old.dwg"), Stamp(@"D:\out\new.dwg") };

        Assert.Equal(new[] { @"D:\out\new.dwg" }, FolderSnapshot.Diff(before, after));
    }

    [Fact]
    public void Diff_ReportsAFileWhoseSizeChanged()
    {
        var before = new List<FileStamp> { Stamp(@"D:\out\a.dwg", size: 100) };
        var after = new List<FileStamp> { Stamp(@"D:\out\a.dwg", size: 250) };

        Assert.Equal(new[] { @"D:\out\a.dwg" }, FolderSnapshot.Diff(before, after));
    }

    [Fact]
    public void Diff_ReportsAFileWhoseTimestampChanged()
    {
        // A re-export that happens to produce the same byte count must still be caught.
        var before = new List<FileStamp> { Stamp(@"D:\out\a.dwg", minutesLate: 0) };
        var after = new List<FileStamp> { Stamp(@"D:\out\a.dwg", minutesLate: 5) };

        Assert.Equal(new[] { @"D:\out\a.dwg" }, FolderSnapshot.Diff(before, after));
    }

    [Fact]
    public void Diff_IgnoresAnUntouchedFile()
    {
        var before = new List<FileStamp> { Stamp(@"D:\out\a.dwg"), Stamp(@"D:\out\b.png") };
        var after = new List<FileStamp> { Stamp(@"D:\out\a.dwg"), Stamp(@"D:\out\b.png") };

        Assert.Empty(FolderSnapshot.Diff(before, after));
    }

    [Fact]
    public void Diff_IgnoresAFileThatDisappeared()
    {
        // Nothing was produced, so a deletion is not our business.
        var before = new List<FileStamp> { Stamp(@"D:\out\gone.dwg") };
        var after = new List<FileStamp>();

        Assert.Empty(FolderSnapshot.Diff(before, after));
    }

    [Fact]
    public void Diff_MatchesPathsCaseInsensitively()
    {
        // Windows paths: the same file re-reported with different casing is not new.
        var before = new List<FileStamp> { Stamp(@"D:\out\A.dwg") };
        var after = new List<FileStamp> { Stamp(@"D:\out\a.dwg") };

        Assert.Empty(FolderSnapshot.Diff(before, after));
    }

    [Fact]
    public void Diff_EmptyBefore_ReportsEverything()
    {
        var after = new List<FileStamp> { Stamp(@"D:\out\a.dwg"), Stamp(@"D:\out\b.png") };

        Assert.Equal(new[] { @"D:\out\a.dwg", @"D:\out\b.png" }, FolderSnapshot.Diff(new List<FileStamp>(), after));
    }
}
