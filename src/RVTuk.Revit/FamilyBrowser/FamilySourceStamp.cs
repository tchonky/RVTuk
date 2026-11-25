using System;
using System.Globalization;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RVTuk.Revit.FamilyBrowser
{
    /// <summary>
    /// The library file a loaded family came from, noted on the <b>Family</b> element: the file's
    /// size and last-write time (UTC) at load time. A loaded family keeps no file facts of its own,
    /// so this is what lets ⟳ tell that the library file changed since (Core's FamilySourceCheck).
    /// Strings only, so the schema stays readable in every Revit year.
    /// </summary>
    public static class FamilySourceStamp
    {
        private static readonly Guid SchemaGuid = new Guid("25ad5669-d8cb-485f-b965-f2d788c7b356");
        private const string SchemaName = "RVTukFamilySource";
        private const string SizeField = "FileSize";
        private const string ModifiedField = "ModifiedUtc";
        private const string PathField = "LibraryPath";

        /// <summary>Notes <paramref name="file"/> on the family. Caller must be inside a transaction.</summary>
        public static void Write(Family family, FileInfo file, string libraryRelativePath)
        {
            var entity = new Entity(GetOrCreateSchema());
            entity.Set(SizeField, file.Length.ToString(CultureInfo.InvariantCulture));
            entity.Set(ModifiedField, file.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture));
            entity.Set(PathField, libraryRelativePath);
            family.SetEntity(entity);
        }

        /// <summary>Forgets the source (the family now comes from elsewhere). Caller must be inside a
        /// transaction. False when there was nothing to forget.</summary>
        public static bool Clear(Family family)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null || !family.GetEntity(schema).IsValid()) return false;
            family.DeleteEntity(schema);
            return true;
        }

        public static bool TryRead(Family family, out long size, out DateTime modifiedUtc)
        {
            size = 0;
            modifiedUtc = default;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return false;
                var entity = family.GetEntity(schema);
                if (!entity.IsValid()) return false;
                return long.TryParse(entity.Get<string>(SizeField), NumberStyles.Integer, CultureInfo.InvariantCulture, out size)
                    && DateTime.TryParse(entity.Get<string>(ModifiedField), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out modifiedUtc);
            }
            catch { return false; }
        }

        /// <summary>The path relative to <paramref name="libraryRoot"/> when <paramref name="path"/>
        /// is a library family: inside the root, outside .DB and .Requests. Null otherwise.</summary>
        public static string? LibraryRelativePath(string? path, string? libraryRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(libraryRoot)) return null;
            try
            {
                var root = Path.GetFullPath(libraryRoot!).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                var full = Path.GetFullPath(path!);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                var rel = full.Substring(root.Length);
                var first = rel.Split('\\', '/')[0];
                if (string.Equals(first, Core.Shared.Config.AppConfig.DbFolderName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(first, Core.FamilyBrowser.Requests.RequestStore.FolderName, StringComparison.OrdinalIgnoreCase))
                    return null;
                return rel;
            }
            catch { return null; }
        }

        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId("KnafoKlimor");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(SizeField, typeof(string));
            builder.AddSimpleField(ModifiedField, typeof(string));
            builder.AddSimpleField(PathField, typeof(string));
            return builder.Finish();
        }
    }
}
