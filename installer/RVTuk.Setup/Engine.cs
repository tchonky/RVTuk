using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace RVTuk.Setup
{
    internal enum Outcome { Installed, Updated, Removed, Unchanged, Newer, SkippedRevitOpen, SkippedInUse, Failed }

    internal sealed class Result { public string AddIn; public string Year; public Outcome Outcome; public string Message; }

    /// <summary>Installs, updates and removes one add-in in one Revit year folder: probe, swap by rename, roll back.</summary>
    internal static class Engine
    {
        private const StringComparison Ic = StringComparison.OrdinalIgnoreCase;

        public static List<Result> Install(Catalog catalog, string sourceDir, IMachine machine,
                                           ICollection<string> keys, ICollection<string> years, Action<string> log)
        {
            var years2 = Candidates(catalog, machine, years);
            var results = new List<Result>();
            foreach (var a in Select(catalog, keys))
                foreach (var y in years2.Where(a.Years.Contains))
                    results.Add(Log(log, InstallOne(a, sourceDir, machine, y)));
            return results;
        }

        public static List<Result> Remove(Catalog catalog, IMachine machine,
                                          ICollection<string> keys, ICollection<string> years, Action<string> log)
        {
            var years2 = Candidates(catalog, machine, years);
            var results = new List<Result>();
            foreach (var a in Select(catalog, keys))
                foreach (var y in years2.Where(a.Years.Contains))
                    results.Add(Log(log, RemoveOne(a.Key, a.Manifest, a.Replaces, machine, y)));

            var unknown = keys?.Where(k => catalog.Find(k) == null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var y in years2)
            {
                string yf;
                List<string> names;
                try
                {
                    yf = machine.YearFolder(y);
                    names = unknown ?? Directory.GetDirectories(yf).Select(Path.GetFileName)
                        .Where(n => catalog.Find(n) == null && !n.EndsWith(".new", Ic) && !n.EndsWith(".old", Ic)).ToList();
                }
                catch (Exception) { continue; }
                foreach (var n in names)
                {
                    var info = Ours(yf, n);
                    if (info != null) results.Add(Log(log, RemoveOne(n, info.Manifest, new List<OldInstall>(), machine, y)));
                }
            }
            return results;
        }

        public static bool Failed(IEnumerable<Result> results) =>
            results.Any(r => r.Outcome is Outcome.SkippedRevitOpen or Outcome.SkippedInUse or Outcome.Failed);

        public static string ManifestXml(CatalogAddIn a) => string.Join("\r\n",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<RevitAddIns>",
            "  <AddIn Type=\"Application\">",
            $"    <Name>{a.Key}</Name>",
            $"    <Assembly>{a.Key}\\{a.Assembly}</Assembly>",
            $"    <FullClassName>{a.EntryClass}</FullClassName>",
            $"    <ClientId>{a.ClientId}</ClientId>",
            "    <VendorId>KnafoKlimor</VendorId>",
            "    <VendorDescription>Knafo Klimor Architects LTD</VendorDescription>",
            "  </AddIn>",
            "</RevitAddIns>",
            "");

        private static List<string> Candidates(Catalog c, IMachine m, ICollection<string> years)
        {
            var all = c.AddIns.SelectMany(a => a.Years).Distinct().OrderBy(y => y, StringComparer.Ordinal);
            return (years == null ? all.Where(m.IsInstalled) : years.Distinct().OrderBy(y => y, StringComparer.Ordinal)).ToList();
        }

        private static IEnumerable<CatalogAddIn> Select(Catalog c, ICollection<string> keys) =>
            keys == null ? c.AddIns : c.AddIns.Where(a => keys.Any(k => string.Equals(k, a.Key, Ic)));

        private static Result InstallOne(CatalogAddIn a, string sourceDir, IMachine m, string year)
        {
            string yf = null;
            try
            {
                yf = m.YearFolder(year);
                string target = Path.Combine(yf, a.Key), fresh = target + ".new", old = target + ".old";
                string manifest = Path.Combine(yf, a.Manifest), tmp = manifest + ".tmp";

                // 1
                if (m.IsRunning(year)) return R(a.Key, year, Outcome.SkippedRevitOpen, $"Revit {year} is open");

                // 2 Recover
                if (!Directory.Exists(target) && Directory.Exists(old) && File.Exists(manifest))
                {
                    try { Directory.Move(old, target); }
                    catch (Exception e) { return R(a.Key, year, Outcome.Failed, $"could not restore {a.Key}.old: {e.Message}"); }
                }

                // 3 Decide
                var info = AddInInfo.TryRead(target);
                if (info != null && File.Exists(manifest) && !OldInstallsLeft(yf, a.Replaces) && !a.Dev && !info.Dev
                    && TryVersion(a.Version, out var want) && TryVersion(info.Version, out var have))
                {
                    int c = have.CompareTo(want);
                    if (c == 0) return R(a.Key, year, Outcome.Unchanged, $"{info.Version} is installed");
                    if (c > 0) return R(a.Key, year, Outcome.Newer, $"{info.Version} is installed, newer than {a.Version}");
                }

                // 4
                if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
                if (Directory.Exists(old)) Directory.Delete(old, true);
                Directory.CreateDirectory(yf);
                string src = Path.Combine(sourceDir, a.Key, year);
                if (!Directory.Exists(src)) return R(a.Key, year, Outcome.Failed, $"{a.Key} {year} is not in the payload");
                Copy(src, fresh);

                // 5 Probe
                foreach (var dir in new[] { target }.Concat(a.Replaces.Select(r => Path.Combine(yf, r.Folder))).Where(Directory.Exists))
                {
                    string busy = Probe(dir);
                    if (busy != null) { Directory.Delete(fresh, true); return R(a.Key, year, Outcome.SkippedInUse, busy); }
                }

                // 6-8, 9 roll back by renames only
                bool existed = Directory.Exists(target), moved = false;
                try
                {
                    if (existed) Retry(() => Directory.Move(target, old));
                    Directory.Move(fresh, target);
                    moved = true;
                    File.WriteAllText(tmp, ManifestXml(a), new UTF8Encoding(false));
                    if (File.Exists(manifest)) File.Replace(tmp, manifest, null); else File.Move(tmp, manifest);
                }
                catch (Exception e)
                {
                    string msg = e.Message;
                    TryDelete(tmp);
                    try
                    {
                        if (moved) Directory.Move(target, fresh);
                        if (Directory.Exists(old)) Directory.Move(old, target);
                    }
                    catch (Exception) { msg += $"; the add-in is left at {a.Key}.old"; }
                    TryDelete(fresh);
                    return R(a.Key, year, Outcome.Failed, msg);
                }

                // 10
                bool removedOld;
                try { removedOld = RemoveOldInstalls(yf, a.Replaces); }
                catch (Exception e) { return R(a.Key, year, Outcome.Failed, "installed, but " + e.Message); }

                // 11, 12
                TryDelete(old);
                return existed || removedOld
                    ? R(a.Key, year, Outcome.Updated, $"updated to {a.Version}")
                    : R(a.Key, year, Outcome.Installed, $"installed {a.Version}");
            }
            catch (Exception e) { return Fail(a.Key, a.Manifest, yf, year, e); }
        }

        private static Result RemoveOne(string key, string manifestName, List<OldInstall> replaces, IMachine m, string year)
        {
            string yf = null;
            try
            {
                yf = m.YearFolder(year);
                string target = Path.Combine(yf, key), fresh = target + ".new", old = target + ".old";
                string manifest = Path.Combine(yf, manifestName);

                if (m.IsRunning(year)) return R(key, year, Outcome.SkippedRevitOpen, $"Revit {year} is open");
                if (!Directory.Exists(target) && !Directory.Exists(old) && !File.Exists(manifest) && !OldInstallsLeft(yf, replaces))
                    return R(key, year, Outcome.Unchanged, "not installed");

                foreach (var dir in new[] { target }.Concat(replaces.Select(r => Path.Combine(yf, r.Folder))).Where(Directory.Exists))
                {
                    string busy = Probe(dir);
                    if (busy != null) return R(key, year, Outcome.SkippedInUse, busy);
                }

                if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
                if (Directory.Exists(old)) Directory.Delete(old, true);
                if (Directory.Exists(target)) Retry(() => Directory.Move(target, old));
                if (File.Exists(manifest)) File.Delete(manifest);
                TryDelete(old);
                RemoveOldInstalls(yf, replaces);
                return R(key, year, Outcome.Removed, "removed");
            }
            catch (Exception e) { return Fail(key, manifestName, yf, year, e); }
        }

        // Step 10: true when something was removed.
        private static bool RemoveOldInstalls(string yf, List<OldInstall> replaces)
        {
            bool removed = false;
            foreach (var r in replaces)
            {
                string folder = Path.Combine(yf, r.Folder), folderOld = folder + ".old", mf = Path.Combine(yf, r.Manifest);
                try
                {
                    if (Directory.Exists(folderOld)) Directory.Delete(folderOld, true);
                    if (Directory.Exists(folder)) { Directory.Move(folder, folderOld); removed = true; }
                    if (File.Exists(mf)) { File.Delete(mf); removed = true; }
                    TryDelete(folderOld);
                }
                catch (Exception e) { throw new IOException($"the old install {r.Folder} could not be removed: {e.Message}", e); }
            }
            return removed;
        }

        private static bool OldInstallsLeft(string yf, List<OldInstall> replaces) =>
            replaces.Any(r => Directory.Exists(Path.Combine(yf, r.Folder)) || File.Exists(Path.Combine(yf, r.Manifest)));

        /// <summary>The addin.json of yf\name when that folder is provably ours, else null.</summary>
        private static AddInInfo Ours(string yf, string name)
        {
            try
            {
                if (!Catalog.IsPlainName(name)) return null;
                var info = AddInInfo.TryRead(Path.Combine(yf, name));
                if (info == null || !string.Equals(info.Key, name, Ic) || !Catalog.IsPlainName(info.Manifest) || !info.Manifest.EndsWith(".addin", Ic))
                    return null;
                string mf = Path.Combine(yf, info.Manifest);
                return File.Exists(mf) && File.ReadAllText(mf).Contains("<VendorId>KnafoKlimor</VendorId>") ? info : null;
            }
            catch (Exception) { return null; }
        }

        private static string Probe(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose(); }
                catch (Exception) { return $"{Path.GetFileName(f)} is in use or cannot be written"; }
            }
            return null;
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                string d = Path.Combine(to, Path.GetFileName(f));
                File.Copy(f, d);
                File.SetAttributes(d, File.GetAttributes(d) & ~FileAttributes.ReadOnly);
            }
            foreach (var d in Directory.GetDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // Antivirus scans fresh DLLs: retry a blocked rename every 250 ms for up to 5 s.
        private static void Retry(Action move)
        {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (true)
            {
                try { move(); return; }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && DateTime.UtcNow < until) { Thread.Sleep(250); }
            }
        }

        private static bool TryVersion(string s, out Version v)
        {
            if (!Version.TryParse(s, out v)) return false;
            v = new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
            return true;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception) { }
        }

        private static Result Fail(string key, string manifestName, string yf, string year, Exception e)
        {
            if (yf != null)
            {
                TryDelete(Path.Combine(yf, key + ".new"));
                TryDelete(Path.Combine(yf, manifestName + ".tmp"));
            }
            return R(key, year, Outcome.Failed, e.Message);
        }

        private static Result R(string key, string year, Outcome o, string msg) =>
            new Result { AddIn = key, Year = year, Outcome = o, Message = msg };

        private static Result Log(Action<string> log, Result r)
        {
            log?.Invoke($"{r.AddIn} {r.Year}: {r.Outcome} - {r.Message}");
            return r;
        }
    }
}
