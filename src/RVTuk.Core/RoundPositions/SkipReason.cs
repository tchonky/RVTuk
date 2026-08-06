namespace RVTuk.Core.RoundPositions
{
    public enum SkipReason
    {
        InRevitLink,
        ImportedCad,
        InGroup,
        InDesignOption,
        OwnedByOtherUser,
        NotOrthogonal,
        UnexpectedClipState,
        RevitRejected,
    }

    public static class SkipReasonText
    {
        public static string Describe(SkipReason reason) => reason switch
        {
            SkipReason.InRevitLink =>
                "In a linked model — linked models are never moved",
            SkipReason.ImportedCad =>
                "Imported or linked CAD — the drawing the model was traced against stays put",
            SkipReason.InGroup =>
                "In a group — moving one member would rewrite every instance of the group",
            SkipReason.InDesignOption =>
                "In a design option",
            SkipReason.OwnedByOtherUser =>
                "Checked out by another user",
            SkipReason.NotOrthogonal =>
                "Not orthogonal — straightening crooked geometry comes in v2",
            SkipReason.UnexpectedClipState =>
                "Base point in an unexpected clip state — moving it could shift shared coordinates",
            SkipReason.RevitRejected =>
                "Revit rejected the move, even after releasing what was holding it",
            _ => reason.ToString(),
        };
    }
}
