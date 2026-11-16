using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RVTuk.Setup.Tests
{
    public sealed class EngineTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rvtuk-engine-tests-" + Guid.NewGuid().ToString("N"));
        private readonly string _src, _m;
        private readonly FakeMachine _machine;

        public EngineTests()
        {
            _src = Path.Combine(_dir, "src");
            _m = Path.Combine(_dir, "m");
            Directory.CreateDirectory(_src);
            Directory.CreateDirectory(_m);
            _machine = new FakeMachine(_m) { Installed = new HashSet<string> { "2024" } };
        }

        public void Dispose() { Directory.Delete(_dir, true); }

        private string M(params string[] parts) => Path.Combine(new[] { _m }.Concat(parts).ToArray());

        private static CatalogAddIn Entry(string key, string version = "1.0.0", bool dev = false, params string[] years) => new CatalogAddIn
        {
            Key = key, Name = key, Manifest = key + ".addin", Assembly = "Host.dll", EntryClass = "X.App",
            ClientId = "11111111-1111-1111-1111-111111111111", Version = version, Dev = dev,
            Years = years.Length == 0 ? new List<string> { "2024" } : years.ToList(), Replaces = new List<OldInstall>(),
        };

        private CatalogAddIn Cat(string key, string version = "1.0.0", bool dev = false, params string[] years)
        {
            var a = Entry(key, version, dev, years);
            foreach (var y in a.Years) Payload(key, y, version, dev);
            return a;
        }

        private static Catalog Of(params CatalogAddIn[] a) => new Catalog { AddIns = a.ToList() };

        private static void WriteAddIn(string folder, string key, string version, bool dev, params string[] files)
        {
            Directory.CreateDirectory(folder);
            foreach (var f in files) File.WriteAllText(Path.Combine(folder, f), f);
            File.WriteAllText(Path.Combine(folder, "addin.json"),
                "{\"key\": \"" + key + "\", \"version\": \"" + version + "\", \"commit\": \"abc\", \"built\": \"2026-11-16T10:00:00Z\", " +
                "\"manifest\": \"" + key + ".addin\", \"dev\": " + (dev ? "true" : "false") + "}");
        }

        private void Payload(string key, string year, string version, bool dev) =>
            WriteAddIn(Path.Combine(_src, key, year), key, version, dev, "Host.dll", "Lib.dll");

        private void Installed(string key, string year, string version = "0.9.0", bool dev = false)
        {
            WriteAddIn(M(year, key), key, version, dev, "Host.dll", "Lib.dll", "stale.dll");
            File.WriteAllText(M(year, key + ".addin"), Engine.ManifestXml(Entry(key, version)), new UTF8Encoding(false));
        }

        private void OldInstall(CatalogAddIn a)
        {
            a.Replaces.Add(new OldInstall { Manifest = "Old.addin", Folder = "Old" });
            Directory.CreateDirectory(M("2024", "Old"));
            File.WriteAllText(M("2024", "Old", "x.dll"), "x");
            File.WriteAllText(M("2024", "Old.addin"), "<RevitAddIns/>");
        }

        private List<Result> Install(Catalog c, ICollection<string> keys = null, ICollection<string> years = null) =>
            Engine.Install(c, _src, _machine, keys, years, _ => { });

        private List<Result> Remove(Catalog c, ICollection<string> keys = null, ICollection<string> years = null) =>
            Engine.Remove(c, _machine, keys, years, _ => { });

        private void NoLeftovers(string year = "2024", string key = "A")
        {
            Assert.False(Directory.Exists(M(year, key + ".new")));
            Assert.False(Directory.Exists(M(year, key + ".old")));
        }

        [Fact]
        public void Install_Fresh_CopiesFilesAndWritesTheManifest()
        {
            var a = Cat("A");
            var r = Assert.Single(Install(Of(a)));
            Assert.Equal(Outcome.Installed, r.Outcome);
            foreach (var f in new[] { "Host.dll", "Lib.dll", "addin.json" }) Assert.True(File.Exists(M("2024", "A", f)), f);
            Assert.Equal(Engine.ManifestXml(a), File.ReadAllText(M("2024", "A.addin")));
            Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(M("2024", "A.addin")).Take(3).ToArray());
            NoLeftovers();
        }

        [Fact]
        public void ManifestXml_IsTodaysManifest()
        {
            var a = Entry("RVTuk");
            a.Assembly = "RVTuk.Revit.dll";
            a.EntryClass = "RVTuk.Revit.Application";
            a.ClientId = "D71D7480-4A21-474E-A47E-3E8DF8C1BDA5";
            string xml = Engine.ManifestXml(a);
            Assert.Equal(418, Encoding.UTF8.GetByteCount(xml));
            Assert.EndsWith("</RevitAddIns>\r\n", xml);
            Assert.DoesNotContain("\n", xml.Replace("\r\n", ""));
            Assert.Contains("<Assembly>RVTuk\\RVTuk.Revit.dll</Assembly>", xml);
            Assert.Contains("<VendorId>KnafoKlimor</VendorId>", xml);
        }

        [Fact]
        public void Install_OverAnOlderVersion_ReplacesTheWholeFolder()
        {
            var a = Cat("A");
            Installed("A", "2024");
            Assert.Equal(Outcome.Updated, Assert.Single(Install(Of(a))).Outcome);
            Assert.False(File.Exists(M("2024", "A", "stale.dll")));
            Assert.Equal(Engine.ManifestXml(a), File.ReadAllText(M("2024", "A.addin")));
            NoLeftovers();
        }

        [Fact]
        public void Install_SameVersion_WritesNothing()
        {
            var a = Cat("A");
            Installed("A", "2024", "1.0.0");
            Assert.Equal(Outcome.Unchanged, Assert.Single(Install(Of(a))).Outcome);
            Assert.True(File.Exists(M("2024", "A", "stale.dll")));
            Assert.False(Directory.Exists(M("2024", "A.new")));
        }

        [Fact]
        public void Install_OlderCatalog_LeavesTheNewerInstall()
        {
            var a = Cat("A");
            Installed("A", "2024", "1.2.0");
            Assert.Equal(Outcome.Newer, Assert.Single(Install(Of(a))).Outcome);
            Assert.True(File.Exists(M("2024", "A", "stale.dll")));
        }

        [Fact]
        public void Install_TwoPartVersion_EqualsThreePart()
        {
            var a = Cat("A", "1.0");
            Installed("A", "2024", "1.0.0");
            Assert.Equal(Outcome.Unchanged, Assert.Single(Install(Of(a))).Outcome);
        }

        [Fact]
        public void Install_OverADevBuild_Replaces()
        {
            var a = Cat("A");
            Installed("A", "2024", "1.0.0", dev: true);
            Assert.Equal(Outcome.Updated, Assert.Single(Install(Of(a))).Outcome);
        }

        [Fact]
        public void Install_DevCatalog_AlwaysReplaces()
        {
            var a = Cat("A", "1.0.0", dev: true);
            Installed("A", "2024", "1.2.0");
            Assert.Equal(Outcome.Updated, Assert.Single(Install(Of(a))).Outcome);
        }

        [Fact]
        public void Install_OverAFolderWithoutAddInJson_Replaces()
        {
            var a = Cat("A");
            Installed("A", "2024");
            File.Delete(M("2024", "A", "addin.json"));
            Assert.Equal(Outcome.Updated, Assert.Single(Install(Of(a))).Outcome);
        }

        [Fact]
        public void Install_SameVersionButNoManifest_Reinstalls()
        {
            var a = Cat("A");
            Installed("A", "2024", "1.0.0");
            File.Delete(M("2024", "A.addin"));
            Assert.Equal(Outcome.Updated, Assert.Single(Install(Of(a))).Outcome);
            Assert.True(File.Exists(M("2024", "A.addin")));
        }

        [Fact]
        public void Install_SameVersionButOldInstallStillThere_RemovesIt()
        {
            var a = Cat("A");
            Installed("A", "2024", "1.0.0");
            OldInstall(a);
            Assert.Equal(Outcome.Updated, Assert.Single(Install(Of(a))).Outcome);
            Assert.False(Directory.Exists(M("2024", "Old")));
            Assert.False(File.Exists(M("2024", "Old.addin")));
        }

        [Fact]
        public void Install_PartialYears_OnlyWhereRevitIs()
        {
            var a = Cat("A", "1.0.0", false, "2024", "2025", "2026", "2027");
            _machine.Installed = new HashSet<string> { "2024", "2026" };
            Assert.Equal(new[] { "2024", "2026" }, Install(Of(a)).Select(r => r.Year));
            Assert.False(Directory.Exists(M("2025")));
            Assert.False(Directory.Exists(M("2027")));
        }

        [Fact]
        public void Install_ExplicitYears_OverrideDetection()
        {
            var a = Cat("A", "1.0.0", false, "2024", "2025");
            _machine.Installed = new HashSet<string>();
            var r = Assert.Single(Install(Of(a), years: new[] { "2025" }));
            Assert.Equal(Outcome.Installed, r.Outcome);
            Assert.Equal("2025", r.Year);
            Assert.True(File.Exists(M("2025", "A", "Host.dll")));
        }

        [Fact]
        public void Install_YearOutsideTheAddIn_IsIgnored()
        {
            var a = Cat("A", "1.0.0", false, "2023");
            Assert.Empty(Install(Of(a), years: new[] { "2024" }));
            Assert.False(Directory.Exists(M("2024")));
        }

        [Fact]
        public void Install_RevitOpen_SkipsThatYearOnly()
        {
            var a = Cat("A", "1.0.0", false, "2024", "2026");
            Installed("A", "2024");
            Installed("A", "2026");
            _machine.Installed = new HashSet<string> { "2024", "2026" };
            _machine.Running = new HashSet<string> { "2026" };
            var results = Install(Of(a));
            Assert.Equal(Outcome.Updated, results.Single(r => r.Year == "2024").Outcome);
            Assert.Equal(Outcome.SkippedRevitOpen, results.Single(r => r.Year == "2026").Outcome);
            Assert.True(File.Exists(M("2026", "A", "stale.dll")));
            Assert.True(Engine.Failed(results));
        }

        [Fact]
        public void Install_DifferingYears_EachByItsOwnState()
        {
            var a = Cat("A", "1.0.0", false, "2024", "2026");
            Installed("A", "2024", "1.0.0");
            Installed("A", "2026", "0.9.0");
            _machine.Installed = new HashSet<string> { "2024", "2026" };
            var results = Install(Of(a));
            Assert.Equal(Outcome.Unchanged, results.Single(r => r.Year == "2024").Outcome);
            Assert.Equal(Outcome.Updated, results.Single(r => r.Year == "2026").Outcome);
        }

        [Fact]
        public void Install_FileInUse_ChangesNothing()
        {
            var a = Cat("A");
            Installed("A", "2024");
            List<Result> results;
            using (new FileStream(M("2024", "A", "Lib.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
                results = Install(Of(a));
            var r = Assert.Single(results);
            Assert.Equal(Outcome.SkippedInUse, r.Outcome);
            Assert.Contains("Lib.dll", r.Message);
            foreach (var f in new[] { "Host.dll", "Lib.dll", "stale.dll" }) Assert.True(File.Exists(M("2024", "A", f)), f);
            Assert.True(File.Exists(M("2024", "A.addin")));
            NoLeftovers();
            Assert.True(Engine.Failed(results));
        }

        [Fact]
        public void Install_ManifestCannotBeWritten_RollsBack()
        {
            var a = Cat("A");
            Installed("A", "2024");
            File.Delete(M("2024", "A.addin"));
            Directory.CreateDirectory(M("2024", "A.addin"));
            Assert.Equal(Outcome.Failed, Assert.Single(Install(Of(a))).Outcome);
            Assert.True(File.Exists(M("2024", "A", "stale.dll")));
            NoLeftovers();
            Assert.False(File.Exists(M("2024", "A.addin.tmp")));
        }

        [Fact]
        public void Install_AfterACrash_RestoresTheOldFolderFirst()
        {
            var a = Cat("A");
            Installed("A", "2024", "1.2.0");
            Directory.Move(M("2024", "A"), M("2024", "A.old"));
            Assert.Equal(Outcome.Newer, Assert.Single(Install(Of(a))).Outcome);
            Assert.True(File.Exists(M("2024", "A", "stale.dll")));
            Assert.False(Directory.Exists(M("2024", "A.old")));
        }

        [Fact]
        public void Install_AfterAHalfRemove_DoesNotRestore()
        {
            var a = Cat("A");
            Installed("A", "2024", "1.0.0");
            Directory.Move(M("2024", "A"), M("2024", "A.old"));
            File.Delete(M("2024", "A.addin"));
            Assert.Equal(Outcome.Installed, Assert.Single(Install(Of(a))).Outcome);
            Assert.False(File.Exists(M("2024", "A", "stale.dll")));
            Assert.False(Directory.Exists(M("2024", "A.old")));
        }

        [Fact]
        public void Install_MissingFromThePayload_Fails()
        {
            var a = Cat("A");
            Directory.Delete(Path.Combine(_src, "A", "2024"), true);
            var r = Assert.Single(Install(Of(a)));
            Assert.Equal(Outcome.Failed, r.Outcome);
            Assert.Contains("not in the payload", r.Message);
            Assert.False(Directory.Exists(M("2024", "A")));
        }

        [Fact]
        public void Install_ReplacesTheOldInstall()
        {
            var a = Cat("A");
            OldInstall(a);
            Assert.Equal(Outcome.Updated, Assert.Single(Install(Of(a))).Outcome);
            Assert.False(Directory.Exists(M("2024", "Old")));
            Assert.False(Directory.Exists(M("2024", "Old.old")));
            Assert.False(File.Exists(M("2024", "Old.addin")));
            Assert.True(File.Exists(M("2024", "A", "Host.dll")));
            Assert.True(File.Exists(M("2024", "A.addin")));
        }

        [Fact]
        public void Install_OldInstallInUse_ChangesNothing()
        {
            var a = Cat("A");
            OldInstall(a);
            List<Result> results;
            using (new FileStream(M("2024", "Old", "x.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
                results = Install(Of(a));
            Assert.Equal(Outcome.SkippedInUse, Assert.Single(results).Outcome);
            Assert.True(File.Exists(M("2024", "Old", "x.dll")));
            Assert.True(File.Exists(M("2024", "Old.addin")));
            Assert.False(Directory.Exists(M("2024", "A")));
            Assert.False(Directory.Exists(M("2024", "A.new")));
        }

        [Fact]
        public void Remove_DeletesFolderAndManifest()
        {
            var a = Cat("A");
            Installed("A", "2024");
            Assert.Equal(Outcome.Removed, Assert.Single(Remove(Of(a))).Outcome);
            Assert.False(Directory.Exists(M("2024", "A")));
            Assert.False(File.Exists(M("2024", "A.addin")));
            Assert.False(Directory.Exists(M("2024", "A.old")));
        }

        [Fact]
        public void Remove_NothingThere_IsUnchanged()
        {
            var a = Cat("A");
            var results = Remove(Of(a));
            Assert.Equal(Outcome.Unchanged, Assert.Single(results).Outcome);
            Assert.False(Engine.Failed(results));
        }

        [Fact]
        public void Remove_WithALeftoverOld_Removes()
        {
            var a = Cat("A");
            Installed("A", "2024");
            Directory.CreateDirectory(M("2024", "A.old"));
            File.WriteAllText(M("2024", "A.old", "x.dll"), "x");
            Assert.Equal(Outcome.Removed, Assert.Single(Remove(Of(a))).Outcome);
            Assert.False(Directory.Exists(M("2024", "A")));
            Assert.False(Directory.Exists(M("2024", "A.old")));
            Assert.False(File.Exists(M("2024", "A.addin")));
        }

        [Fact]
        public void Install_KeyInOtherCase_Installs()
        {
            var a = Cat("A");
            var r = Assert.Single(Install(Of(a), keys: new[] { "a" }));
            Assert.Equal(Outcome.Installed, r.Outcome);
            Assert.Equal("A", r.AddIn);
        }

        [Fact]
        public void Remove_FileInUse_ChangesNothing()
        {
            var a = Cat("A");
            Installed("A", "2024");
            List<Result> results;
            using (new FileStream(M("2024", "A", "Lib.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
                results = Remove(Of(a));
            Assert.Equal(Outcome.SkippedInUse, Assert.Single(results).Outcome);
            foreach (var f in new[] { "Host.dll", "Lib.dll", "stale.dll" }) Assert.True(File.Exists(M("2024", "A", f)), f);
            Assert.True(File.Exists(M("2024", "A.addin")));
        }

        [Fact]
        public void Remove_All_AlsoRemovesOurAddInTheCatalogDoesNotKnow()
        {
            var a = Cat("A");
            Installed("Gone", "2024");
            Directory.CreateDirectory(M("2024", "Other"));
            var results = Remove(Of(a));
            Assert.Equal(Outcome.Removed, results.Single(r => r.AddIn == "Gone").Outcome);
            Assert.False(Directory.Exists(M("2024", "Gone")));
            Assert.False(File.Exists(M("2024", "Gone.addin")));
            Assert.True(Directory.Exists(M("2024", "Other")));
        }

        [Fact]
        public void Remove_All_LeavesAForeignAddInJson()
        {
            var a = Cat("A");
            Directory.CreateDirectory(M("2024", "Foreign"));
            File.WriteAllText(M("2024", "Foreign", "addin.json"), "{\"name\":\"x\"}");
            Directory.CreateDirectory(M("2024", "Foreign2"));
            File.WriteAllText(M("2024", "Foreign2", "addin.json"), "{\"key\":\"Foreign2\",\"manifest\":\"Foreign2.addin\"}");
            File.WriteAllText(M("2024", "Foreign2.addin"), "<RevitAddIns><AddIn><VendorId>Other</VendorId></AddIn></RevitAddIns>");
            var results = Remove(Of(a));
            Assert.True(Directory.Exists(M("2024", "Foreign")));
            Assert.True(Directory.Exists(M("2024", "Foreign2")));
            Assert.True(File.Exists(M("2024", "Foreign2.addin")));
            Assert.DoesNotContain(results, r => r.AddIn.StartsWith("Foreign"));
            Assert.Empty(Directory.GetDirectories(M("2024"), "*.old"));
        }

        [Fact]
        public void Remove_ByName_OnDiskOnly()
        {
            var a = Cat("A");
            Installed("Gone", "2024");
            var r = Assert.Single(Remove(Of(a), keys: new[] { "Gone" }));
            Assert.Equal(Outcome.Removed, r.Outcome);
            Assert.Equal("Gone", r.AddIn);
            Assert.Empty(Remove(Of(a), keys: new[] { "Nope" }));
        }

        [Fact]
        public void Remove_ByKey_LeavesOtherAddIns()
        {
            var a = Cat("A");
            var b = Cat("B");
            Installed("A", "2024");
            Installed("B", "2024");
            Assert.Equal(Outcome.Removed, Assert.Single(Remove(Of(a, b), keys: new[] { "A" })).Outcome);
            Assert.True(Directory.Exists(M("2024", "B")));
            Assert.True(File.Exists(M("2024", "B.addin")));
        }

        [Fact]
        public void Failed_IsFalseForInstalledUpdatedUnchangedNewerRemoved()
        {
            var results = new[] { Outcome.Installed, Outcome.Updated, Outcome.Unchanged, Outcome.Newer, Outcome.Removed }
                .Select(o => new Result { AddIn = "A", Year = "2024", Outcome = o });
            Assert.False(Engine.Failed(results));
        }
    }
}
