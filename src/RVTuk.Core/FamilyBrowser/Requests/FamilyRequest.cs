namespace RVTuk.Core.FamilyBrowser.Requests
{
    /// <summary>
    /// One user's request to the library maintainer: fix a family, or add a new one. Stored as
    /// one small JSON file in the library's <c>.Requests</c> folder, so two people asking at
    /// the same time never write the same file (see <see cref="RequestStore"/>).
    /// Only read/write properties: both serializers (DataContractJsonSerializer on net48,
    /// System.Text.Json elsewhere) then write the same keys.
    /// </summary>
    public class FamilyRequest
    {
        public const string KindFix = "fix";
        public const string KindNewFamily = "new";

        /// <summary>A family from a user's project, attached, proposed for the library (a
        /// view-only user's Save to Library). The maintainer approves it into a library folder.</summary>
        public const string KindSuggestion = "add";

        /// <summary>The file name without ".json". Set from the file on reading.</summary>
        public string Id { get; set; } = string.Empty;

        public string Kind { get; set; } = KindFix;

        /// <summary>Library-relative path of the family to fix; empty for a new-family request.</summary>
        public string? RelativePath { get; set; }

        public string? FamilyName { get; set; }

        /// <summary>The family's category in the project (suggestions): where Approve files it.</summary>
        public string? Category { get; set; }

        public string User { get; set; } = string.Empty;

        /// <summary>UTC, ISO-8601 ("o"). A string, not a DateTime: the two serializers write
        /// dates differently.</summary>
        public string CreatedUtc { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        /// <summary>File name of the user's project version of the family, saved next to the
        /// request; null when none was attached.</summary>
        public string? Attachment { get; set; }
    }
}
