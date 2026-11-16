using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
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

        /// <summary>A file is trusted when its owner is the Administrators group, the system or the current user.</summary>
        internal static Func<string, bool> IsTrustedFile = path =>
        {
            try
            {
                var owner = File.GetAccessControl(path).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                using (var me = WindowsIdentity.GetCurrent())
                    return owner != null && (owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                        || owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner == me.User);
            }
            catch (Exception) { return false; }
        };

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
                Directory.CreateDirectory(yf);
                string target = Path.Combine(yf, a.Key), fresh = target + ".new", old = target + ".old";
                string manifest = Path.Combine(yf, a.Manifest), tmp = manifest + ".tmp";
                string link = Link(new[] { yf, target, fresh, old, manifest, tmp }.Concat(OldInstallPaths(yf, a.Replaces)).ToArray());
                if (link != null) return R(a.Key, year, Outcome.Failed, link + " is a link");
                if (!IsTrustedFile(yf)) return R(a.Key, year, Outcome.Failed, $"{yf} is not owned by an administrator");

                // 1
                if (m.IsRunning(year)) return R(a.Key, year, Outcome.SkippedRevitOpen, $"Revit {year} is open");

                // 2 Recover
                if (!Directory.Exists(target) && Directory.Exists(old) && File.Exists(manifest))
                {
                    try { Retry(() => Directory.Move(old, target)); }
                    catch (Exception e) { return R(a.Key, year, Outcome.Failed, $"could not restore {a.Key}.old: {e.Message}"); }
                }

                // 3 Decide
                var info = IsTrustedFile(Path.Combine(target, "addin.json")) ? AddInInfo.TryRead(target) : null;
                if (info != null && File.Exists(manifest) && IsTrustedFile(manifest) && !OldInstallsLeft(yf, a.Replaces) && !a.Dev && !info.Dev
                    && TryVersion(a.Version, out var want) && TryVersion(info.Version, out var have))
                {
                    int c = have.CompareTo(want);
                    if (c == 0) return R(a.Key, year, Outcome.Unchanged, $"{info.Version} is installed");
                    if (c > 0) return R(a.Key, year, Outcome.Newer, $"{info.Version} is installed, newer than {a.Version}");
                }

                // 4
                DeleteTree(fresh);
                DeleteTree(old);
                string src = Path.Combine(sourceDir, a.Key, year);
                if (!Directory.Exists(src)) return R(a.Key, year, Outcome.Failed, $"{a.Key} {year} is not in the payload");
                Directory.CreateDirectory(fresh);
                if ((File.GetAttributes(fresh) & FileAttributes.ReparsePoint) != 0 || !IsTrustedFile(fresh))
                    return R(a.Key, year, Outcome.Failed, $"{a.Key}.new is not ours");
                Copy(src, fresh);

                // 5 Probe
                foreach (var dir in new[] { target }.Concat(a.Replaces.Select(r => Path.Combine(yf, r.Folder))).Where(Directory.Exists))
                {
                    string busy = Probe(dir);
                    if (busy != null) { DeleteTree(fresh); return R(a.Key, year, Outcome.SkippedInUse, busy); }
                }

                // 6-8, 9 roll back by renames only
                bool existed = Directory.Exists(target), moved = false;
                try
                {
                    if (existed) Retry(() => Directory.Move(target, old));
                    Retry(() => Directory.Move(fresh, target));
                    moved = true;
                    if (Directory.Exists(manifest)) DeleteTree(manifest);
                    else if (File.Exists(manifest) && (File.GetAttributes(manifest) & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(manifest, File.GetAttributes(manifest) & ~FileAttributes.ReadOnly);
                    DeleteTree(tmp);
                    var bytes = new UTF8Encoding(false).GetBytes(ManifestXml(a));
                    using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))   // never follows a link
                        fs.Write(bytes, 0, bytes.Length);
                    if (File.Exists(manifest)) File.Replace(tmp, manifest, null); else File.Move(tmp, manifest);
                }
                catch (Exception e)
                {
                    string msg = e.Message;
                    TryDelete(tmp);
                    try
                    {
                        if (moved) Retry(() => Directory.Move(target, fresh));
                        if (Directory.Exists(old)) Retry(() => Directory.Move(old, target));
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
                string link = Link(new[] { yf, target, fresh, old, manifest, manifest + ".tmp" }.Concat(OldInstallPaths(yf, replaces)).ToArray());
                if (link != null) return R(key, year, Outcome.Failed, link + " is a link");
                if (Directory.Exists(yf) && !IsTrustedFile(yf)) return R(key, year, Outcome.Failed, $"{yf} is not owned by an administrator");

                if (m.IsRunning(year)) return R(key, year, Outcome.SkippedRevitOpen, $"Revit {year} is open");
                if (!Directory.Exists(target) && !Directory.Exists(old) && !File.Exists(manifest) && !OldInstallsLeft(yf, replaces))
                    return R(key, year, Outcome.Unchanged, "not installed");

                foreach (var dir in new[] { target }.Concat(replaces.Select(r => Path.Combine(yf, r.Folder))).Where(Directory.Exists))
                {
                    string busy = Probe(dir);
                    if (busy != null) return R(key, year, Outcome.SkippedInUse, busy);
                }

                DeleteTree(fresh);
                DeleteTree(old);
                if (Directory.Exists(target)) Retry(() => Directory.Move(target, old));
                DeleteTree(manifest);
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
                    DeleteTree(folderOld);
                    if (Directory.Exists(folder)) { Retry(() => Directory.Move(folder, folderOld)); removed = true; }
                    if (File.Exists(mf) || Directory.Exists(mf)) { DeleteTree(mf); removed = true; }
                    TryDelete(folderOld);
                }
                catch (Exception e) { throw new IOException($"the old install {r.Folder} could not be removed: {e.Message}", e); }
            }
            return removed;
        }

        private static IEnumerable<string> OldInstallPaths(string yf, List<OldInstall> replaces) =>
            replaces.SelectMany(r => new[] { Path.Combine(yf, r.Folder), Path.Combine(yf, r.Folder + ".old"), Path.Combine(yf, r.Manifest) });

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
                return IsTrustedFile(Path.Combine(yf, name, "addin.json")) && IsTrustedFile(mf) && File.ReadAllText(mf).Contains("<VendorId>KnafoKlimor</VendorId>") ? info : null;
            }
            catch (Exception) { return null; }
        }

        // Walks by hand: never opens or enters a link (a junction could lead outside the add-in folder).
        private static string Probe(string dir)
        {
            foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (e is DirectoryInfo)
                {
                    string busy = Probe(e.FullName);
                    if (busy != null) return busy;
                    continue;
                }
                try
                {
                    if ((e.Attributes & FileAttributes.ReadOnly) != 0) e.Attributes &= ~FileAttributes.ReadOnly; // the folder is about to be replaced
                    new FileStream(e.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose();
                }
                catch (Exception) { return $"{e.Name} is in use or cannot be written"; }
            }
            return null;
        }

        // The name of the first path that exists and is a link (junction, symbolic link), else null.
        private static string Link(params string[] paths)
        {
            foreach (var p in paths)
            {
                FileAttributes attr;
                try { attr = File.GetAttributes(p); } catch (Exception) { continue; }
                if ((attr & FileAttributes.ReparsePoint) != 0) return Path.GetFileName(p);
            }
            return null;
        }

        // Deletes a file or a folder tree, read-only entries included; never enters a link, the path itself
        // included (only the link is deleted).
        internal static void DeleteTree(string path)
        {
            FileAttributes attr;
            try { attr = File.GetAttributes(path); }
            catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException) { return; }
            bool dir = (attr & FileAttributes.Directory) != 0;
            if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);   // on a link: the link's own flag
            if ((attr & FileAttributes.ReparsePoint) != 0) { if (dir) Directory.Delete(path); else File.Delete(path); return; }
            if (!dir) { File.Delete(path); return; }
            foreach (var e in new DirectoryInfo(path).EnumerateFileSystemInfos()) DeleteTree(e.FullName);
            Directory.Delete(path);
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
            try { DeleteTree(path); }
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
