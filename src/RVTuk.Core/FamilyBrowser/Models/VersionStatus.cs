namespace RVTuk.Core.FamilyBrowser.Models
{
    // None = in the library only (not loaded in the project).
    // UpToDate / UpdateAvailable = in both; verdict of the _Version check.
    // ModelOnly = loaded in the project but absent from the library (synthetic browser row).
    // DuplicateInLibrary = in the project, but the library holds several copies of its file name:
    //   no verdict, never updated (which copy is "the" library one is unknown).
    public enum VersionStatus { None, UpToDate, UpdateAvailable, ModelOnly, DuplicateInLibrary }
}
