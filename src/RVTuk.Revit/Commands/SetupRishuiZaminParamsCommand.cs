using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AreaSubmission;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace RVTuk.Revit.Commands
{
    /// <summary>
    /// One-time (idempotent) setup command for the Rishui Zamin area workflow: binds the
    /// <c>RZ_UsageType</c> shared parameter to Areas as an instance parameter, and creates an
    /// Area key schedule for the usage catalog.
    ///
    /// The parameter bind is the essential part and always runs. The key-schedule row pre-fill
    /// is best-effort: the public Revit API has no supported way to programmatically insert key
    /// schedule rows (there is no "add key" method on <see cref="ViewSchedule"/> or
    /// <see cref="Document.Create"/> — only the UI's "New Row" button does this, backed by an
    /// internal, unexposed command). So this command creates the (empty) key schedule and leaves
    /// row entry to the user; see the TaskDialog / report for the exact codes and Hebrew names to
    /// type in.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SetupRishuiZaminParamsCommand : IExternalCommand
    {
        private const string ParamName = "RZ_UsageType";
        private const string SharedParamFileName = "RZ_AreaParams.txt";
        private const string KeyScheduleName = "RZ Usage Key Schedule";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var app = uiApp.Application;
            var doc = uiApp.ActiveUIDocument?.Document;

            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var summary = new StringBuilder();

            try
            {
                using (var tx = new Transaction(doc, "Setup Rishui Zamin parameters"))
                {
                    tx.Start();

                    EnsureUsageTypeBinding(app, doc, summary);
                    TryCreateKeySchedule(doc, summary);

                    tx.Commit();
                }

                TaskDialog.Show("RVTuk – Rishui Zamin Setup", summary.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("RVTuk – Rishui Zamin Setup", $"Setup failed:\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// ESSENTIAL: binds RZ_UsageType (Integer, instance) to OST_Areas via the deployed shared
        /// parameter file, unless it is already bound. Runs inside the caller's transaction.
        /// </summary>
        private static void EnsureUsageTypeBinding(RevitApplication app, Document doc, StringBuilder summary)
        {
            if (IsAlreadyBound(doc))
            {
                summary.AppendLine($"'{ParamName}' is already bound to Areas — left unchanged.");
                return;
            }

            string addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Could not resolve the add-in's own folder.");
            string sharedParamFile = Path.Combine(addinDir, SharedParamFileName);

            if (!File.Exists(sharedParamFile))
            {
                throw new FileNotFoundException(
                    $"Shared parameter file not found next to the add-in: {sharedParamFile}", sharedParamFile);
            }

            // Point at our file; restore whatever the user had configured afterwards so this
            // command doesn't silently repoint their shared-parameter file for other work.
            string? previousFile = SafeGet(() => app.SharedParametersFilename);
            app.SharedParametersFilename = sharedParamFile;

            try
            {
                DefinitionFile defFile = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException($"Could not open shared parameter file: {sharedParamFile}");

                ExternalDefinition? extDef = defFile.Groups
                    .Cast<DefinitionGroup>()
                    .SelectMany(g => g.Definitions.Cast<Definition>())
                    .OfType<ExternalDefinition>()
                    .FirstOrDefault(d => d.Name == ParamName);

                if (extDef == null)
                {
                    throw new InvalidOperationException(
                        $"'{ParamName}' was not found in {SharedParamFileName}.");
                }

                Category? areaCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Areas);
                if (areaCategory == null)
                {
                    throw new InvalidOperationException("The Areas category is not available in this document.");
                }

                var catSet = app.Create.NewCategorySet();
                catSet.Insert(areaCategory);

                var binding = app.Create.NewInstanceBinding(catSet);
                // GroupTypeId.IdentityData is the ForgeTypeId successor to the (now-removed-in-2025)
                // BuiltInParameterGroup.PG_IDENTITY_DATA enum member; available on both API versions.
                bool inserted = doc.ParameterBindings.Insert(extDef, binding, GroupTypeId.IdentityData);

                summary.AppendLine(inserted
                    ? $"Bound '{ParamName}' (Integer, instance) to Areas under Identity Data."
                    : $"Could not bind '{ParamName}' to Areas (ParameterBindings.Insert returned false).");
            }
            finally
            {
                // Best-effort restore — never let this throw and mask the real result above.
                try { app.SharedParametersFilename = previousFile; } catch { /* ignore */ }
            }
        }

        private static bool IsAlreadyBound(Document doc)
        {
            var iter = doc.ParameterBindings.ForwardIterator();
            iter.Reset();
            while (iter.MoveNext())
            {
                if (iter.Key is Definition def && def.Name == ParamName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// BEST-EFFORT: creates the Area key schedule if it doesn't already exist. Row pre-fill
        /// from <see cref="UsageCatalog"/> is intentionally NOT attempted — see class remarks.
        /// Any failure here is caught and reported, never thrown, so the essential parameter
        /// bind above still counts as a successful run.
        /// </summary>
        private static void TryCreateKeySchedule(Document doc, StringBuilder summary)
        {
            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(v => v.Name == KeyScheduleName);

                if (existing != null)
                {
                    summary.AppendLine($"Key schedule '{KeyScheduleName}' already exists — left unchanged.");
                }
                else
                {
                    var schedule = ViewSchedule.CreateKeySchedule(doc, new ElementId(BuiltInCategory.OST_Areas));
                    schedule.Name = KeyScheduleName;
                    summary.AppendLine($"Created key schedule '{KeyScheduleName}'.");
                }

                summary.AppendLine();
                summary.AppendLine(
                    $"NOTE: Revit's API has no supported way to insert key-schedule rows " +
                    $"programmatically, so the {UsageCatalog.All.Count} usage rows were NOT pre-filled. " +
                    "Open the schedule, use \"New Row\" for each code, and set the key Name to the " +
                    $"Hebrew name and '{ParamName}' to the code (see RVTuk.Core.AreaSubmission.UsageCatalog " +
                    "for the full code/name list).");
            }
            catch (Exception ex)
            {
                summary.AppendLine($"Key schedule setup skipped (best-effort): {ex.Message}");
            }
        }

        private static T? SafeGet<T>(Func<T> getter) where T : class
        {
            try { return getter(); }
            catch { return null; }
        }
    }
}
