using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using AwRibbonPanel = Autodesk.Windows.RibbonPanel;
using AwRibbonPanelSource = Autodesk.Windows.RibbonPanelSource;
using AwRibbonTab = Autodesk.Windows.RibbonTab;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// What the Topo Tools ribbon code reaches in Revit's ribbon beyond the supported API: tabs
    /// found by Id, the Ids Revit gives what an add-in puts on the Add-Ins tab, a panel of the class
    /// Revit makes its own panels with, and the Modify tab's edit mode. It goes through the
    /// unsupported Autodesk.Windows ribbon, and through UIFramework by reflection only (the project
    /// has no reference to it), so any of it may throw, also after a Revit update: callers catch,
    /// log one line and switch their part off.
    ///
    /// Each member that touches either library is NoInlining and is called from inside the
    /// caller's try: a member that a Revit update removed then throws there, not while the caller
    /// itself is being compiled, outside its try.
    /// </summary>
    internal static class ModifyTabAccess
    {
        /// <summary>Autodesk.Windows Id of the Add-Ins tab, which hosts the RVTuk and Topo Edit
        /// panels. Tabs are found by Id, never by their localised title.</summary>
        internal const string AddInsTabId = "Add-Ins";

        /// <summary>Autodesk.Windows Id of the Modify tab: the one tab whose title and panels Revit
        /// changes with the selection and the command ("Modify | Toposolid" and the like).</summary>
        internal const string ModifyTabId = "Modify";

        /// <summary>The start of every Id Revit gives what an add-in puts on the ribbon.</summary>
        private const string CustomCtrl = "CustomCtrl_%";

        private const string UIFrameworkName = "UIFramework";

        /// <summary>The class Revit makes the panels of its Modify tab with.</summary>
        private const string RevitPanelTypeName = "UIFramework.RvtRibbonPanel";

        /// <summary>The flag UIFramework.RvtRibbonTab sets when Revit builds the tab for a context:
        /// true for an edit mode.</summary>
        private const string EditModePropertyName = "IsInEditMode";

        /// <summary>The Autodesk.Windows source Id Revit gives a panel an add-in makes on the Add-Ins
        /// tab: CustomCtrl_%Add-Ins% and the panel's name.</summary>
        internal static string AddInsPanelId(string panelName) => CustomCtrl + AddInsTabId + "%" + panelName;

        /// <summary>The id Revit gives a push button on such a panel: CustomCtrl_%, the panel's
        /// source Id, % and the button's name. It is both the button's command id and its
        /// Autodesk.Windows Id; a stack adds no level.</summary>
        internal static string AddInsButtonId(string panelName, string buttonName) =>
            CustomCtrl + AddInsPanelId(panelName) + "%" + buttonName;

        /// <summary>The ribbon's tab with this Id, hidden or not; null when there is none, or no
        /// ribbon.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static AwRibbonTab? FindTab(string id) => Autodesk.Windows.ComponentManager.Ribbon?.FindTab(id);

        /// <summary>
        /// A new panel of Revit's own panel class, UIFramework.RvtRibbonPanel, showing
        /// <paramref name="source"/>. Revit's code walks the Modify tab's panels as if all were of
        /// that class, so nothing of ours goes there as a plain Autodesk.Windows panel.
        ///
        /// Never null, never half made, and it logs nothing: it throws, and the caller logs. A step
        /// that fails without an exception of its own (UIFramework not loaded, the class not in it,
        /// an instance that is not an Autodesk.Windows panel) throws an InvalidOperationException
        /// naming the step and the class. What the class's constructor throws comes out as itself,
        /// not wrapped by reflection, and what setting the source throws comes out as it is.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static AwRibbonPanel NewRevitPanel(AwRibbonPanelSource source)
        {
            Assembly uiFramework = LoadedAssembly(UIFrameworkName)
                ?? throw new InvalidOperationException(
                    $"Finding the {UIFrameworkName} assembly for {RevitPanelTypeName}: it is not loaded.");
            Type type = uiFramework.GetType(RevitPanelTypeName, throwOnError: false)
                ?? throw new InvalidOperationException(
                    $"Finding the type {RevitPanelTypeName}: {uiFramework.FullName} has no such type.");
            var instance = Unwrapped(() => Activator.CreateInstance(type));
            if (instance is not AwRibbonPanel panel)
                throw new InvalidOperationException(
                    $"Making a {RevitPanelTypeName}: the instance is {instance?.GetType().FullName ?? "null"}, " +
                    "not an Autodesk.Windows.RibbonPanel.");
            panel.Source = source;
            return panel;
        }

        /// <summary>
        /// Whether Revit is in an edit mode (Edit Boundary, a sketch, Edit Group), read on the Modify
        /// tab: it is contextual, and UIFramework.RvtRibbonTab's IsInEditMode, read by reflection, is
        /// set. Revit writes IsInEditMode only when it builds the tab for a context, and it starts
        /// true, so on its own it stays true on the plain Modify tab: at start-up (seen in the
        /// Revit 2024 probe), and after an edit mode that ends with nothing selected (read in
        /// UIFramework 2024, 2026 and 2027). Throws when the flag cannot be read.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsInEditMode(AwRibbonTab modifyTab)
        {
            bool contextual = modifyTab.IsContextualTab;

            // Read every time, contextual or not, so a flag that a Revit update took away shows at
            // the first call.
            Type type = modifyTab.GetType();
            PropertyInfo? found = type.GetProperty(EditModePropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (found == null || found.PropertyType != typeof(bool) || !found.CanRead
                || found.GetIndexParameters().Length != 0)
                throw new InvalidOperationException(
                    $"Reading the edit mode: {type.FullName} has no public bool property {EditModePropertyName}.");
            PropertyInfo property = found;
            if (Unwrapped(() => property.GetValue(modifyTab)) is not bool editMode)
                throw new InvalidOperationException(
                    $"Reading the edit mode: {type.FullName}.{EditModePropertyName} gave no value.");
            return contextual && editMode;
        }

        /// <summary>The loaded assembly with this simple name, or null. Loads nothing.</summary>
        private static Assembly? LoadedAssembly(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? loadedName;
                try
                {
                    loadedName = assembly.GetName().Name;
                }
                catch (Exception)
                {
                    // An assembly whose name cannot be read is not the one looked for: on to the next.
                    continue;
                }
                if (string.Equals(loadedName, name, StringComparison.Ordinal)) return assembly;
            }
            return null;
        }

        /// <summary>Runs a reflective call. What the member called throws comes out as itself, not
        /// wrapped in a TargetInvocationException, so the caller's one log line names it.</summary>
        private static T Unwrapped<T>(Func<T> call)
        {
            try
            {
                return call();
            }
            catch (TargetInvocationException ex) when (ex.InnerException is Exception inner)
            {
                ExceptionDispatchInfo.Capture(inner).Throw();
                throw; // not reached: Throw always throws
            }
        }
    }
}
