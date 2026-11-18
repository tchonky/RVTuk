using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class ModifyTabPanelsTests
{
    [Theory]
    [InlineData(false, false, false, true,  false, false)]
    [InlineData(false, false, true,  true,  false, true )]
    [InlineData(true,  false, true,  true,  true,  false)]
    [InlineData(true,  false, false, true,  true,  false)]
    [InlineData(true,  false, true,  false, false, true )]  // copy off: the shortcut stays, and opens the session dialog
    [InlineData(true,  false, false, false, false, false)]  // copy off: the shortcut still follows the selection
    [InlineData(true,  true,  true,  true,  false, false)]  // edit mode: nothing of ours
    [InlineData(false, true,  true,  true,  false, false)]
    [InlineData(true,  true,  false, true,  false, false)]  // edit mode, no target selected: no copy either
    [InlineData(true,  true,  true,  false, false, false)]  // edit mode, copy off: no shortcut either
    [InlineData(false, false, true,  false, false, true )]  // copy off, no session: the shortcut as usual
    [InlineData(false, true,  true,  false, false, false)]  // edit mode, copy off, no session: no shortcut either
    [InlineData(false, false, false, false, false, false)]
    [InlineData(false, true,  false, false, false, false)]
    [InlineData(false, true,  false, true,  false, false)]
    [InlineData(true,  true,  false, false, false, false)]
    public void Decides(bool session, bool editMode, bool oneTarget, bool copyAvailable, bool copy, bool shortcut)
        => Assert.Equal(new ModifyTabWanted(copy, shortcut),
                        ModifyTabPanels.Decide(session, editMode, oneTarget, copyAvailable));

    [Fact]
    public void NamesTheCopyAndTheShortcut()
    {
        var duringSession = ModifyTabPanels.Decide(
            sessionOpen: true, inEditMode: false, oneToposolidSelected: false, copyAvailable: true);
        Assert.True(duringSession.TopoEditCopy);
        Assert.False(duringSession.TopoToolsShortcut);

        var outsideSession = ModifyTabPanels.Decide(
            sessionOpen: false, inEditMode: false, oneToposolidSelected: true, copyAvailable: true);
        Assert.False(outsideSession.TopoEditCopy);
        Assert.True(outsideSession.TopoToolsShortcut);
    }
}
