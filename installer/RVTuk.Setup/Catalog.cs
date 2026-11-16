using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace RVTuk.Setup
{
    /// <summary>catalog.json: the add-ins a setup carries.</summary>
    [DataContract]
    internal sealed class Catalog
    {
        [DataMember(Name = "built")] public string Built;
        [DataMember(Name = "commit")] public string Commit;
        [DataMember(Name = "addIns")] public List<CatalogAddIn> AddIns;

        public static Catalog Read(string path)
        {
            Catalog c;
            try { c = Parse<Catalog>(path); }
            catch (SerializationException e) { throw new InvalidDataException($"{path}: {e.Message}", e); }
            if (c == null) throw new InvalidDataException($"{path}: empty");
            c.Validate();
            return c;
        }

        public CatalogAddIn Find(string key) =>
            AddIns.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));

        public static bool IsPlainName(string s) =>
            !string.IsNullOrEmpty(s) && s.IndexOfAny(new[] { '\\', '/', ':' }) < 0 && !s.Contains("..") && s != ".";

        private void Validate()
        {
            if (AddIns == null || AddIns.Count == 0) Fail("no add-ins");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in AddIns)
            {
                if (a == null) Fail("empty add-in entry");
                a.Years = a.Years ?? new List<string>();
                a.Replaces = a.Replaces ?? new List<OldInstall>();
                Plain(a.Key, "key");
                Plain(a.Manifest, "manifest");
                Plain(a.Assembly, "assembly");
                Manifest(a.Manifest);
                foreach (var r in a.Replaces)
                {
                    if (r == null) Fail($"{a.Key}: empty replaces entry");
                    Plain(r.Manifest, "replaces manifest");
                    Plain(r.Folder, "replaces folder");
                    Manifest(r.Manifest);
                }
                foreach (var y in a.Years)
                    if (y == null || !Regex.IsMatch(y, @"^[0-9]{4}$")) Fail($"{a.Key}: year '{y}' is not four digits");
                if (!keys.Add(a.Key)) Fail($"key '{a.Key}' occurs twice");
                if (a.Key.EndsWith(".new", StringComparison.OrdinalIgnoreCase) || a.Key.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                    Fail($"key '{a.Key}' ends in .new or .old");
            }
        }

        private static void Plain(string s, string what) { if (!IsPlainName(s)) Fail($"{what} '{s}' is not a plain name"); }
        private static void Manifest(string s) { if (!s.EndsWith(".addin", StringComparison.OrdinalIgnoreCase)) Fail($"manifest '{s}' does not end in .addin"); }
        private static void Fail(string reason) => throw new InvalidDataException("catalog.json: " + reason);

        internal static T Parse<T>(string path)
        {
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path))))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s);
        }
    }

    [DataContract]
    internal sealed class CatalogAddIn
    {
        [DataMember(Name = "key")] public string Key;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "manifest")] public string Manifest;
        [DataMember(Name = "entryClass")] public string EntryClass;
        [DataMember(Name = "assembly")] public string Assembly;
        [DataMember(Name = "clientId")] public string ClientId;
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "dev")] public bool Dev;
        [DataMember(Name = "years")] public List<string> Years;
        [DataMember(Name = "replaces")] public List<OldInstall> Replaces;
    }

    [DataContract]
    internal sealed class OldInstall
    {
        [DataMember(Name = "manifest")] public string Manifest;
        [DataMember(Name = "folder")] public string Folder;
    }

    /// <summary>addin.json inside an installed add-in folder.</summary>
    [DataContract]
    internal sealed class AddInInfo
    {
        [DataMember(Name = "key")] public string Key;
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "commit")] public string Commit;
        [DataMember(Name = "built")] public string Built;
        [DataMember(Name = "manifest")] public string Manifest;
        [DataMember(Name = "dev")] public bool Dev;

        public static AddInInfo TryRead(string folder)
        {
            try { return Catalog.Parse<AddInInfo>(Path.Combine(folder, "addin.json")); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SerializationException) { return null; }
        }
    }
}
