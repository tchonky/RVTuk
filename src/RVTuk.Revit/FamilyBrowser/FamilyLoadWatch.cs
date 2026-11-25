using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RVTuk.Core.Shared.Config;

namespace RVTuk.Revit.FamilyBrowser
{
    /// <summary>
    /// Notes where every family loaded into a project came from — Revit's own Load Family and
    /// drag-and-drop too, not only the Family Browser's Load — so ⟳ can tell later whether the
    /// library file changed since (<see cref="FamilySourceStamp"/>). A family loaded from the
    /// library gets the file's size and date; one loaded from anywhere else (or saved back from
    /// the Family Editor) has any old note removed, so it shows as "can't tell" rather than up to
    /// date. The note is written at the next idle moment, in a transaction of its own: whether
    /// Revit lets a FamilyLoadedIntoDocument handler change the document directly is not relied on.
    /// </summary>
    public static class FamilyLoadWatch
    {
        private sealed class Loaded
        {
            public Document Doc = null!;
            public ElementId FamilyId = ElementId.InvalidElementId;
            public string FamilyName = string.Empty;
            public string? Path;
        }

        private static readonly List<Loaded> Pending = new();
        private static UIControlledApplication? _app;
        private static bool _idlingHooked;

        /// <summary>The Family Browser's own load notes the file itself, in its transaction.</summary>
        public static int SuppressDepth { get; set; }

        public static void Start(UIControlledApplication app)
        {
            try
            {
                _app = app;
                app.ControlledApplication.FamilyLoadedIntoDocument += OnFamilyLoaded;
            }
            catch (Exception ex) { RVTuk.Revit.Application.LogDiagnostic($"Family source watch could not start: {ex}"); }
        }

        private static void OnFamilyLoaded(object? sender, FamilyLoadedIntoDocumentEventArgs e)
        {
            try
            {
                if (SuppressDepth > 0 || e.Status != RevitAPIEventStatus.Succeeded) return;
                var doc = e.Document;
                if (doc == null || doc.IsFamilyDocument) return; // nested families: not the library's business
                var id = e.NewFamilyId != null && e.NewFamilyId != ElementId.InvalidElementId ? e.NewFamilyId : e.OriginalFamilyId;
                Pending.Add(new Loaded { Doc = doc, FamilyId = id ?? ElementId.InvalidElementId, FamilyName = e.FamilyName ?? string.Empty, Path = e.FamilyPath });
                HookIdling();
            }
            catch (Exception ex) { RVTuk.Revit.Application.LogDiagnostic($"Family source watch failed: {ex}"); }
        }

        private static void HookIdling()
        {
            if (_idlingHooked || _app == null) return;
            _app.Idling += OnIdling;
            _idlingHooked = true;
        }

        private static void OnIdling(object? sender, IdlingEventArgs e)
        {
            if (_app != null) _app.Idling -= OnIdling;
            _idlingHooked = false;
            var work = Pending.ToList();
            Pending.Clear();
            string? libraryRoot = null;
            try { libraryRoot = ConfigManager.LoadConfig().LibraryFolderPath; } catch { }

            foreach (var group in work.GroupBy(w => w.Doc))
            {
                var doc = group.Key;
                try
                {
                    if (!doc.IsValidObject || doc.IsReadOnly) continue;
                    if (doc.IsModifiable) { Pending.AddRange(group); HookIdling(); continue; } // still mid-transaction
                    Note(doc, group.ToList(), libraryRoot);
                }
                catch (Exception ex) { RVTuk.Revit.Application.LogDiagnostic($"Family source note failed: {ex}"); }
            }
        }

        private static void Note(Document doc, List<Loaded> loads, string? libraryRoot)
        {
            var changes = new List<(Family Family, FileInfo? File, string? Relative)>();
            foreach (var load in loads)
            {
                var family = doc.GetElement(load.FamilyId) as Family
                    ?? new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                        .FirstOrDefault(f => string.Equals(f.Name, load.FamilyName, StringComparison.Ordinal));
                if (family == null) continue;
                // Someone else's family in a workshared model: leave it (it shows as "can't tell").
                if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, family.Id) == CheckoutStatus.OwnedByOtherUser)
                    continue;
                var relative = FamilySourceStamp.LibraryRelativePath(load.Path, libraryRoot);
                var file = relative != null && File.Exists(load.Path) ? new FileInfo(load.Path!) : null;
                if (file == null && !FamilySourceStamp.TryRead(family, out _, out _)) continue; // nothing to note or clear
                changes.Add((family, file, relative));
            }
            if (changes.Count == 0) return;

            using var tx = new Transaction(doc, "RVTuk: note where families come from");
            var options = tx.GetFailureHandlingOptions();
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            if (tx.Start() != TransactionStatus.Started) return;
            foreach (var (family, file, relative) in changes)
            {
                if (file != null) FamilySourceStamp.Write(family, file, relative!);
                else FamilySourceStamp.Clear(family);
            }
            tx.Commit();
        }
    }
}
