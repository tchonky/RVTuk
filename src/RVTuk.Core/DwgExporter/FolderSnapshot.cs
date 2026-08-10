using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>One file as seen at a moment in time.</summary>
    public class FileStamp
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public DateTime ModifiedUtc { get; set; }
    }

    /// <summary>
    /// Works out what an export produced by comparing the output folder before and after it
    /// ran. We have no DWG reader, so a drawing can't be asked what it references — but every
    /// file Revit writes shows up here, including the raster images and the xref'd view
    /// drawings whose names Revit invents when a setup has MergedViews off. Pure: the caller
    /// enumerates the directory and hands in both lists.
    /// </summary>
    public static class FolderSnapshot
    {
        /// <summary>Reads a folder's files into stamps. The one impure member here — <see
        /// cref="Diff"/> below is the part worth testing, and takes its lists from the caller.
        /// A missing folder is an empty snapshot, not an error: it just means nothing was
        /// there before the export created it.</summary>
        public static IReadOnlyList<FileStamp> Take(string folder)
        {
            if (!System.IO.Directory.Exists(folder)) return new List<FileStamp>();

            return System.IO.Directory.EnumerateFiles(folder)
                .Select(path =>
                {
                    var info = new System.IO.FileInfo(path);
                    return new FileStamp
                    {
                        Path = path,
                        Size = info.Length,
                        ModifiedUtc = info.LastWriteTimeUtc,
                    };
                })
                .ToList();
        }

        /// <summary>Paths present in <paramref name="after"/> that are new, or whose size or
        /// timestamp moved. A file that disappeared is not reported — nothing was produced.</summary>
        public static IReadOnlyList<string> Diff(
            IReadOnlyList<FileStamp> before, IReadOnlyList<FileStamp> after)
        {
            var previous = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
            foreach (var stamp in before) previous[stamp.Path] = stamp;

            return after
                .Where(stamp => !previous.TryGetValue(stamp.Path, out var was)
                                || was.Size != stamp.Size
                                || was.ModifiedUtc != stamp.ModifiedUtc)
                .Select(stamp => stamp.Path)
                .ToList();
        }
    }
}
