namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// The answer of <see cref="ModifyTabPanels.Decide"/>: whether the copy of the Topo Edit panel,
    /// and whether the Topo Tools shortcut, belong on Revit's Modify tab. At most one of them does,
    /// and the copy never does without a session.
    /// </summary>
    public sealed record ModifyTabWanted(bool TopoEditCopy, bool TopoToolsShortcut);

    /// <summary>
    /// The rule for which of our panels belong on Revit's Modify tab, kept here so the Revit side
    /// only applies its answer. Outside a Topo Edit session the Topo Tools shortcut shows there
    /// while one toposolid or floor is selected. During a session a copy of the Topo Edit panel
    /// shows there instead, whatever is selected; if the copy is not available, the shortcut stays
    /// as it is outside a session. In Revit's edit modes, such as Edit Boundary or Edit Group,
    /// neither shows.
    /// </summary>
    public static class ModifyTabPanels
    {
        /// <param name="sessionOpen">A Topo Edit session is open.</param>
        /// <param name="inEditMode">Revit is in an edit mode, such as Edit Boundary or Edit Group.</param>
        /// <param name="oneTopoTargetSelected">One toposolid or floor is selected.</param>
        /// <param name="copyAvailable">The Revit side can put the copy on the Modify tab.</param>
        public static ModifyTabWanted Decide(
            bool sessionOpen, bool inEditMode, bool oneTopoTargetSelected, bool copyAvailable)
        {
            if (inEditMode) return new ModifyTabWanted(false, false);

            if (sessionOpen && copyAvailable) return new ModifyTabWanted(true, false);

            // Also during a session without the copy: the shortcut then opens the session dialog.
            return new ModifyTabWanted(false, oneTopoTargetSelected);
        }
    }
}
