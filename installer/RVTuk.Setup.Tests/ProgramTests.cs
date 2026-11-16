using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace RVTuk.Setup.Tests
{
    public sealed class ProgramTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rvtuk-program-tests-" + Guid.NewGuid().ToString("N"));
        private readonly string _src, _t;

        public ProgramTests()
        {
            _src = Path.Combine(_dir, "src");
            _t = Path.Combine(_dir, "T");
            Directory.CreateDirectory(_src);
            File.WriteAllText(Path.Combine(_src, "catalog.json"),
                "{\"built\": \"2026-11-16T10:00:00Z\", \"commit\": \"abc\", \"addIns\": [{\"key\": \"A\", \"name\": \"A\", \"manifest\": \"A.addin\", " +
                "\"entryClass\": \"X.App\", \"assembly\": \"Host.dll\", \"clientId\": \"11111111-1111-1111-1111-111111111111\", " +
                "\"version\": \"1.0.0\", \"dev\": true, \"years\": [\"2023\", \"2024\"]}]}");
            foreach (var y in new[] { "2023", "2024" })
            {
                string p = Path.Combine(_src, "A", y);
                Directory.CreateDirectory(p);
                File.WriteAllText(Path.Combine(p, "Host.dll"), "x");
                File.WriteAllText(Path.Combine(p, "addin.json"),
                    "{\"key\": \"A\", \"version\": \"1.0.0\", \"commit\": \"abc\", \"built\": \"2026-11-16T10:00:00Z\", \"manifest\": \"A.addin\", \"dev\": true}");
            }
        }

        public void Dispose() { Directory.Delete(_dir, true); }

        private string T(params string[] parts) => Path.Combine(_t, Path.Combine(parts));

        private Options Opt(bool remove = false, List<string> keys = null, List<string> years = null, bool quiet = false) =>
            new Options { Remove = remove, Keys = keys, Years = years, Source = _src, Target = _t, Quiet = quiet };

        private static int Run(Options o, out string output, string logFile = null)
        {
            var w = new StringWriter();
            int exit = Program.Run(o, w, logFile);
            output = w.ToString();
            return exit;
        }

        [Fact]
        public void Parse_NoArguments_InstallsEverything()
        {
            var o = Options.Parse(new string[0], out var error);
            Assert.Null(error);
            Assert.False(o.Remove);
            Assert.Null(o.Keys);
            Assert.Null(o.Years);
        }

        [Fact]
        public void Parse_NamesAndYears()
        {
            var o = Options.Parse(new[] { "--install", "RVTuk,KKarea", "--years", "2024,2026", "--source", "S", "--target", "T", "--quiet", "--no-pause" }, out var error);
            Assert.Null(error);
            Assert.False(o.Remove);
            Assert.Equal(new[] { "RVTuk", "KKarea" }, o.Keys);
            Assert.Equal(new[] { "2024", "2026" }, o.Years);
            Assert.Equal("S", o.Source);
            Assert.Equal("T", o.Target);
            Assert.True(o.Quiet);
            Assert.True(o.NoPause);
        }

        [Fact]
        public void Parse_Uninstall_IsRemoveAll()
        {
            var o = Options.Parse(new[] { "--uninstall" }, out var error);
            Assert.Null(error);
            Assert.True(o.Remove);
            Assert.Null(o.Keys);
        }

        [Fact]
        public void Parse_All_IsItsOwnWord()
        {
            var o = Options.Parse(new[] { "--remove", "all" }, out var error);
            Assert.Null(error);
            Assert.True(o.Remove);
            Assert.Null(o.Keys);
        }

        [Theory]
        [InlineData("--all")]
        [InlineData("--preview")]
        [InlineData("--install", "A", "--remove", "A")]
        [InlineData("--years")]
        [InlineData("--years", "24")]
        [InlineData("--bogus")]
        [InlineData("--install", "--quiet")]
        public void Parse_Rejects(params string[] args)
        {
            Assert.Null(Options.Parse(args, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void Run_InstallsFromASourceFolderIntoATarget()
        {
            Assert.Equal(0, Run(Opt(), out var output));
            Assert.True(File.Exists(T("2023", "A", "Host.dll")));
            Assert.True(File.Exists(T("2024", "A.addin")));
            Assert.Contains("installed", output);
            Assert.Contains("Done.", output);
        }

        [Fact]
        public void Run_UnknownName_Returns2()
        {
            Assert.Equal(2, Run(Opt(keys: new List<string> { "Nope" }), out var output));
            Assert.Contains("Unknown add-in 'Nope'", output);
            Assert.Contains("A", output);
            Assert.False(Directory.Exists(_t));
        }

        [Fact]
        public void Run_Quiet_PrintsNothing()
        {
            Assert.Equal(0, Run(Opt(quiet: true), out var output));
            Assert.Equal("", output);
            Assert.True(File.Exists(T("2024", "A", "Host.dll")));
        }

        [Fact]
        public void Run_SkippedYear_Returns1()
        {
            Assert.Equal(0, Run(Opt(), out _));
            using (new FileStream(T("2024", "A", "Host.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(1, Run(Opt(), out var output));
                Assert.Contains("SKIPPED", output);
                Assert.Contains("NOT everything", output);
                Assert.Contains("Revit 2023  A  updated", output);
            }
        }

        [Fact]
        public void Run_RemoveThenNothingLeft()
        {
            Assert.Equal(0, Run(Opt(), out _));
            Assert.Equal(0, Run(Opt(remove: true), out _));
            foreach (var y in new[] { "2023", "2024" })
            {
                Assert.False(Directory.Exists(T(y, "A")));
                Assert.False(File.Exists(T(y, "A.addin")));
            }
        }

        [Fact]
        public void Run_NothingSelected_SaysSo()
        {
            Assert.Equal(0, Run(Opt(years: new List<string> { "2030" }), out var output));
            Assert.Contains("Nothing to do", output);
        }

        [Fact]
        public void Run_WritesTheLog_AndSurvivesAnUnwritableOne()
        {
            string log = Path.Combine(_dir, "log.txt");
            Assert.Equal(0, Run(Opt(quiet: true), out _, log));
            Assert.Contains("installed", File.ReadAllText(log));
            Assert.Equal(0, Run(Opt(quiet: true), out _, _src));
        }
    }
}
