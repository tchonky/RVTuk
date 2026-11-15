using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class InstructionXamlRewriterTests
{
    private const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    // The post-split home of the helper, to prove nothing is hard-wired.
    private const string Ns = "RVTuk.UI.FamilyBrowser.Controls";
    private const string Asm = "RVTuk.FamilyBrowser.UI";

    private static string Doc(string declarations, string body = "<Paragraph />") =>
        $"<FlowDocument xmlns=\"{P}\" {declarations}>{body}</FlowDocument>";

    private static string Decl(string ns, string asm) =>
        $"xmlns:rvtuic=\"clr-namespace:{ns};assembly={asm}\"";

    [Fact]
    public void ForRead_OldControlsNamespace_BecomesTheHelpers()
    {
        var result = InstructionXamlRewriter.ForRead(Doc(Decl("RVTuk.UI.Controls", "RVTuk.UI")), Ns, Asm);
        Assert.Equal(Doc(Decl(Ns, Asm)), result);
    }

    [Fact]
    public void ForRead_StoredForm_BecomesTheHelpers()
    {
        var result = InstructionXamlRewriter.ForRead(Doc(Decl("RVTuk.UI.Shared.Controls", "RVTuk.UI")), Ns, Asm);
        Assert.Equal(Doc(Decl(Ns, Asm)), result);
    }

    [Fact]
    public void ForRead_StoredForm_IsUnchangedForTodaysHelper()
    {
        var x = Doc(Decl("RVTuk.UI.Shared.Controls", "RVTuk.UI"));
        Assert.Equal(x, InstructionXamlRewriter.ForRead(x, InstructionXamlRewriter.StoredNamespace, InstructionXamlRewriter.StoredAssembly));
    }

    [Fact]
    public void ForRead_CoreLib_BecomesMscorlib()
    {
        var result = InstructionXamlRewriter.ForRead(
            Doc("xmlns:s=\"clr-namespace:System;assembly=System.Private.CoreLib\""), Ns, Asm);
        Assert.Equal(Doc("xmlns:s=\"clr-namespace:System;assembly=mscorlib\""), result);
    }

    [Fact]
    public void ForWrite_HelpersDeclaration_BecomesTheStoredForm()
    {
        var result = InstructionXamlRewriter.ForWrite(Doc(Decl(Ns, Asm)), Ns, Asm);
        Assert.Equal(Doc(Decl("RVTuk.UI.Shared.Controls", "RVTuk.UI")), result);
    }

    [Fact]
    public void ForWrite_CoreLib_BecomesMscorlib()
    {
        var result = InstructionXamlRewriter.ForWrite(
            Doc("xmlns:s=\"clr-namespace:System;assembly=System.Private.CoreLib\""), Ns, Asm);
        Assert.Equal(Doc("xmlns:s=\"clr-namespace:System;assembly=mscorlib\""), result);
    }

    [Fact]
    public void ForWrite_ThenForRead_GivesTheOriginalBack()
    {
        var x = Doc(Decl(Ns, Asm) + " xmlns:s=\"clr-namespace:System;assembly=mscorlib\"");
        var stored = InstructionXamlRewriter.ForWrite(x, Ns, Asm);
        Assert.NotEqual(x, stored);
        Assert.Equal(x, InstructionXamlRewriter.ForRead(stored, Ns, Asm));
    }

    [Fact]
    public void LookAlikeNamesAreLeftAlone()
    {
        var x = Doc(Decl("RVTuk.UI.ControlsX", "RVTuk.UI") + " " + Decl("RVTuk.UI.Controls", "RVTuk.UI.Other"));
        Assert.Equal(x, InstructionXamlRewriter.ForRead(x, Ns, Asm));
    }

    [Fact]
    public void TextBetweenTagsIsNeverRewritten()
    {
        var body = "<Paragraph>xmlns:a=\"clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI\" and assembly=System.Private.CoreLib\"</Paragraph>";
        var x = Doc("", body);
        Assert.Equal(x, InstructionXamlRewriter.ForRead(x, Ns, Asm));
        Assert.Equal(x, InstructionXamlRewriter.ForWrite(x, Ns, Asm));
    }

    [Fact]
    public void NullAndEmptyComeBackAsTheyAre()
    {
        Assert.Null(InstructionXamlRewriter.ForRead(null, Ns, Asm));
        Assert.Equal("", InstructionXamlRewriter.ForRead("", Ns, Asm));
        Assert.Null(InstructionXamlRewriter.ForWrite(null, Ns, Asm));
        Assert.Equal("", InstructionXamlRewriter.ForWrite("", Ns, Asm));
    }
}
