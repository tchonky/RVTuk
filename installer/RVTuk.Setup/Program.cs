using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace RVTuk.Setup
{
    /// <summary>
    /// Self-contained installer for the office's Revit add-ins. Build-Installer.ps1 embeds
    /// payload.zip; this program hands it to <see cref="Engine"/>, which installs each add-in
    /// into the per-year Revit Addins folders (see <see cref="RealMachine.YearFolder"/>).
    ///
    /// Payload layout (payload.zip, or a folder given with --source):
    ///   catalog.json            the add-ins it holds, their years and versions
    ///   &lt;AddIn&gt;/&lt;year&gt;/...     each add-in's files for one Revit year, with its addin.json
    ///
    /// Flags: --install &lt;names|all&gt;  install these add-ins (the default: all)
    ///        --remove &lt;names|all&gt;   remove them instead;  --uninstall = --remove all
    ///        --years &lt;y,y&gt;         only these Revit years (default: the ones on this PC)
    ///        --source &lt;dir&gt;        use a payload folder instead of the embedded payload.zip
    ///        --target &lt;dir&gt;        install every year under that folder (testing, no elevation)
    ///        --quiet                print nothing (the log is still written)
    ///        --no-pause             don't wait for Enter at the end
    /// </summary>
    internal sealed class Options
    {
        public bool Remove;
        public List<string> Keys;
        public List<string> Years;
        public string Source, Target;
        public bool Quiet, NoPause;

        public static Options Parse(string[] args, out string error)
        {
            var o = new Options();
            bool install = false;
            string err = null;
            for (int i = 0; i < args.Length && err == null; i++)
            {
                string a = args[i];
                string Value()
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) return args[++i];
                    err = a + " needs a value";
                    return null;
                }
                switch (a)
                {
                    case "--install":
                    case "--remove":
                        if (a == "--install") install = true; else o.Remove = true;
                        string v = Value();
                        if (v == null) break;
                        o.Keys = v == "all" ? null : v.Split(',').ToList();
                        string bad = o.Keys?.FirstOrDefault(k => !Catalog.IsPlainName(k));
                        if (bad != null) err = $"'{bad}' is not an add-in name";
                        break;
                    case "--uninstall": o.Remove = true; o.Keys = null; break;
                    case "--years":
                        string y = Value();
                        if (y == null) break;
                        o.Years = y.Split(',').ToList();
                        string badYear = o.Years.FirstOrDefault(s => !Regex.IsMatch(s, @"^[0-9]{4}$"));
                        if (badYear != null) err = $"'{badYear}' is not a four-digit year";
                        break;
                    case "--source": o.Source = Value(); break;
                    case "--target": o.Target = Value(); break;
                    case "--quiet": o.Quiet = true; break;
                    case "--no-pause": o.NoPause = true; break;
                    default: err = $"unknown argument '{a}'"; break;
                }
            }
            if (err == null && install && o.Remove) err = "--install cannot be combined with --remove or --uninstall";
            error = err;
            return err == null ? o : null;
        }
    }

    internal static class Program
    {
        private const string Usage =
            "Usage: RVTukSetup [--install <names|all> | --remove <names|all> | --uninstall] [--years <y,y>]\r\n" +
            "                  [--source <dir>] [--target <dir>] [--quiet] [--no-pause]";

        private static int Main(string[] args)
        {
            var o = Options.Parse(args, out string error);
            if (o == null)
            {
                Console.WriteLine(error);
                Console.WriteLine(Usage);
                return 2;
            }

            // Installing into the machine-wide folders needs admin; relaunch elevated if we aren't.
            bool elevated = IsElevated();
            if (o.Target == null && !elevated)
                return RelaunchElevated(args);

            string log = Path.Combine(Environment.GetFolderPath(elevated
                ? Environment.SpecialFolder.CommonApplicationData
                : Environment.SpecialFolder.LocalApplicationData), "RVTuk", "setup.log");
            int exit = Run(o, Console.Out, log);

            if (!o.NoPause && !o.Quiet && !Console.IsOutputRedirected)
            {
                Console.WriteLine();
                Console.Write("  Press Enter to close...");
                Console.ReadLine();
            }
            return exit;
        }

        // 0 = done; 1 = something failed or was skipped, or there is no payload; 2 = --install names an unknown add-in.
        internal static int Run(Options o, TextWriter output, string logFile)
        {
            void Say(string line)
            {
                if (!o.Quiet) output.WriteLine(line);
                if (logFile == null) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFile));
                    File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
                }
                catch (Exception) { }
            }

            string temp = null;
            try
            {
                string payload = o.Source;
                Catalog catalog;
                try
                {
                    if (payload == null)
                    {
                        var asm = Assembly.GetExecutingAssembly();
                        string name = asm.GetManifestResourceNames()
                            .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
                        if (name == null)
                        {
                            Say("  FAILED: no embedded payload - this exe was built without Build-Installer.ps1.");
                            return 1;
                        }
                        payload = temp = Path.Combine(Path.GetTempPath(), "RVTukSetup-" + Process.GetCurrentProcess().Id);
                        using (var zip = new ZipArchive(asm.GetManifestResourceStream(name), ZipArchiveMode.Read))
                            foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
                            {
                                string dest = Path.GetFullPath(Path.Combine(temp, entry.FullName));
                                if (!dest.StartsWith(temp + Path.DirectorySeparatorChar)) continue; // never extract outside temp
                                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                                entry.ExtractToFile(dest, overwrite: true);
                            }
                    }
                    catalog = Catalog.Read(Path.Combine(payload, "catalog.json"));
                }
                catch (Exception e)
                {
                    Say("  FAILED: " + e.Message);
                    return 1;
                }

                Say("");
                Say("  " + new string('=', 56));
                Say("   RVTuk " + (o.Remove ? "uninstall" : "setup") + "  -  " + catalog.Commit + ", built " + catalog.Built);
                Say("  " + new string('=', 56));
                Say("");

                string unknown = o.Remove ? null : o.Keys?.FirstOrDefault(k => catalog.Find(k) == null);
                if (unknown != null)
                {
                    Say($"  Unknown add-in '{unknown}'. This package holds: {string.Join(", ", catalog.AddIns.Select(a => a.Key))}");
                    return 2;
                }

                IMachine machine = o.Target != null ? new TargetMachine(o.Target) : new RealMachine();
                var results = o.Remove
                    ? Engine.Remove(catalog, machine, o.Keys, o.Years, null)
                    : Engine.Install(catalog, payload, machine, o.Keys, o.Years, null);

                foreach (var r in results)
                    Say($"  Revit {r.Year}  {r.AddIn}  {Words(r)}");
                if (results.Count == 0)
                    Say("  Nothing to do: no selected add-in fits a Revit year on this PC.");
                bool failed = Engine.Failed(results);
                Say(failed ? "  NOT everything was done - see the lines marked SKIPPED or FAILED." : "  Done.");
                return failed ? 1 : 0;
            }
            finally
            {
                if (temp != null)
                    try { Directory.Delete(temp, true); } catch (Exception) { }
            }
        }

        private static string Words(Result r)
        {
            switch (r.Outcome)
            {
                case Outcome.Installed: return "installed";
                case Outcome.Updated: return "updated";
                case Outcome.Removed: return "removed";
                case Outcome.Unchanged: return "unchanged";
                case Outcome.Newer: return "newer version installed";
                case Outcome.SkippedRevitOpen: return $"SKIPPED: Revit {r.Year} is open";
                case Outcome.SkippedInUse: return "SKIPPED: " + r.Message;
                default: return "FAILED: " + r.Message;
            }
        }

        // --- environment ------------------------------------------------------

        private static bool IsElevated()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static int RelaunchElevated(string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Assembly.GetExecutingAssembly().Location,
                Arguments = string.Join(" ", args.Select(a => "\"" + a + "\"")),
                UseShellExecute = true,
                Verb = "runas",
            };
            try
            {
                using (var child = Process.Start(psi))
                {
                    child.WaitForExit();
                    return child.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception) // UAC prompt declined
            {
                Console.WriteLine("Administrator rights are required to install into the machine-wide Revit Addins folders.");
                return 1;
            }
        }
    }
}
