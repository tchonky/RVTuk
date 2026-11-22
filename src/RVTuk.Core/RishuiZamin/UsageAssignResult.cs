namespace RVTuk.Core.RishuiZamin
{
    /// <summary>Which of an area's two usage keys is being set: "New Usage" (drives
    /// RZ_USAGE_TYPE and the Name) or "Old Usage" (drives RZ_USAGE_TYPE_OLD).</summary>
    public enum UsageKeySlot
    {
        New,
        Old
    }

    public enum UsageAssignStatus
    {
        /// <summary>The key was set on every area, in one undo step.</summary>
        Done,

        /// <summary>The key schedule, or its row for the code, does not exist; nothing was
        /// changed. The window offers to run Setup Usage Keys first.</summary>
        KeysMissing,

        /// <summary>Nothing was changed; <see cref="UsageAssignResult.Message"/> says why.</summary>
        Failed
    }

    public sealed record UsageAssignResult(UsageAssignStatus Status, string Message);
}
