using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RishuiZamin;

namespace RVTuk.Revit.RishuiZamin
{
    /// <summary>
    /// Sets the "New Usage" or "Old Usage" key on areas — the same thing as picking a key in
    /// Properties, so the key schedule keeps driving RZ_USAGE_TYPE(_OLD) and the Name. One undo
    /// step per call, even when the key schedules have to be set up first; nothing changes on
    /// failure. Also blanks the office template's old "Usage Type" parameters on those areas,
    /// since <see cref="AreaExtractor"/> falls back to them when RZ_* is empty and a cleared code
    /// would otherwise come back. See
    /// docs/tools/rishui-zamin/specs/2026-11-22-area-usage-assign-design.md §5.7.
    /// </summary>
    public static class UsageKeyAssigner
    {
        public static UsageAssignResult Assign(Document doc, UsageKeySlot slot, int? code,
            IReadOnlyList<long> areaIds, bool runSetupFirst)
        {
            var scheduleName = slot == UsageKeySlot.New
                ? UsageKeyScheduleBuilder.NewUsageScheduleName
                : UsageKeyScheduleBuilder.OldUsageScheduleName;
            var codeParam = slot == UsageKeySlot.New
                ? UsageKeyScheduleBuilder.UsageTypeParam
                : UsageKeyScheduleBuilder.UsageTypeOldParam;
            var legacyParams = slot == UsageKeySlot.New
                ? new[] { "Usage Type", "Usage Type Name" }
                : new[] { "Usage Type Prev", "Usage Type Prev. Name" };
            var txName = slot == UsageKeySlot.New ? "Area Calc — set New usage" : "Area Calc — set Old usage";

            if (doc.IsReadOnly)
                return Failed("The project is read-only.");
            if (code != null)
            {
                var entry = UsageCatalog.ByCode(code.Value);
                if (entry == null)
                    return Failed($"Code {code} is not in the usage table.");
                if (slot == UsageKeySlot.Old && entry.Kind == UsageKind.Process)
                    return Failed($"Code {code} is a process marker and cannot be an Old usage.");
            }

            var areas = new List<Area>();
            foreach (var id in areaIds)
            {
                if (doc.GetElement(ToId(id)) is not Area area)
                    return Failed($"Area {id} no longer exists in the project. Click Refresh.");
                areas.Add(area);
            }

            var missing = FindKey(doc, scheduleName, codeParam, code, out var keyParamName, out var rowId);
            if (missing != null && !runSetupFirst)
                return new UsageAssignResult(UsageAssignStatus.KeysMissing, missing);

            using var group = new TransactionGroup(doc, txName);
            group.Start();
            try
            {
                if (missing != null)
                {
                    var (ok, message) = UsageKeyScheduleBuilder.EnsureUsageKeySchedules(doc);
                    if (!ok)
                    {
                        group.RollBack();
                        return Failed(message);
                    }

                    missing = FindKey(doc, scheduleName, codeParam, code, out keyParamName, out rowId);
                    if (missing != null)
                    {
                        group.RollBack();
                        return Failed("Setup Usage Keys ran, but the key is still missing: " + missing);
                    }
                }

                using (var tx = new Transaction(doc, txName))
                {
                    tx.Start();
                    foreach (var area in areas)
                    {
                        var keyParam = area.LookupParameter(keyParamName!)
                            ?? throw new InvalidOperationException(
                                $"Areas have no \"{keyParamName}\" parameter. Run Setup Usage Keys.");
                        keyParam.Set(rowId!);
                    }

                    if (code == null)
                    {
                        // A cleared key may leave its last driven value behind on the area.
                        doc.Regenerate();
                        foreach (var area in areas)
                        {
                            var p = area.LookupParameter(codeParam);
                            if (p != null && !p.IsReadOnly && !string.IsNullOrEmpty(p.AsString()))
                                p.Set("");
                        }
                    }

                    foreach (var area in areas)
                        foreach (var name in legacyParams)
                            Blank(area.LookupParameter(name));

                    tx.Commit();
                }

                group.Assimilate();
                return new UsageAssignResult(UsageAssignStatus.Done, "");
            }
            catch (Exception ex)
            {
                if (group.HasStarted()) group.RollBack();
                return Failed(ex.Message);
            }
        }

        /// <summary>Finds the key schedule's key parameter name and the row id for
        /// <paramref name="code"/> (<see cref="ElementId.InvalidElementId"/> for none). Returns
        /// null when found, else the message for the window's Run-setup question.</summary>
        private static string? FindKey(Document doc, string scheduleName, string codeParam, int? code,
            out string? keyParamName, out ElementId? rowId)
        {
            keyParamName = null;
            rowId = null;

            var schedule = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => v.Name == scheduleName && v.Definition.IsKeySchedule &&
                                     v.Definition.CategoryId == new ElementId(BuiltInCategory.OST_Areas));
            if (schedule == null)
                return $"This project has no \"{scheduleName}\" key schedule yet. Run Setup Usage Keys now, then set the code?";

            keyParamName = schedule.KeyScheduleParameterName;
            if (code == null)
            {
                rowId = ElementId.InvalidElementId;
                return null;
            }

            var codeText = code.Value.ToString(CultureInfo.InvariantCulture);
            var rows = new FilteredElementCollector(doc, schedule.Id).ToElements();
            var row = rows.FirstOrDefault(r => r.LookupParameter(codeParam)?.AsString()?.Trim() == codeText)
                ?? rows.FirstOrDefault(r => NameStartsWithCode(UsageKeyScheduleBuilder.KeyRowName(r), codeText));
            if (row == null)
                return $"The \"{scheduleName}\" key schedule has no row for code {codeText}. Run Setup Usage Keys now to add it, then set the code?";

            rowId = row.Id;
            return null;
        }

        /// <summary>"119" matches "119", "119 - …" and "119-…", never "1190 - …".</summary>
        private static bool NameStartsWithCode(string keyName, string codeText)
        {
            var name = keyName.Trim();
            return name.StartsWith(codeText, StringComparison.Ordinal) &&
                   (name.Length == codeText.Length || !char.IsDigit(name[codeText.Length]));
        }

        /// <summary>Empties an old office-template parameter. Text → "", numbers → 0, which the
        /// extractor reads as unset.</summary>
        private static void Blank(Parameter? p)
        {
            if (p == null || p.IsReadOnly) return;
            switch (p.StorageType)
            {
                case StorageType.String:
                    if (!string.IsNullOrEmpty(p.AsString())) p.Set("");
                    break;
                case StorageType.Integer:
                    if (p.AsInteger() != 0) p.Set(0);
                    break;
                case StorageType.Double:
                    if (p.AsDouble() != 0) p.Set(0.0);
                    break;
            }
        }

        private static UsageAssignResult Failed(string message) =>
            new UsageAssignResult(UsageAssignStatus.Failed, message);

        // Revit 2023 (KKarea) only has the int ctor; area element ids fit in 32 bits there.
#if REVIT2023
        private static ElementId ToId(long id) => new ElementId((int)id);
#else
        private static ElementId ToId(long id) => new ElementId(id);
#endif
    }
}
