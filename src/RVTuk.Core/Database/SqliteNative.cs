using System;

namespace RVTuk.Core.Database
{
    /// <summary>
    /// Ensures the native SQLite engine is loadable from inside a Revit add-in.
    /// </summary>
    /// <remarks>
    /// On .NET Framework (Revit 2023/2024) SQLitePCLRaw resolves <c>e_sqlite3.dll</c> by base name,
    /// which the OS searches relative to the host process (<c>Revit.exe</c>) — not our add-in folder.
    /// We pre-load it by full path so the later name-only P/Invoke binds to the already-loaded module.
    /// On .NET 8 (Revit 2025) the default resolver finds it next to the assembly, so this is a no-op.
    /// </remarks>
    internal static class SqliteNative
    {
        private static int _initialized;

        public static void EnsureLoaded()
        {
            if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0)
                return;
#if REVIT2024
            try
            {
                var dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (dir == null) return;
                var dll = System.IO.Path.Combine(dir, "e_sqlite3.dll");
                if (System.IO.File.Exists(dll))
                    LoadLibrary(dll);
            }
            catch { /* fall through — SQLitePCLRaw's own resolver may still succeed */ }
#endif
        }

#if REVIT2024
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string dllPath);
#endif
    }
}
