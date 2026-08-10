using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class TransmittalWriterTests : IDisposable
{
    private readonly string _root;

    public TransmittalWriterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rvtuk_zip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string MakeFile(string name, string content = "x")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static TransmittalContents Contents(params TransmittalEntry[] entries)
        => new TransmittalContents { Entries = entries };

    private static TransmittalEntry Entry(string path, TransmittalKind kind = TransmittalKind.Drawing)
        => new TransmittalEntry { SourcePath = path, EntryName = Path.GetFileName(path), Kind = kind };

    private static List<string> EntryNames(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        return zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void Write_PutsEveryEntryAndTheReportInTheArchive()
    {
        var archive = Path.Combine(_root, "bundle.zip");
        var contents = Contents(Entry(MakeFile("A-101.dwg")), Entry(MakeFile("logo.png"), TransmittalKind.Image));

        var warnings = TransmittalWriter.Write(archive, contents, "report text");

        Assert.Empty(warnings);
        Assert.Equal(new[] { "A-101.dwg", "TRANSMITTAL.txt", "logo.png" }, EntryNames(archive));
    }

    [Fact]
    public void Write_StoresEntriesFlat_NotUnderTheirFolders()
    {
        // A DWG references its images by bare filename; nesting would break that.
        var sub = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        var nested = Path.Combine(sub, "deep.dwg");
        File.WriteAllText(nested, "x");
        var archive = Path.Combine(_root, "bundle.zip");

        TransmittalWriter.Write(archive, Contents(Entry(nested)), "report");

        Assert.Contains("deep.dwg", EntryNames(archive));
    }

    [Fact]
    public void Write_ReportContentIsReadableBack()
    {
        var archive = Path.Combine(_root, "bundle.zip");

        TransmittalWriter.Write(archive, Contents(Entry(MakeFile("A-101.dwg"))), "hello transmittal");

        using var zip = ZipFile.OpenRead(archive);
        using var reader = new StreamReader(zip.GetEntry("TRANSMITTAL.txt")!.Open());
        Assert.Equal("hello transmittal", reader.ReadToEnd());
    }

    [Fact]
    public void Write_AMissingSourceFileIsSkippedAndWarned_NotThrown()
    {
        var archive = Path.Combine(_root, "bundle.zip");
        var contents = Contents(
            Entry(MakeFile("A-101.dwg")),
            Entry(Path.Combine(_root, "vanished.dwg")));

        var warnings = TransmittalWriter.Write(archive, contents, "report");

        Assert.Contains(warnings, w => w.Contains("vanished.dwg"));
        // The rest of the bundle still gets written.
        Assert.Contains("A-101.dwg", EntryNames(archive));
    }

    [Fact]
    public void Write_OverwritesAnExistingArchive()
    {
        var archive = Path.Combine(_root, "bundle.zip");
        File.WriteAllText(archive, "stale content");

        TransmittalWriter.Write(archive, Contents(Entry(MakeFile("A-101.dwg"))), "report");

        Assert.Equal(new[] { "A-101.dwg", "TRANSMITTAL.txt" }, EntryNames(archive));
    }

    [Fact]
    public void Write_DuplicateEntryNames_AreKeptDistinct()
    {
        // Two folders could in principle yield the same basename; the archive must not
        // silently drop one.
        var sub = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        var second = Path.Combine(sub, "A-101.dwg");
        File.WriteAllText(second, "different");
        var archive = Path.Combine(_root, "bundle.zip");

        TransmittalWriter.Write(archive, Contents(Entry(MakeFile("A-101.dwg")), Entry(second)), "report");

        Assert.Equal(3, EntryNames(archive).Count);
    }
}
