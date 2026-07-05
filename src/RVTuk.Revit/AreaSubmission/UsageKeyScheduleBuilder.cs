using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using RVTuk.Core.AreaSubmission;

namespace RVTuk.Revit.AreaSubmission
{
    /// <summary>
    /// Sets a project up for area submission: binds the five robot text parameters
    /// (<c>RZ_USAGE_TYPE</c>, <c>RZ_USAGE_TYPE_OLD</c>, <c>RZ_AREA</c>, <c>RZ_ASSET</c>,
    /// <c>RZ_BUILDING_NO</c>) to the Areas category (shared parameters with stable GUIDs, so
    /// every project gets the same definitions) and creates two Area key schedules filled from
    /// <see cref="UsageCatalog"/>:
    ///
    /// "New Usage" — Key Name = "«code» - «Hebrew name»" for the whole catalog; drives
    /// <c>RZ_USAGE_TYPE</c> (the code as text) and the area's system Name parameter (the Hebrew
    /// description), so picking a key both codes and names the area.
    ///
    /// "Old Usage" — same key naming, but without the process/demolition codes (300-302 can
    /// never be an existing-permit usage); drives <c>RZ_USAGE_TYPE_OLD</c> only, so assigning
    /// permit history never renames the area.
    ///
    /// Key row names are written through the <see cref="BuiltInParameter.REF_TABLE_ELEM_NAME"/>
    /// ("Key Name") parameter, NOT the <c>Element.Name</c> setter: key rows of an Area key
    /// schedule are Area-class elements, and Areas do not implement the Name setter — it throws
    /// "This element does not support assignment of a user-specified name".
    ///
    /// Idempotent: existing parameters, schedules and key rows are kept, missing catalog rows
    /// are topped up (safe to re-run after a catalog update).
    /// </summary>
    public static class UsageKeyScheduleBuilder
    {
        public const string UsageTypeParam = "RZ_USAGE_TYPE";
        public const string UsageTypeOldParam = "RZ_USAGE_TYPE_OLD";
        public const string PermitAreaParam = "RZ_AREA";
        public const string AssetParam = "RZ_ASSET";
        public const string BuildingNoParam = "RZ_BUILDING_NO";

        private const string NewUsageScheduleName = "New Usage";
        private const string OldUsageScheduleName = "Old Usage";

        // Stable shared-parameter GUIDs: the same definition in every project, so schedules,
        // tags and the extractor always agree on identity (not just on the display name).
        private static readonly (string Name, Guid Guid)[] ParameterDefs =
        {
            (UsageTypeParam, new Guid("737fe9fb-a9ab-4e92-b951-bafbeb34eec0")),
            (UsageTypeOldParam, new Guid("50640c88-869d-40cc-b9c0-d82180e83a57")),
            (PermitAreaParam, new Guid("e59fbd50-19bb-4185-a7d6-6dc9191c7c4f")),
            (AssetParam, new Guid("7af015fb-bc87-438a-93c8-869578f37f67")),
            (BuildingNoParam, new Guid("e8702634-4fc1-4bb0-b0e1-2d944aa3944a")),
        };

        /// <summary>Runs the full setup inside one transaction and reports what happened.</summary>
        public static (bool ok, string message) EnsureUsageKeySchedules(Document doc)
        {
            using var tx = new Transaction(doc, "RZ area parameters + usage key schedules");
            tx.Start();

            var report = new StringBuilder();
            try
            {
                var boundNow = EnsureAreaTextParameters(doc);
                report.AppendLine(boundNow == 0
                    ? $"Area text parameters: all {ParameterDefs.Length} already present."
                    : $"Area text parameters: bound {boundNow} new (of {ParameterDefs.Length}).");
                doc.Regenerate();

                var msgNew = EnsureSchedule(doc, NewUsageScheduleName, "New Usage",
                    UsageTypeParam, setAreaName: true, UsageCatalog.All);
                report.AppendLine(msgNew);

                // Demolition/process codes (300-302) mark work being done, never a usage that
                // exists in the current permit — the Old Usage pick list must not offer them.
                var oldEntries = UsageCatalog.All.Where(e => e.Kind != UsageKind.Process).ToList();
                var msgOld = EnsureSchedule(doc, OldUsageScheduleName, "Old Usage",
                    UsageTypeOldParam, setAreaName: false, oldEntries);
                report.AppendLine(msgOld);

                tx.Commit();
                return (true, report.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                tx.RollBack();
                return (false, "Usage key schedule setup failed: " + ex.Message);
            }
        }

        /// <summary>Binds any of the five text parameters not yet bound to Areas, via a
        /// temporary shared-parameter file (the API cannot create non-shared project
        /// parameters). Returns how many new bindings were added.</summary>
        private static int EnsureAreaTextParameters(Document doc)
        {
            var app = doc.Application;
            var areasCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Areas);

            var alreadyBound = new HashSet<string>(StringComparer.Ordinal);
            var iterator = doc.ParameterBindings.ForwardIterator();
            while (iterator.MoveNext())
            {
                if (iterator.Key is Definition definition &&
                    iterator.Current is ElementBinding binding &&
                    binding.Categories.Contains(areasCategory))
                {
                    alreadyBound.Add(definition.Name);
                }
            }

            var missing = ParameterDefs.Where(p => !alreadyBound.Contains(p.Name)).ToList();
            if (missing.Count == 0)
            {
                return 0;
            }

            var originalFile = app.SharedParametersFilename;
            try
            {
                var tempFile = Path.Combine(Path.GetTempPath(), "RVTuk_SharedParams.txt");
                if (!File.Exists(tempFile))
                {
                    File.WriteAllText(tempFile, "", Encoding.Unicode);
                }

                app.SharedParametersFilename = tempFile;
                var sharedFile = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException("Could not open the temporary shared-parameter file.");

                var group = sharedFile.Groups.get_Item("RVTuk") ?? sharedFile.Groups.Create("RVTuk");

                var categories = app.Create.NewCategorySet();
                categories.Insert(areasCategory);
                var instanceBinding = app.Create.NewInstanceBinding(categories);

                foreach (var (name, guid) in missing)
                {
                    var definition = group.Definitions.get_Item(name) as ExternalDefinition
                        ?? (ExternalDefinition)group.Definitions.Create(
                            new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text) { GUID = guid });

                    // GroupTypeId.General is today's ForgeTypeId for what Revit's UI used to
                    // label "Other" — keeps these out of the crowded Identity Data group.
                    doc.ParameterBindings.Insert(definition, instanceBinding, GroupTypeId.General);
                }

                return missing.Count;
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
            }
        }

        /// <summary>Finds or creates one key schedule and tops up its rows from
        /// <paramref name="entries"/>. Key name = "«code» - «Hebrew name»", so the pick list is
        /// self-describing; the key drives the code text parameter and (for the New Usage
        /// schedule) the area's system Name.</summary>
        private static string EnsureSchedule(Document doc, string scheduleName, string keyParameterName,
            string codeParam, bool setAreaName, IReadOnlyList<UsageEntry> entries)
        {
            var existingView = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => v.Name == scheduleName);

            if (existingView != null && (!existingView.Definition.IsKeySchedule ||
                existingView.Definition.CategoryId != new ElementId(BuiltInCategory.OST_Areas)))
            {
                throw new InvalidOperationException(
                    $"A view named \"{scheduleName}\" already exists but is not an Area key schedule — rename or delete it and re-run.");
            }

            var schedule = existingView;
            var created = false;
            if (schedule == null)
            {
                schedule = ViewSchedule.CreateKeySchedule(doc, new ElementId(BuiltInCategory.OST_Areas));
                schedule.Name = scheduleName;
                try
                {
                    // The parameter Areas expose in Properties for picking a key ("New Usage" /
                    // "Old Usage" — same wording as the schedule title).
                    schedule.KeyScheduleParameterName = keyParameterName;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Revit refuses in some states (e.g. name clash); the auto-assigned key
                    // parameter name still works, it just reads less nicely in Properties.
                }

                created = true;
            }

            AddFieldIfMissing(doc, schedule, codeParam, null);
            var nameFieldNote = "";
            if (setAreaName)
            {
                // The area's system Name parameter, driven by the key: picking "108 - מערכות
                // טכניות" names the area "מערכות טכניות". Not all Revit versions offer Name as
                // a key-schedule field — skip with a note instead of failing the whole setup.
                if (!TryAddFieldIfMissing(doc, schedule, null, BuiltInParameter.ROOM_NAME))
                {
                    nameFieldNote = " (system Name parameter is not schedulable here — key rows carry only the code)";
                    setAreaName = false;
                }
            }

            var existingKeys = new HashSet<string>(
                new FilteredElementCollector(doc, schedule.Id).ToElements().Select(KeyRowName),
                StringComparer.Ordinal);

            var body = schedule.GetTableData().GetSectionData(SectionType.Body);
            var added = 0;
            foreach (var entry in entries)
            {
                var keyName = entry.Code.ToString(CultureInfo.InvariantCulture) + " - " + entry.HebrewName;
                if (existingKeys.Contains(keyName))
                {
                    continue;
                }

                var before = new HashSet<ElementId>(new FilteredElementCollector(doc, schedule.Id).ToElementIds());
                body.InsertRow(body.FirstRowNumber);

                var newId = new FilteredElementCollector(doc, schedule.Id).ToElementIds()
                    .FirstOrDefault(id => !before.Contains(id));
                if (newId == null || doc.GetElement(newId) is not Element keyElement)
                {
                    throw new InvalidOperationException($"Could not create key row for {keyName}.");
                }

                SetKeyRowName(keyElement, keyName);
                keyElement.LookupParameter(codeParam)?.Set(entry.Code.ToString(CultureInfo.InvariantCulture));
                if (setAreaName)
                {
                    keyElement.get_Parameter(BuiltInParameter.ROOM_NAME)?.Set(entry.HebrewName);
                }

                added++;
            }

            return created
                ? $"\"{scheduleName}\": created with {added} usage keys{nameFieldNote}."
                : added == 0
                    ? $"\"{scheduleName}\": already present, all {entries.Count} keys in place{nameFieldNote}."
                    : $"\"{scheduleName}\": topped up {added} missing usage keys{nameFieldNote}.";
        }

        /// <summary>The key row's "Key Name" — read through REF_TABLE_ELEM_NAME to match how
        /// <see cref="SetKeyRowName"/> writes it (Element.Name is only a fallback).</summary>
        private static string KeyRowName(Element keyRow)
        {
            var p = keyRow.get_Parameter(BuiltInParameter.REF_TABLE_ELEM_NAME);
            return p != null && p.HasValue ? p.AsString() ?? "" : keyRow.Name;
        }

        /// <summary>Names a key row via the "Key Name" parameter. Area key rows are Area-class
        /// elements whose <c>Element.Name</c> setter throws ("This element does not support
        /// assignment of a user-specified name"), so the parameter is the only reliable path;
        /// the property setter stays as fallback for categories where the parameter is absent.</summary>
        private static void SetKeyRowName(Element keyRow, string keyName)
        {
            var p = keyRow.get_Parameter(BuiltInParameter.REF_TABLE_ELEM_NAME);
            if (p != null && !p.IsReadOnly)
            {
                p.Set(keyName);
                return;
            }

            keyRow.Name = keyName;
        }

        private static void AddFieldIfMissing(Document doc, ViewSchedule schedule, string? parameterName, BuiltInParameter? builtIn)
        {
            if (!TryAddFieldIfMissing(doc, schedule, parameterName, builtIn))
            {
                throw new InvalidOperationException(
                    $"Parameter \"{parameterName}\" is not schedulable on Areas — binding failed?");
            }
        }

        /// <summary>Adds a schedule field by shared-parameter name or by built-in parameter id.
        /// Matching built-ins by id (not display name) keeps this working on localized Revit.</summary>
        private static bool TryAddFieldIfMissing(Document doc, ViewSchedule schedule, string? parameterName, BuiltInParameter? builtIn)
        {
            var definition = schedule.Definition;
            var builtInId = builtIn == null ? null : new ElementId(builtIn.Value);

            for (var i = 0; i < definition.GetFieldCount(); i++)
            {
                var field = definition.GetField(i);
                if (parameterName != null && field.GetName() == parameterName)
                {
                    return true;
                }

                if (builtInId != null && field.ParameterId == builtInId)
                {
                    return true;
                }
            }

            var schedulable = definition.GetSchedulableFields()
                .FirstOrDefault(f => parameterName != null
                    ? f.GetName(doc) == parameterName
                    : f.ParameterId == builtInId);
            if (schedulable == null)
            {
                return false;
            }

            definition.AddField(schedulable);
            return true;
        }
    }
}
