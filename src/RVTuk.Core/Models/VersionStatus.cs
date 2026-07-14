namespace RVTuk.Core.Models
{
    // None = in the library only (not loaded in the project).
    // UpToDate / UpdateAvailable = in both; verdict of the _Version check.
    // ModelOnly = loaded in the project but absent from the library (synthetic browser row).
    public enum VersionStatus { None, UpToDate, UpdateAvailable, ModelOnly }
}
