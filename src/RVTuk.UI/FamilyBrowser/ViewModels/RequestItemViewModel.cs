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
        public bool IsFix => Request.Kind != FamilyRequest.KindNewFamily;
        public bool IsNewFamily => !IsFix;
        public string Title => IsFix ? (Request.FamilyName ?? Request.RelativePath ?? "Family") : "New family";
        public string IconName => IsFix ? "tool" : "plus";
        public string Text => Request.Text;

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
