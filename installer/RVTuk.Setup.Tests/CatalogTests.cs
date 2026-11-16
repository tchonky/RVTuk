using System;
using System.IO;
using System.Text;
using Xunit;

namespace RVTuk.Setup.Tests
{
    public sealed class CatalogTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rvtuk-setup-tests-" + Guid.NewGuid().ToString("N"));

        public CatalogTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { Directory.Delete(_dir, true); }

        private string Write(string name, string text)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, text, new UTF8Encoding(true));
            return path;
        }

        // As ConvertTo-Json writes it: 2-space indent.
        private const string PowerShellCatalog =
@"{
  ""built"": ""2026-11-16T10:00:00Z"",
  ""commit"": ""35b2c47-dirty"",
  ""addIns"": [
    {
      ""key"": ""KKarea"",
      ""name"": ""KKarea"",
      ""manifest"": ""KKarea.addin"",
      ""entryClass"": ""KKarea.Revit.Application"",
      ""assembly"": ""KKarea.Revit.dll"",
      ""clientId"": ""9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC"",
      ""version"": ""1.0.0"",
      ""dev"": false,
      ""years"": [
        ""2023""
      ],
      ""replaces"": []
    },
    {
      ""key"": ""RVTuk"",
      ""name"": ""RVTuk"",
      ""manifest"": ""RVTuk.addin"",
      ""entryClass"": ""RVTuk.Revit.Application"",
      ""assembly"": ""RVTuk.Revit.dll"",
      ""clientId"": ""D71D7480-4A21-474E-A47E-3E8DF8C1BDA5"",
      ""version"": ""1.0.0"",
      ""dev"": true,
      ""years"": [
        ""2024"",
        ""2025"",
        ""2026"",
        ""2027""
      ],
      ""replaces"": [
        {
          ""manifest"": ""Old.addin"",
          ""folder"": ""Old""
        }
      ]
    }
  ]
}";

        private static string OneEntry(string key = "A", string manifest = "A.addin", string replacesFolder = "Old") =>
            "{\"key\":\"" + key + "\",\"name\":\"A\",\"manifest\":\"" + manifest + "\",\"entryClass\":\"A.App\",\"assembly\":\"A.dll\"," +
            "\"clientId\":\"x\",\"version\":\"1.0.0\",\"dev\":false,\"years\":[\"2024\"]," +
            "\"replaces\":[{\"manifest\":\"Old.addin\",\"folder\":\"" + replacesFolder + "\"}]}";

        private static string CatalogOf(params string[] entries) =>
            "{\"built\":\"b\",\"commit\":\"c\",\"addIns\":[" + string.Join(",", entries) + "]}";

        [Fact]
        public void Read_ReadsWhatPowerShellWrites()
        {
            var c = Catalog.Read(Write("catalog.json", PowerShellCatalog));
            Assert.Equal(2, c.AddIns.Count);
            Assert.Equal(new[] { "2023" }, c.Find("kkarea").Years);
            Assert.Equal("Old", c.Find("RVTuk").Replaces[0].Folder);
            Assert.True(c.Find("RVTuk").Dev);
            Assert.Equal("2026-11-16T10:00:00Z", c.Built);
            Assert.Equal("35b2c47-dirty", c.Commit);
        }

        [Fact]
        public void Read_WithoutReplaces_GivesAnEmptyList()
        {
            string entry = "{\"key\":\"A\",\"name\":\"A\",\"manifest\":\"A.addin\",\"entryClass\":\"A.App\",\"assembly\":\"A.dll\",\"years\":[\"2024\"]}";
            var c = Catalog.Read(Write("catalog.json", CatalogOf(entry)));
            Assert.NotNull(c.Find("A").Replaces);
            Assert.Empty(c.Find("A").Replaces);
        }

        public static TheoryData<string> Broken => new TheoryData<string>
        {
            CatalogOf(OneEntry(key: "..\\\\X")),
            CatalogOf(OneEntry(manifest: "sub\\\\A.addin")),
            CatalogOf(OneEntry(manifest: "A.txt")),
            CatalogOf(OneEntry(replacesFolder: "C:\\\\Windows")),
            CatalogOf(OneEntry(key: "A"), OneEntry(key: "a")),
        };

        [Theory]
        [MemberData(nameof(Broken))]
        public void Read_RejectsUnsafeOrBrokenCatalogs(string json)
        {
            Assert.StartsWith("catalog.json: ", Assert.Throws<InvalidDataException>(() => Catalog.Read(Write("catalog.json", json))).Message);
        }

        [Fact]
        public void Find_UnknownKey_IsNull()
        {
            Assert.Null(Catalog.Read(Write("catalog.json", PowerShellCatalog)).Find("Nope"));
        }

        [Fact]
        public void AddInInfo_TryRead_MissingFile_IsNull()
        {
            Assert.Null(AddInInfo.TryRead(_dir));
        }

        [Fact]
        public void AddInInfo_TryRead_Garbage_IsNull()
        {
            Write("addin.json", "not json");
            Assert.Null(AddInInfo.TryRead(_dir));
        }

        [Fact]
        public void AddInInfo_TryRead_ReadsAFile()
        {
            Write("addin.json", "{\"key\":\"RVTuk\",\"version\":\"1.2.0\",\"commit\":\"abc\",\"built\":\"2026-11-16T10:00:00Z\",\"manifest\":\"RVTuk.addin\",\"dev\":false}");
            var info = AddInInfo.TryRead(_dir);
            Assert.Equal("RVTuk", info.Key);
            Assert.Equal("1.2.0", info.Version);
            Assert.Equal("abc", info.Commit);
            Assert.Equal("2026-11-16T10:00:00Z", info.Built);
            Assert.Equal("RVTuk.addin", info.Manifest);
            Assert.False(info.Dev);
        }

        [Fact]
        public void TargetMachine_PutsEveryYearUnderTheFolder()
        {
            var m = new TargetMachine(_dir);
            Assert.Equal(Path.Combine(_dir, "2027"), m.YearFolder("2027"));
            Assert.True(m.IsInstalled("2027"));
            Assert.False(m.IsRunning("2027"));
        }

        [Fact]
        public void RealMachine_FollowsThePathRule()
        {
            var m = new RealMachine();
            Assert.Equal(@"C:\ProgramData\Autodesk\Revit\Addins\2026", m.YearFolder("2026"));
            Assert.Equal(@"C:\Program Files\Autodesk\Revit\Addins\2027", m.YearFolder("2027"));
        }

        [Fact]
        public void RealMachine_InstalledMatchesRevitExe()
        {
            var m = new RealMachine();
            for (int y = 2023; y <= 2027; y++)
                Assert.Equal(File.Exists($@"C:\Program Files\Autodesk\Revit {y}\Revit.exe"), m.IsInstalled(y.ToString()));
        }

        [Fact]
        public void TestsRunAs64Bit()
        {
            Assert.True(Environment.Is64BitProcess);
        }
    }
}
