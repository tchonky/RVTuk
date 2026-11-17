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
    public void Decides(bool session, bool editMode, bool oneTarget, bool copyAvailable, bool copy, bool shortcut)
        => Assert.Equal(new ModifyTabWanted(copy, shortcut),
                        ModifyTabPanels.Decide(session, editMode, oneTarget, copyAvailable));
}
