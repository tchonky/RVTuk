using System;
using System.IO;
using System.Linq;
using RVTuk.Core.FamilyBrowser.Config;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// The office keyboard shortcuts: a Revit export in the library's .DB folder, copied over each
// year's user file (office keys win, the old file kept as a backup).
public class OfficeShortcutsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rvtuk_keys_" + Guid.NewGuid().ToString("N"));
    private string UserRoot => Path.Combine(_root, "AppData", "Autodesk", "Revit");
    private string Library => Path.Combine(_root, "Library");

    private const string Office =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Shortcuts>\n" +
        "  <ShortcutItem CommandName=\"Family Browser\" CommandId=\"CustomCtrl_%CustomCtrl_%Add-Ins%RVTuk%FamilyBrowser\" Shortcuts=\"FB\" Paths=\"Add-Ins&gt;RVTuk\" />\n" +
        "  <ShortcutItem CommandName=\"Wall\" CommandId=\"ID_OBJECTS_WALL\" Shortcuts=\"WA\" />\n" +
        "</Shortcuts>";

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string Year(int year, string? content = null)
    {
        var folder = Path.Combine(UserRoot, "Autodesk Revit " + year);
        Directory.CreateDirectory(folder);
        if (content != null) File.WriteAllText(Path.Combine(folder, OfficeShortcuts.FileName), content);
        return folder;
    }

    private string WriteOffice(string content = Office)
    {
        var path = OfficeShortcuts.OfficeFile(Library);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void OfficeFile_SitsInTheDbFolder() =>
        Assert.Equal(Path.Combine(Library, ".DB", "KeyboardShortcuts.xml"), OfficeShortcuts.OfficeFile(Library));

    [Fact]
    public void Install_OverwritesEveryYear_AndKeepsTheOldFiles()
    {
        var y2024 = Year(2024, "<Shortcuts><ShortcutItem CommandId=\"ID_OBJECTS_WALL\" Shortcuts=\"WW\" /></Shortcuts>");
        var y2026 = Year(2026);                                   // started, never customised
        Directory.CreateDirectory(Path.Combine(UserRoot, "Addins")); // not a year folder
        var office = WriteOffice();

        var years = OfficeShortcuts.Install(office, UserRoot, new DateTime(2026, 11, 25, 9, 30, 0));

        Assert.Equal(new[] { 2024, 2026 }, years);
        Assert.Equal(Office, File.ReadAllText(Path.Combine(y2024, "KeyboardShortcuts.xml")));
        Assert.Equal(Office, File.ReadAllText(Path.Combine(y2026, "KeyboardShortcuts.xml")));
        Assert.Contains("WW", File.ReadAllText(Path.Combine(y2024, "KeyboardShortcuts.before-RVTuk-20261125-093000.xml")));
        Assert.Single(Directory.GetFiles(y2026)); // nothing to back up there
    }

    [Theory]
    [InlineData("<NotShortcuts />")]
    [InlineData("<Shortcuts></Shortcuts>")]
    [InlineData("not xml at all")]
    public void Install_RefusesAFileThatIsNotAShortcutsExport_AndChangesNothing(string content)
    {
        var y2024 = Year(2024, "<Shortcuts><ShortcutItem Shortcuts=\"WW\" /></Shortcuts>");
        var office = WriteOffice(content);

        Assert.Throws<InvalidDataException>(() => OfficeShortcuts.Install(office, UserRoot, DateTime.Now));
        Assert.Contains("WW", File.ReadAllText(Path.Combine(y2024, "KeyboardShortcuts.xml")));
        Assert.Single(Directory.GetFiles(y2024));
    }

    [Fact]
    public void Install_WithoutAnOfficeFile_SaysWhere()
    {
        Year(2024);
        var ex = Assert.Throws<InvalidDataException>(() =>
            OfficeShortcuts.Install(OfficeShortcuts.OfficeFile(Library), UserRoot, DateTime.Now));
        Assert.Contains(".DB", ex.Message);
    }

    [Fact]
    public void Publish_CopiesTheMaintainersNewestFileToTheLibrary()
    {
        var y2024 = Year(2024, Office.Replace("FB", "F1"));
        var y2025 = Year(2025, Office);
        File.SetLastWriteTimeUtc(Path.Combine(y2024, "KeyboardShortcuts.xml"), DateTime.UtcNow.AddDays(-3));

        var newest = OfficeShortcuts.NewestUserFile(UserRoot);
        Assert.Equal(2025, newest!.Value.Year);

        OfficeShortcuts.Publish(newest.Value.Path, OfficeShortcuts.OfficeFile(Library));
        Assert.Equal(Office, File.ReadAllText(OfficeShortcuts.OfficeFile(Library)));
    }

    [Fact]
    public void NoRevitFolders_NothingToDo()
    {
        Assert.Empty(OfficeShortcuts.UserYears(UserRoot));
        Assert.Null(OfficeShortcuts.NewestUserFile(UserRoot));
    }
}
