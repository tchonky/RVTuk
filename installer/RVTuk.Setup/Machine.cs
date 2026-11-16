using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RVTuk.Setup
{
    /// <summary>The machine the install engine works on.</summary>
    internal interface IMachine
    {
        /// <summary>The folder that holds &lt;Key&gt;\ and &lt;Key&gt;.addin for that year.</summary>
        string YearFolder(string year);
        bool IsInstalled(string year);
        bool IsRunning(string year);
    }

    /// <summary>This PC: the machine-wide Addins folders, the registry, the running processes. Reads only.</summary>
    internal sealed class RealMachine : IMachine
    {
        // ProgramData up to 2026, Program Files from 2027 (the only copy of this rule).
        public string YearFolder(string year) =>
            Path.Combine(int.Parse(year) >= 2027
                ? @"C:\Program Files\Autodesk\Revit\Addins"
                : @"C:\ProgramData\Autodesk\Revit\Addins", year);

        public bool IsInstalled(string year)
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var revit = hklm.OpenSubKey($@"SOFTWARE\Autodesk\Revit\{year}"))
            {
                if (revit != null)
                    foreach (var name in revit.GetSubKeyNames())
                        using (var sub = revit.OpenSubKey(name))
                            if (sub?.GetValue("InstallationLocation") is string dir && File.Exists(Path.Combine(dir, "Revit.exe")))
                                return true;
            }
            return File.Exists($@"C:\Program Files\Autodesk\Revit {year}\Revit.exe");
        }

        public bool IsRunning(string year)
        {
            foreach (var p in Process.GetProcessesByName("Revit"))
                using (p)
                {
                    int major;
                    try { major = p.MainModule.FileVersionInfo.FileMajorPart; }
                    catch (Exception) { return true; } // an unreadable Revit counts as every year: skipping is the safe side
                    if (major + 2000 == int.Parse(year)) return true;
                }
            return false;
        }
    }

    /// <summary>--target: every year lives under one test folder, always installed, never running.</summary>
    internal sealed class TargetMachine : IMachine
    {
        private readonly string _dir;
        public TargetMachine(string dir) { _dir = dir; }

        public string YearFolder(string year) => Path.Combine(_dir, year);
        public bool IsInstalled(string year) => true;
        public bool IsRunning(string year) => false;
    }
}
