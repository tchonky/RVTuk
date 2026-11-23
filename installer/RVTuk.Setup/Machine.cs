using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
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

        /// <summary>Trusts the signing certificate for code on this machine; false when it was
        /// trusted already.</summary>
        bool TrustPublisher(byte[] certificate);
    }

    /// <summary>This PC: the machine-wide Addins folders, the registry, the running processes. Reads
    /// only, except <see cref="TrustPublisher"/>, which writes the machine's certificate stores.</summary>
    internal sealed class RealMachine : IMachine
    {
        // ProgramData up to 2026, Program Files from 2027 (the only copy of this rule).
        public string YearFolder(string year) =>
            Path.Combine(int.Parse(year) >= 2027
                ? @"C:\Program Files\Autodesk\Revit\Addins"
                : @"C:\ProgramData\Autodesk\Revit\Addins", year);

        public bool IsInstalled(string year)
        {
            try
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
            }
            catch (Exception) { } // a bad registry entry: fall back to the default folder
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

        /// <summary>
        /// Adds the certificate to the machine's Trusted Root (a self-signed certificate is its own
        /// root, so its signature only checks out from there) and Trusted Publishers (where Revit
        /// looks before asking "Always Load"). Machine-wide: every user, every Revit year. Needs
        /// the elevation the install already has.
        /// </summary>
        public bool TrustPublisher(byte[] certificate)
        {
            bool added = false;
            using (var cert = new X509Certificate2(certificate))
                foreach (var name in new[] { StoreName.Root, StoreName.TrustedPublisher })
                    using (var store = new X509Store(name, StoreLocation.LocalMachine))
                    {
                        store.Open(OpenFlags.ReadWrite);
                        if (store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count > 0) continue;
                        store.Add(cert);
                        added = true;
                    }
            return added;
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

        /// <summary>A test install changes no certificate store.</summary>
        public bool TrustPublisher(byte[] certificate) => false;
    }
}
