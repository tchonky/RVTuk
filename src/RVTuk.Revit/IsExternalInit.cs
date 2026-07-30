#if NETFRAMEWORK
// net48 has no built-in System.Runtime.CompilerServices.IsExternalInit — the compiler
// needs this marker type to allow `init`-only members (and positional records, which
// generate them). net8 already ships it in the BCL.
//
// Core carries its own copy (tucked into RishuiZamin/UsageCatalog.cs): the type is internal,
// so it cannot be shared across assemblies, and each project that declares a record needs one.
// It lives at the project root rather than in a tool folder because it belongs to the whole
// assembly, not to any one tool.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
