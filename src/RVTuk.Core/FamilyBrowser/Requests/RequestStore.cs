using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#if NETFRAMEWORK
using System.Runtime.Serialization.Json;
#else
using System.Text.Json;
#endif

namespace RVTuk.Core.FamilyBrowser.Requests
{
    /// <summary>
    /// The library's <c>.Requests</c> folder. Everyone gets "View + Upload" on it in Autodesk
    /// Docs, which lets them add files but not change or delete them, so every action here only
    /// ever creates new files:
    /// <list type="bullet">
    /// <item>a request is a new <c>&lt;id&gt;.json</c> (plus <c>&lt;id&gt;.rfa</c> when the
    /// user attached their project's version);</item>
    /// <item>withdrawing one adds an empty <c>&lt;id&gt;.withdrawn</c> marker;</item>
    /// <item>only the maintainer, who can edit the folder, moves files into <c>Done\</c>.</item>
    /// </list>
    /// The folder starts with a dot so the library scan skips it (FamilyIndexer).
    /// </summary>
    public sealed class RequestStore
    {
        public const string FolderName = ".Requests";
        public const string DoneFolderName = "Done";
        public const string WithdrawnExtension = ".withdrawn";

        public const string MissingFolderMessage =
            "The library has no .Requests folder yet. The library maintainer's Family Browser creates it: " +
            "ask them to open it once, then let everyone add files to that folder in Autodesk Docs.";

        public RequestStore(string libraryRoot)
        {
            Folder = Path.Combine(libraryRoot, FolderName);
        }

        public string Folder { get; }
        public string DoneFolder => Path.Combine(Folder, DoneFolderName);

        /// <summary>Open requests, oldest first. A missing folder or an unreadable file is
        /// skipped, never thrown: requests must not stop the browser from opening.</summary>
        public IReadOnlyList<FamilyRequest> LoadOpen()
        {
            var result = new List<FamilyRequest>();
            if (!Directory.Exists(Folder)) return result;
            string[] files;
            try { files = Directory.GetFiles(Folder, "*.json"); }
            catch { return result; }
            foreach (var file in files)
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (File.Exists(Path.Combine(Folder, id + WithdrawnExtension))) continue;
                var request = TryRead(file);
                if (request == null) continue;
                request.Id = id;
                result.Add(request);
            }
            return result.OrderBy(r => r.CreatedUtc, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        }

        public int CountDone()
        {
            try { return Directory.Exists(DoneFolder) ? Directory.GetFiles(DoneFolder, "*.json").Length : 0; }
            catch { return 0; }
        }

        /// <summary>A new file name: sortable by time, with the user and a random part so two
        /// requests in the same second never collide.</summary>
        public static string NewId(string user, DateTime utcNow)
        {
            var safeUser = new string((user ?? string.Empty).Where(char.IsLetterOrDigit).Take(20).ToArray());
            if (safeUser.Length == 0) safeUser = "user";
            return utcNow.ToString("yyyyMMdd-HHmmss") + "-" + safeUser + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        public string AttachmentPath(string id) => Path.Combine(Folder, id + ".rfa");

        public bool FolderExists => Directory.Exists(Folder);

        /// <summary>For the maintainer only. A view-only user could create the folder on their
        /// own PC, but Desktop Connector can't upload a new folder where they may not edit, so
        /// their requests would never arrive; the maintainer's browser makes it instead.</summary>
        public void EnsureFolder() => Directory.CreateDirectory(Folder);

        /// <summary>Writes the request as a new file. Never creates the folder (see
        /// <see cref="EnsureFolder"/>). Throws when the folder is missing or can't be written,
        /// so the caller can tell the user the request was not sent.</summary>
        public void Save(FamilyRequest request)
        {
            if (string.IsNullOrEmpty(request.Id)) throw new ArgumentException("The request has no id.", nameof(request));
            if (!FolderExists) throw new DirectoryNotFoundException(MissingFolderMessage);
            var bytes = Encoding.UTF8.GetBytes(Serialize(request));
            using var stream = new FileStream(Path.Combine(Folder, request.Id + ".json"), FileMode.CreateNew, FileAccess.Write);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Hides a request by adding a marker file (users can't delete files).</summary>
        public void Withdraw(FamilyRequest request)
        {
            using (new FileStream(Path.Combine(Folder, request.Id + WithdrawnExtension), FileMode.OpenOrCreate, FileAccess.Write)) { }
        }

        /// <summary>The maintainer's Done: the request and its files move to <c>Done\</c>, kept as history.</summary>
        public void MarkDone(FamilyRequest request)
        {
            Directory.CreateDirectory(DoneFolder);
            foreach (var name in FilesOf(request.Id))
            {
                var from = Path.Combine(Folder, name);
                if (!File.Exists(from)) continue;
                var to = Path.Combine(DoneFolder, name);
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
        }

        /// <summary>
        /// The maintainer approves a suggested family: its attached file moves to
        /// <paramref name="targetPath"/> in the library, then the request goes to <c>Done\</c>.
        /// The target's file name is the family's own name, which Revit uses as the family name
        /// on loading (the attachment is named after the request id).
        /// </summary>
        /// <exception cref="FileNotFoundException">The attachment hasn't arrived (Desktop
        /// Connector may still be downloading it).</exception>
        /// <exception cref="IOException">The target exists and <paramref name="overwrite"/> is false.</exception>
        public void ApproveSuggestion(FamilyRequest request, string targetPath, bool overwrite)
        {
            var source = string.IsNullOrEmpty(request.Attachment)
                ? AttachmentPath(request.Id)
                : Path.Combine(Folder, request.Attachment);
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    "The suggested family's file is not on this PC yet. Desktop Connector may still be downloading it: try again in a minute.",
                    source);
            if (File.Exists(targetPath))
            {
                if (!overwrite) throw new IOException(targetPath + " already exists.");
                File.Delete(targetPath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Move(source, targetPath);
            MarkDone(request);
        }

        /// <summary>For the maintainer: moves withdrawn requests to <c>Done\</c> so the folder
        /// stays short. Best-effort; returns how many were moved.</summary>
        public int TidyWithdrawn()
        {
            int moved = 0;
            if (!Directory.Exists(Folder)) return 0;
            try
            {
                foreach (var marker in Directory.GetFiles(Folder, "*" + WithdrawnExtension))
                {
                    try { MarkDone(new FamilyRequest { Id = Path.GetFileNameWithoutExtension(marker) }); moved++; }
                    catch { /* locked or not synced yet: try again next time */ }
                }
            }
            catch { }
            return moved;
        }

        private static IEnumerable<string> FilesOf(string id) =>
            new[] { id + ".json", id + ".rfa", id + WithdrawnExtension };

        private static FamilyRequest? TryRead(string file)
        {
            try
            {
                var json = File.ReadAllText(file, Encoding.UTF8);
#if NETFRAMEWORK
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
                return (FamilyRequest?)new DataContractJsonSerializer(typeof(FamilyRequest)).ReadObject(ms);
#else
                return JsonSerializer.Deserialize<FamilyRequest>(json);
#endif
            }
            catch { return null; }
        }

        internal static string Serialize(FamilyRequest request)
        {
#if NETFRAMEWORK
            using var ms = new MemoryStream();
            new DataContractJsonSerializer(typeof(FamilyRequest)).WriteObject(ms, request);
            return Encoding.UTF8.GetString(ms.ToArray());
#else
            return JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
#endif
        }
    }
}
