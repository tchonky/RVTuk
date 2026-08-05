using System;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;

string dir = Path.Combine(Path.GetTempPath(), "rvtuk_repro_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);

int Run(string variant, int iterations)
{
    int leaks = 0;
    for (int i = 0; i < iterations; i++)
    {
        var db = Path.Combine(dir, variant + "_" + i + ".db");
        long id;
        using (var index = new IndexRepository(db))
            id = index.InsertFamily("Doors/A.rfa", "A.rfa");

        using (var repo = new BrowserRepository(db))
        {
            if (variant is "getall" or "full") repo.GetAllFamilies();
            if (variant is "savetags" or "full") repo.SaveTags(id, "door");
        }

        try { File.Delete(db); }
        catch (IOException) { leaks++; }
    }
    return leaks;
}

foreach (var v in new[] { "ctor", "getall", "savetags", "full" })
    Console.WriteLine($"{v}: {Run(v, 10)}/10 leaked");
