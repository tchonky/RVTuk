using System;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// The shared index DB lives on a network share used by the whole office, so a Revit session
// must not keep file handles on it beyond the operation that needed them — a lingering
// read-write handle from one user's Revit is what blocked everyone else's DB access
// (backlog "Read Only DB"). Microsoft.Data.Sqlite pools connections by default, which keeps
// the underlying handle open long after Dispose; the repositories must opt out.
// File.Delete only fails on Windows while a handle is open, so these assertions bite on
// Windows (where the product runs) and are trivially green on Linux CI.
public class RepositoryConnectionLifetimeTests
{
    private static string NewDbPath(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "rvtuk_lifetime_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "test.db");
    }

    [Fact]
    public void IndexRepository_Dispose_ReleasesDatabaseFile()
    {
        var dbPath = NewDbPath(out var root);
        try
        {
            using (var repo = new IndexRepository(dbPath))
                repo.InsertFamily("Doors/A.rfa", "A.rfa");

            File.Delete(dbPath); // throws IOException if a handle is still open
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BrowserRepository_Dispose_ReleasesDatabaseFile_AfterReadsAndWrites()
    {
        var dbPath = NewDbPath(out var root);
        try
        {
            long id;
            using (var index = new IndexRepository(dbPath))
                id = index.InsertFamily("Doors/A.rfa", "A.rfa");

            using (var repo = new BrowserRepository(dbPath))
            {
                repo.GetAllFamilies();
                repo.SaveTags(id, "door"); // exercises the short-lived write connection
            }

            File.Delete(dbPath); // throws IOException if a handle is still open
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }
}
