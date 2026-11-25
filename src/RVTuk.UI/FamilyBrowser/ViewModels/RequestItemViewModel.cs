using System;
using System.Globalization;
using RVTuk.Core.FamilyBrowser.Requests;

namespace RVTuk.UI.FamilyBrowser.ViewModels
{
    /// <summary>One open request, as the Requests list and the detail banner show it. The text
    /// was written by another user: it is only ever shown as plain text.</summary>
    public sealed class RequestItemViewModel
    {
        public RequestItemViewModel(FamilyRequest request, bool canMarkDone, string currentUser)
        {
            Request = request;
            CanMarkDone = canMarkDone;
            CanWithdraw = string.Equals(request.User, currentUser, StringComparison.OrdinalIgnoreCase);
        }

        public FamilyRequest Request { get; }
        public bool IsNewFamily => Request.Kind == FamilyRequest.KindNewFamily;
        public bool IsSuggestion => Request.Kind == FamilyRequest.KindSuggestion;
        /// <summary>Any other kind is a fix, so a request from a newer add-in still shows.</summary>
        public bool IsFix => !IsNewFamily && !IsSuggestion;

        /// <summary>The title line: a fix's family is a link to it; the others are plain text.</summary>
        public bool HasPlainTitle => !IsFix;
        public string Title => IsNewFamily ? "New family"
            : IsSuggestion ? (Request.FamilyName ?? "Suggested family") + (string.IsNullOrEmpty(Request.Category) ? "" : " · " + Request.Category)
            : (Request.FamilyName ?? Request.RelativePath ?? "Family");
        public string IconName => IsNewFamily ? "plus" : IsSuggestion ? "box" : "tool";
        public string Text => Request.Text;
        public bool HasText => !string.IsNullOrWhiteSpace(Request.Text);

        /// <summary>The maintainer approves a suggestion into the library.</summary>
        public bool CanApprove => CanMarkDone && IsSuggestion;

        /// <summary>For a suggestion, closing it without adding the family is a rejection.</summary>
        public string DoneLabel => IsSuggestion ? "Reject" : "Done";
        public string DoneToolTip => IsSuggestion
            ? "Close this suggestion without adding the family to the library"
            : "Mark this request as done";

        /// <summary>Right to left when the text starts with Hebrew (or Arabic), like dir="auto":
        /// office requests mix Hebrew and English, and a Hebrew sentence laid out left to right
        /// puts its punctuation and numbers on the wrong side.</summary>
        public System.Windows.FlowDirection TextFlowDirection => StartsRightToLeft(Request.Text)
            ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

        public static bool StartsRightToLeft(string? text)
        {
            foreach (var c in text ?? string.Empty)
            {
                if (c >= '֐' && c <= 'ࣿ') return true;   // Hebrew, Arabic, Syriac…
                if (c >= 'יִ' && c <= '﷿') return true;   // presentation forms
                if (char.IsLetter(c)) return false;                // first strong letter is Latin etc.
            }
            return false;
        }
        public bool HasAttachment => !string.IsNullOrEmpty(Request.Attachment);

        /// <summary>"Their project's version is attached" — said of fixes; a suggestion always is.</summary>
        public bool ShowAttachmentNote => HasAttachment && IsFix;

        public string Meta
        {
            get
            {
                var who = CanWithdraw ? "You" : Request.User;
                return DateTime.TryParse(Request.CreatedUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var at)
                    ? who + " · " + at.ToLocalTime().ToString("d")
                    : who;
            }
        }

        /// <summary>The maintainer (who can edit the library) closes requests.</summary>
        public bool CanMarkDone { get; }

        /// <summary>Users can take back their own requests.</summary>
        public bool CanWithdraw { get; }
    }
}
