using System.Text.RegularExpressions;

namespace RVTuk.Core.FamilyBrowser.Util;

/// <summary>
/// Rewrites the CLR-namespace declarations inside stored instruction FlowDocument XAML.
/// Stored form is always <c>clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI</c>
/// and <c>assembly=mscorlib</c>; <see cref="ForRead"/> maps it to where the helper type
/// lives now, <see cref="ForWrite"/> maps it back. Plain string replacement (no XML
/// parser) so every byte outside the declarations is preserved. Only attribute values
/// inside element tags are touched, never text between tags.
/// </summary>
public static class InstructionXamlRewriter
{
    public const string StoredNamespace = "RVTuk.UI.Shared.Controls";
    public const string StoredAssembly = "RVTuk.UI";

    private const string CoreLib = ";assembly=System.Private.CoreLib\"";
    private const string Mscorlib = ";assembly=mscorlib\"";
    private const string OldControls = "\"clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI\"";

    private static readonly Regex Tag = new Regex("<[^<>]+>", RegexOptions.Compiled);

    private static string Value(string ns, string asm) => "\"clr-namespace:" + ns + ";assembly=" + asm + "\"";

    public static string? ForRead(string? xaml, string helperNamespace, string helperAssembly)
    {
        if (string.IsNullOrEmpty(xaml)) return xaml;
        var helper = Value(helperNamespace, helperAssembly);
        var stored = Value(StoredNamespace, StoredAssembly);
        return Tag.Replace(xaml, m => m.Value
            .Replace(OldControls, helper)
            .Replace(stored, helper)
            .Replace(CoreLib, Mscorlib));
    }

    public static string? ForWrite(string? xaml, string helperNamespace, string helperAssembly)
    {
        if (string.IsNullOrEmpty(xaml)) return xaml;
        var helper = Value(helperNamespace, helperAssembly);
        var stored = Value(StoredNamespace, StoredAssembly);
        return Tag.Replace(xaml, m => m.Value
            .Replace(helper, stored)
            .Replace(CoreLib, Mscorlib));
    }
}
