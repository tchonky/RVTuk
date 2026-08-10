using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class TransmittalBuilderTests
{
    private const string Archive = @"D:\out\Issue 01_2026-08-04_1030.zip";

    private static TransmittalContents Build(
        IEnumerable<string> produced, IEnumerable<ResolvedFont>? fonts = null)
        => TransmittalBuilder.Build(
            produced.ToList(), (fonts ?? Enumerable.Empty<ResolvedFont>()).ToList(), Archive);

    [Fact]
    public void Build_KeepsDrawingsAndImages_WithFlatEntryNames()
    {
        var contents = Build(new[] { @"D:\out\A-101.dwg", @"D:\out\logo.png" });

        Assert.Equal(new[] { "A-101.dwg", "logo.png" }, contents.Entries.Select(e => e.EntryName));
        Assert.Equal(
            new[] { TransmittalKind.Drawing, TransmittalKind.Image },
            contents.Entries.Select(e => e.Kind));
    }

    [Fact]
    public void Build_ExcludesPdfs()
    {
        // A run may write PDFs into the same folder; the bundle is a CAD transmittal.
        var contents = Build(new[] { @"D:\out\A-101.dwg", @"D:\out\A-101.pdf" });

        Assert.Equal(new[] { "A-101.dwg" }, contents.Entries.Select(e => e.EntryName));
    }

    [Fact]
    public void Build_ExcludesTheArchiveItself()
    {
        var contents = Build(new[] { @"D:\out\A-101.dwg", Archive });

        Assert.Equal(new[] { "A-101.dwg" }, contents.Entries.Select(e => e.EntryName));
    }

    [Fact]
    public void Build_ClassifiesByExtension_CaseInsensitively()
    {
        var contents = Build(new[]
        {
            @"D:\out\A-101.DWG", @"D:\out\photo.JPG", @"D:\out\survey.tif", @"D:\out\notes.txt",
        });

        Assert.Equal(
            new[]
            {
                TransmittalKind.Drawing, TransmittalKind.Image,
                TransmittalKind.Image, TransmittalKind.Other,
            },
            contents.Entries.Select(e => e.Kind));
    }

    [Fact]
    public void Build_AFoundFontBecomesAnEntry()
    {
        var contents = Build(
            new[] { @"D:\out\A-101.dwg" },
            new[] { new ResolvedFont { Name = "romans", FilePath = @"C:\Fonts\romans.shx" } });

        var font = Assert.Single(contents.Entries, e => e.Kind == TransmittalKind.Font);
        Assert.Equal("romans.shx", font.EntryName);
        Assert.Equal(@"C:\Fonts\romans.shx", font.SourcePath);
        Assert.Empty(contents.FontsNotIncluded);
    }

    [Fact]
    public void Build_AnUnfoundFontIsListedButNotBundled()
    {
        var contents = Build(
            new[] { @"D:\out\A-101.dwg" },
            new[] { new ResolvedFont { Name = "Arial", FilePath = null } });

        Assert.DoesNotContain(contents.Entries, e => e.Kind == TransmittalKind.Font);
        Assert.Equal(new[] { "Arial" }, contents.FontsNotIncluded);
    }

    [Fact]
    public void Build_TheSameFontResolvedTwice_IsBundledOnce()
    {
        // Several text styles commonly map to one SHX.
        var contents = Build(
            new[] { @"D:\out\A-101.dwg" },
            new[]
            {
                new ResolvedFont { Name = "romans", FilePath = @"C:\Fonts\romans.shx" },
                new ResolvedFont { Name = "ROMANS", FilePath = @"C:\Fonts\romans.shx" },
            });

        Assert.Single(contents.Entries, e => e.Kind == TransmittalKind.Font);
    }

    [Fact]
    public void Build_NothingProduced_YieldsNoEntries()
    {
        Assert.Empty(Build(new string[0]).Entries);
    }
}
