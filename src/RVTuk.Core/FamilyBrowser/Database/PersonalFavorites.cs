using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RVTuk.Core.FamilyBrowser.Database
{
    /// <summary>
    /// This user's favourite families, kept on their own PC (one library-relative path per
    /// line). Favourites used to live in the shared database, which users with view-only
    /// access to the library cannot write.
    /// </summary>
    public sealed class PersonalFavorites
    {
        private readonly string _path;
        private readonly HashSet<string> _set;

        public PersonalFavorites(string path)
        {
            _path = path;
            _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                        if (!string.IsNullOrWhiteSpace(line)) _set.Add(Normalize(line));
            }
            catch { /* unreadable: start empty */ }
        }

        public bool Contains(string relativePath) =>
            !string.IsNullOrEmpty(relativePath) && _set.Contains(Normalize(relativePath));

        /// <summary>Adds or removes a favourite and saves straight away. Returns false when
        /// the file could not be written (the change then lasts only for this session).</summary>
        public bool Set(string relativePath, bool isFavorite)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            var key = Normalize(relativePath);
            if (isFavorite) _set.Add(key); else _set.Remove(key);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllLines(_path, _set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
                return true;
            }
            catch { return false; }
        }

        private static string Normalize(string p) => p.Trim().Replace('/', '\\');
    }
}
