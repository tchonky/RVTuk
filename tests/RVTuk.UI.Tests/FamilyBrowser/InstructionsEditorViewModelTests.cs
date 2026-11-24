using System;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.UI.FamilyBrowser.ViewModels;
using RVTuk.UI.Shared.Controls;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    // The editor must never overwrite stored instructions it could not read, and a failed
    // serialisation must write nothing. Runs on every framework: xunit.runner.json switches shadow
    // copy off, so SQLite finds e_sqlite3.dll under the test folder's runtimes\win-x64\native.
    public class InstructionsEditorViewModelTests : IDisposable
    {
        private const string Broken =
            "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><NoSuchElement /></FlowDocument>";

        private readonly string _folder;

        public InstructionsEditorViewModelTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "rvtuk_editor_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { /* best effort */ }
        }

        private static string Good() => RichTextBoxHelper.SerializeDocument(InstructionFixtures.SampleDocument())!;

        private (BrowserRepository Repo, long Id, InstructionsEditorViewModel Vm) Arrange(string? stored, string? tags = null)
        {
            var db = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".db");
            long id;
            using (var index = new IndexRepository(db))
                id = index.InsertFamily("Doors/A.rfa", "A.rfa");
            var repo = new BrowserRepository(db);
            repo.SaveInstructionsXaml(id, stored);
            if (tags != null) repo.SaveTags(id, tags);
            var vm = new InstructionsEditorViewModel(id, "A.rfa", Path.Combine(_folder, "A.rfa"), repo);
            return (repo, id, vm);
        }

        [Fact]
        public void UnreadableRow_IsFlagged() => Sta.Run(() =>
        {
            foreach (var stored in new string?[] { Good(), null, "" })
            {
                var (repo, _, vm) = Arrange(stored);
                using (repo) Assert.False(vm.InstructionsUnreadable);
            }
            var (brokenRepo, _, brokenVm) = Arrange(Broken);
            using (brokenRepo) Assert.True(brokenVm.InstructionsUnreadable);
        });

        [Fact]
        public void UnreadableRow_SaveKeepsTheTextAndSavesTheTags() => Sta.Run(() =>
        {
            var (repo, id, vm) = Arrange(Broken);
            using (repo)
            {
                vm.TagsText = "door";
                Assert.True(vm.ExecuteSave(Good()));
                Assert.Equal(Broken, repo.GetInstructionsXaml(id));
                Assert.Equal("door", repo.GetTags(id));
            }
        });

        [Fact]
        public void ReadableRow_SaveWritesTheEditorsText() => Sta.Run(() =>
        {
            var (repo, id, vm) = Arrange(null);
            using (repo)
            {
                var good = Good();
                Assert.True(vm.ExecuteSave(good));
                Assert.Equal(good, repo.GetInstructionsXaml(id));
            }
        });

        [Fact]
        public void BuiltInTags_AreNeverStored() => Sta.Run(() =>
        {
            // Project, Library and Fix requested are worked out from the family's state; a typed
            // "Project" stored as a tag would make the family look loaded when it isn't.
            var (repo, id, vm) = Arrange(null);
            using (repo)
            {
                vm.TagsText = "interior, Project, LIBRARY, fix requested, דלת פנים";
                Assert.True(vm.ExecuteSave(Good()));
                Assert.Equal("interior, דלת פנים", repo.GetTags(id));

                vm.TagsText = "Project";
                Assert.True(vm.ExecuteSave(Good()));
                Assert.Null(repo.GetTags(id));
            }
        });

        [Fact]
        public void EditorCouldNotSerialise_NothingIsWritten() => Sta.Run(() =>
        {
            var good = Good();
            var (repo, id, vm) = Arrange(good, "old");
            using (repo)
            {
                vm.TagsText = "new";
                Assert.False(vm.ExecuteSave(null));
                Assert.Equal(good, repo.GetInstructionsXaml(id));
                Assert.Equal("old", repo.GetTags(id));
            }
        });
    }
}
