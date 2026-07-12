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
    /// Self-contained installer for the RVTuk / KKarea Revit add-ins.
    /// Build-Installer.ps1 embeds payload.zip (per-year add-in DLLs + .addin manifests);
    /// this program extracts it into the per-year Revit Addins folders — the same layout
    /// Deploy.ps1 produces on a dev machine.
    ///
    /// Zip layout, one top-level folder per Revit year:
    ///   2023/KKarea.addin   2023/KKarea/...dlls...
    ///   2024/RVTuk.addin    2024/RVTuk/...dlls...
    ///   2025/RVTuk.addin    2025/RVTuk/...dlls...
    ///   version.txt         (source commit + build date, shown in the banner)
    ///
    /// Flags: --uninstall  remove the add-in instead of installing
    ///        --all        don't skip years whose Addins folder is missing
    ///        --target X   install under folder X instead of ProgramData (testing)
    ///        --no-pause   don't wait for Enter at the end
    /// </summary>
    internal static class Program
    {
        private const string DefaultAddinsBase = @"C:\ProgramData\Autodesk\Revit\Addins";

        private static int Main(string[] args)
        {
            bool uninstall = args.Contains("--uninstall");
            bool allYears = args.Contains("--all");
            bool noPause = args.Contains("--no-pause");
            string target = GetOption(args, "--target");
            bool customTarget = target != null;
            string addinsBase = target ?? DefaultAddinsBase;

            // Installing under ProgramData needs admin; relaunch elevated if we aren't.
            if (!customTarget && !IsElevated())
                return RelaunchElevated(args);

            int exit = 1;
            try
            {
                exit = Run(uninstall, allYears, customTarget, addinsBase);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("  FAILED: " + ex.Message);
            }

            if (!noPause && !Console.IsOutputRedirected)
            {
                Console.WriteLine();
                Console.Write("  Press Enter to close...");
                Console.ReadLine();
            }
            return exit;
        }

        private static int Run(bool uninstall, bool allYears, bool customTarget, string addinsBase)
        {
            using (var zip = OpenPayload())
            {
                Banner(zip, uninstall);

                var years = PayloadYears(zip);
                if (years.Count == 0)
                {
                    Console.WriteLine("  FAILED: payload.zip contains no Revit-year folders.");
                    return 1;
                }

                var running = customTarget ? new HashSet<string>() : GetRunningRevitYears();
                var results = new Dictionary<string, string>();

                foreach (var year in years)
                {
                    string addinName = ManifestName(zip, year);
                    Console.WriteLine($"  Revit {year}  [{addinName}]");

                    if (!allYears && !Directory.Exists(Path.Combine(addinsBase, year)))
                    {
                        Console.WriteLine($"    SKIP  Revit {year} not found on this machine.");
                        results[year] = "skipped (not installed)";
                        continue;
                    }
                    if (running.Contains(year))
                    {
                        Console.WriteLine($"    SKIP  Revit {year} is open (files are locked). Close it and re-run.");
                        results[year] = "skipped (Revit open)";
                        continue;
                    }

                    try
                    {
                        if (uninstall)
                        {
                            Remove(addinsBase, year, addinName);
                            Console.WriteLine($"    OK    removed from {Path.Combine(addinsBase, year)}");
                            results[year] = "removed";
                        }
                        else
                        {
                            int files = Install(zip, addinsBase, year, addinName);
                            Console.WriteLine($"    OK    {files} files -> {Path.Combine(addinsBase, year, addinName)}");
                            results[year] = "installed";
                        }
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"    SKIP  files are locked (Revit {year} likely open): {ex.Message}");
                        results[year] = "skipped (locked)";
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    FAIL  {ex.Message}");
                        results[year] = "FAILED";
                    }
                }

                Summary(results, uninstall);
                return results.Values.Contains("FAILED") ? 1 : 0;
            }
        }

        // --- install / uninstall --------------------------------------------

        private static int Install(ZipArchive zip, string addinsBase, string year, string addinName)
        {
            string yearDir = Path.Combine(addinsBase, year);
            string dllDir = Path.Combine(yearDir, addinName);

            // Wipe the whole folder so stale DLLs from older versions can't survive.
            if (Directory.Exists(dllDir))
                Directory.Delete(dllDir, recursive: true);
            Directory.CreateDirectory(dllDir);

            int files = 0;
            foreach (var entry in zip.Entries)
            {
                var parts = Normalize(entry.FullName).Split('/');
                if (parts.Length < 2 || parts[0] != year ||
                    entry.Name.Length == 0 ||          // directory entry
                    parts.Contains(".."))              // never extract outside the target
                    continue;

                string dest = Path.Combine(yearDir, Path.Combine(parts.Skip(1).ToArray()));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                entry.ExtractToFile(dest, overwrite: true);
                files++;
            }
            return files;
        }

        private static void Remove(string addinsBase, string year, string addinName)
        {
            string dllDir = Path.Combine(addinsBase, year, addinName);
            string manifest = Path.Combine(addinsBase, year, addinName + ".addin");
            if (Directory.Exists(dllDir))
                Directory.Delete(dllDir, recursive: true);
            if (File.Exists(manifest))
                File.Delete(manifest);
        }

        // --- payload ---------------------------------------------------------

        private static ZipArchive OpenPayload()
        {
            var asm = Assembly.GetExecutingAssembly();
            string name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
            if (name == null)
                throw new InvalidOperationException(
                    "no embedded payload — this exe was built without Build-Installer.ps1.");
            return new ZipArchive(asm.GetManifestResourceStream(name), ZipArchiveMode.Read);
        }

        private static List<string> PayloadYears(ZipArchive zip) =>
            zip.Entries
                .Select(e => Normalize(e.FullName).Split('/')[0])
                .Where(top => Regex.IsMatch(top, @"^\d{4}$"))
                .Distinct()
                .OrderBy(y => y)
                .ToList();

        /// <summary>Add-in name for a year, from its "{year}/{name}.addin" manifest entry.</summary>
        private static string ManifestName(ZipArchive zip, string year)
        {
            var entry = zip.Entries.FirstOrDefault(e =>
            {
                var parts = Normalize(e.FullName).Split('/');
                return parts.Length == 2 && parts[0] == year &&
                       parts[1].EndsWith(".addin", StringComparison.OrdinalIgnoreCase);
            });
            if (entry == null)
                throw new InvalidOperationException($"payload has no .addin manifest for {year}.");
            return Path.GetFileNameWithoutExtension(entry.Name);
        }

        private static string Normalize(string entryName) => entryName.Replace('\\', '/');

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
                Console.WriteLine("Administrator rights are required to install into " + DefaultAddinsBase);
                return 1;
            }
        }

        /// <summary>Map running Revit.exe processes to their year via the install path.</summary>
        private static HashSet<string> GetRunningRevitYears()
        {
            var years = new HashSet<string>();
            foreach (var p in Process.GetProcessesByName("Revit"))
            {
                try
                {
                    var m = Regex.Match(p.MainModule.FileName, @"Revit\s+(\d{4})");
                    if (m.Success) years.Add(m.Groups[1].Value);
                }
                catch { } // access denied reading the path — ignore
                finally { p.Dispose(); }
            }
            return years;
        }

        // --- output -----------------------------------------------------------

        private static void Banner(ZipArchive zip, bool uninstall)
        {
            string version = "";
            var v = zip.GetEntry("version.txt");
            if (v != null)
                using (var r = new StreamReader(v.Open()))
                    version = r.ReadToEnd().Trim();

            Console.WriteLine();
            Console.WriteLine("  " + new string('=', 56));
            Console.WriteLine("   RVTuk " + (uninstall ? "uninstall" : "setup") +
                              (version.Length > 0 ? "  -  " + version : ""));
            Console.WriteLine("  " + new string('=', 56));
            Console.WriteLine();
        }

        private static void Summary(Dictionary<string, string> results, bool uninstall)
        {
            Console.WriteLine();
            Console.WriteLine("  " + new string('-', 54));
            Console.WriteLine("  Summary");
            foreach (var kv in results)
                Console.WriteLine($"    Revit {kv.Key}  : {kv.Value}");
            if (!uninstall && results.Values.Contains("installed"))
            {
                Console.WriteLine();
                Console.WriteLine("  Done. Start (or restart) Revit to load the add-in.");
            }
        }

        private static string GetOption(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
