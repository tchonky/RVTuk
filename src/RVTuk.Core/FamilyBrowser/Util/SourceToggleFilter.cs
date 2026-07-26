using RVTuk.Core.FamilyBrowser.Models;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// Predicate for the browser's three toolbar toggles. Each pressed toggle is an
    /// independent AND constraint; unpressed toggles constrain nothing. The model/library
    /// pair are <b>inclusive</b> membership filters — "loaded in the model" is every status
    /// except <see cref="VersionStatus.None"/>, "present in the library" is every status
    /// except <see cref="VersionStatus.ModelOnly"/> — so the two overlap on the in-both
    /// statuses and pressing both yields exactly the families that exist in both places.
    /// (The pre-ModelOnly design used exclusive toggles combined with OR because its two
    /// source states partitioned the list and an AND could only ever yield nothing.)
    /// </summary>
    public static class SourceToggleFilter
    {
        public static bool Matches(VersionStatus status, bool isFavorite,
            bool showModel, bool showLibrary, bool showFavorites)
            => (!showModel || status != VersionStatus.None)
            && (!showLibrary || status != VersionStatus.ModelOnly)
            && (!showFavorites || isFavorite);
    }
}
