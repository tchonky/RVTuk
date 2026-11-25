using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Requests;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.FamilyBrowser.ViewModels;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    // A view-only user's Save to Library becomes a suggestion: Revit saves the family from the
    // project into .Requests (here a fake that writes the file), with its category, for the
    // maintainer to approve. Revit itself is out of the picture, so the save is a stand-in.
    public class FamilyBrowserSuggestionTests : IDisposable
    {
        private readonly string _library = Path.Combine(Path.GetTempPath(), "rvtuk_suggest_" + Guid.NewGuid().ToString("N"));
        private readonly List<string> _savedTo = new();

        public void Dispose()
        {
            try { if (Directory.Exists(_library)) Directory.Delete(_library, true); } catch { /* best effort */ }
        }

        private FamilyBrowserViewModel Arrange(bool canEdit)
        {
            Directory.CreateDirectory(Path.Combine(_library, ".DB"));
            var db = Path.Combine(_library, ".DB", "RVTuk.db");
            using (var index = new IndexRepository(db))
            {
                var id = index.InsertFamily("Furniture/Desk.rfa", "Desk.rfa");
                index.UpdateFamilyMetadata(id, "Furniture", new List<ParameterModel>(), null);
            }
            var snapshot = new ProjectSnapshot
            {
                DocumentKey = "A", Title = "Tower A",
                Families = new[] { new ProjectFamilyInfo { Name = "Reception Desk - Custom", Category = "Furniture" } },
            };
            var vm = new FamilyBrowserViewModel(new AppConfig { LibraryFolderPath = _library }, new BrowserRepository(db),
                () => snapshot, null!, null!, null!, null!, null!,
                (name, path) => { _savedTo.Add(path); File.WriteAllText(path, "rfa of " + name); return (true, null); },
                _ => new Dictionary<string, byte[]>(),
                canEdit: canEdit, currentUser: "noa")
            {
                RequestDialog = _ => new RequestDialogResult { Text = "we use it a lot" },
                // Every question answered Yes ("add it in the planned folder"); every note kept.
                AnswerMessage = (text, _) => { Messages.Add(text); return System.Windows.MessageBoxResult.Yes; },
            };
            return vm;
        }

        private List<string> Messages { get; } = new();

        // The view model finishes background work through the dispatcher: pump it until done.
        private static void PumpUntil(Func<bool> done)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!done())
            {
                if (DateTime.UtcNow > until) throw new TimeoutException("The browser did not finish in time.");
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                System.Threading.Thread.Sleep(10);
            }
        }

        [Fact]
        public void ViewOnlyUser_SuggestsTheModelFamily_AndTheMaintainerSeesIt() => Sta.Run(() =>
        {
            new RequestStore(_library).EnsureFolder(); // the maintainer's browser made it
            using var vm = Arrange(canEdit: false);
            Assert.Equal("Suggest for Library", vm.SaveToLibraryLabel);

            vm.SyncCommand.Execute(null);
            PumpUntil(() => !vm.IsSyncing && vm.FilteredItems.Any(i => i.IsModelOnly));
            vm.SelectedItem = vm.FilteredItems.Single(i => i.IsModelOnly);
            vm.SaveToLibraryCommand.Execute(null);
            PumpUntil(() => !vm.IsSendingRequest && vm.SuggestedFamilyRequests.Count == 1);

            var request = Assert.Single(new RequestStore(_library).LoadOpen());
            Assert.Equal(FamilyRequest.KindSuggestion, request.Kind);
            Assert.Equal("Reception Desk - Custom", request.FamilyName);
            Assert.Equal("Furniture", request.Category);
            Assert.Equal("noa", request.User);
            Assert.Null(request.RelativePath);
            Assert.Equal(request.Id + ".rfa", request.Attachment);
            Assert.Equal(Path.Combine(_library, ".Requests", request.Id + ".rfa"), Assert.Single(_savedTo));

            // The suggester sees it on the family, and can't suggest it twice.
            Assert.True(vm.HasSelectedFamilyRequests);
            Assert.Equal("Suggested for the library", vm.SelectedRequestsTitle);
            Assert.True(vm.SuggestedFamilyRequests[0].CanWithdraw);
            Assert.False(vm.SuggestedFamilyRequests[0].CanApprove);
        });

        [Fact]
        public void Maintainer_ApprovesIntoTheLibrary_UnderTheFamilysOwnName() => Sta.Run(() =>
        {
            // A suggestion already in the folder, as a user's browser left it.
            var store = new RequestStore(_library);
            store.EnsureFolder();
            var r = new FamilyRequest
            {
                Id = RequestStore.NewId("noa", DateTime.UtcNow), Kind = FamilyRequest.KindSuggestion,
                FamilyName = "Reception Desk - Custom", Category = "Furniture", User = "noa",
                CreatedUtc = DateTime.UtcNow.ToString("o"), Text = "",
            };
            r.Attachment = r.Id + ".rfa";
            File.WriteAllText(store.AttachmentPath(r.Id), "rfa");
            store.Save(r);

            using var vm = Arrange(canEdit: true);
            PumpUntil(() => vm.SuggestedFamilyRequests.Count == 1);
            var item = vm.SuggestedFamilyRequests[0];
            Assert.True(item.CanApprove);
            Assert.Equal("Reject", item.DoneLabel);
            Assert.Empty(vm.FixRequests);          // a suggestion is not a fix
            Assert.Empty(vm.NewFamilyRequests);

            // Approve, answering Yes: the planned folder is Furniture, where the library's other
            // desk lives, and the file takes the family's name (not the request id).
            vm.ApproveSuggestionCommand.Execute(item);
            PumpUntil(() => vm.SuggestedFamilyRequests.Count == 0);
            var target = Path.Combine(_library, "Furniture", "Reception Desk - Custom.rfa");
            Assert.True(File.Exists(target));
            Assert.Contains(Messages, m => m.Contains("Furniture") && m.Contains("Yes adds it there"));
            Assert.Contains(Messages, m => m.StartsWith("Added to:") && m.Contains(target));
            Assert.Empty(store.LoadOpen());
            Assert.Equal(1, store.CountDone());
        });

        [Fact]
        public void Suggestion_IsNotSent_WhenRevitCannotSaveTheFamily() => Sta.Run(() =>
        {
            new RequestStore(_library).EnsureFolder();
            Directory.CreateDirectory(Path.Combine(_library, ".DB"));
            var db = Path.Combine(_library, ".DB", "RVTuk.db");
            using (new IndexRepository(db)) { }
            var snapshot = new ProjectSnapshot
            {
                DocumentKey = "A", Title = "Tower A",
                Families = new[] { new ProjectFamilyInfo { Name = "In-place thing", Category = "Generic Models" } },
            };
            using var vm = new FamilyBrowserViewModel(new AppConfig { LibraryFolderPath = _library }, new BrowserRepository(db),
                () => snapshot, null!, null!, null!, null!, null!,
                (_, __) => (false, "In-place families cannot be edited or saved as .rfa files."),
                _ => new Dictionary<string, byte[]>(), canEdit: false, currentUser: "noa")
            {
                RequestDialog = _ => new RequestDialogResult(),
                AnswerMessage = (text, _) => { Messages.Add(text); return System.Windows.MessageBoxResult.OK; },
            };
            vm.SyncCommand.Execute(null);
            PumpUntil(() => !vm.IsSyncing && vm.FilteredItems.Any(i => i.IsModelOnly));
            vm.SelectedItem = vm.FilteredItems.Single(i => i.IsModelOnly);
            vm.SaveToLibraryCommand.Execute(null);
            PumpUntil(() => !vm.IsSendingRequest && Messages.Count > 0);

            Assert.Empty(new RequestStore(_library).LoadOpen());
            Assert.StartsWith("The request was not sent", Messages.Last());
            Assert.Contains("In-place", Messages.Last());
        });
    }
}
