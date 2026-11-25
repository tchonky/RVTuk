using System.Windows.Controls;
using System.Windows.Documents;
using RVTuk.UI.FamilyBrowser.Views;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    // A click in the window decides whether the search dropdown stays open. Clicks in text start
    // on content elements (a FlowDocument, a Run), not Visuals; the first version asked
    // IsAncestorOf about one directly, which throws and closed the Family Browser
    // (crash.log, 2026-11-25).
    public class SearchPopupClickTests
    {
        [Fact]
        public void ClickInInstructionsText_IsJudgedWithoutThrowing() => Sta.Run(() =>
        {
            var instructions = new RichTextBox(new FlowDocument(new Paragraph(new Run("Host on interior walls"))));
            var detail = new Border { Child = instructions };
            var searchRow = new Grid();
            var paragraph = (Paragraph)instructions.Document.Blocks.FirstBlock;
            var run = (Run)paragraph.Inlines.FirstInline;

            Assert.False(FamilyBrowserWindow.IsInside(instructions.Document, searchRow));
            Assert.False(FamilyBrowserWindow.IsInside(run, searchRow));
            Assert.True(FamilyBrowserWindow.IsInside(run, detail));
            Assert.True(FamilyBrowserWindow.IsInside(instructions.Document, detail));
        });

        [Fact]
        public void ClickOnTheSearchRowItself_CountsAsInside() => Sta.Run(() =>
        {
            var box = new TextBox();
            var searchRow = new Grid();
            searchRow.Children.Add(box);
            Assert.True(FamilyBrowserWindow.IsInside(box, searchRow));
            Assert.True(FamilyBrowserWindow.IsInside(searchRow, searchRow));
            Assert.False(FamilyBrowserWindow.IsInside(null, searchRow));
            Assert.False(FamilyBrowserWindow.IsInside(new FlowDocument(), searchRow)); // in no tree at all
        });
    }
}
