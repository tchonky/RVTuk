using System;
using RVTuk.Core.FamilyBrowser.Models;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// Is the copy loaded in the project the library's current file? A loaded family keeps no
    /// file size or date of its own, so RVTuk notes the library file's size and last-write time
    /// on the family when it is loaded (the "load stamp"), and compares that with the library
    /// file on ⟳:
    /// <list type="number">
    /// <item>a library <c>_Version</c> ahead of the project's means an update, whatever else;</item>
    /// <item>with a load stamp, the file decides: same size and time is up to date, anything
    ///   else means the library file changed since it was loaded — an update even when
    ///   <c>_Version</c> was not bumped, or is missing;</item>
    /// <item>without one, matching <c>_Version</c>s still say up to date (the office's own check);</item>
    /// <item>otherwise nothing can tell: <see cref="VersionStatus.Unknown"/>, never a false ✓.</item>
    /// </list>
    /// </summary>
    public static class FamilySourceCheck
    {
        /// <summary>File times can come back rounded (cloud sync, copies between disks).</summary>
        public static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

        public static VersionStatus Judge(string? libraryVersion, ProjectFamilyInfo loaded,
            long? libraryFileSize, DateTime? libraryFileModifiedUtc)
        {
            if (FamilyVersionCheck.IsUpdateAvailable(libraryVersion, loaded.Version))
                return VersionStatus.UpdateAvailable;

            if (loaded.HasLoadStamp && libraryFileSize != null && libraryFileModifiedUtc != null)
                return SameFile(loaded.LoadedFileSize!.Value, loaded.LoadedFileModifiedUtc!.Value,
                        libraryFileSize.Value, libraryFileModifiedUtc.Value)
                    ? VersionStatus.UpToDate
                    : VersionStatus.UpdateAvailable;

            var lib = FamilyVersionCheck.Normalize(libraryVersion);
            var proj = FamilyVersionCheck.Normalize(loaded.Version);
            return lib != null && proj != null ? VersionStatus.UpToDate : VersionStatus.Unknown;
        }

        public static bool SameFile(long sizeA, DateTime modifiedUtcA, long sizeB, DateTime modifiedUtcB) =>
            sizeA == sizeB
            && (ToUtc(modifiedUtcA) - ToUtc(modifiedUtcB)).Duration() <= TimeTolerance;

        private static DateTime ToUtc(DateTime d) => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d;
    }
}
