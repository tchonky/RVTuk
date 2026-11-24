using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.FamilyBrowser.ViewModels;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    // The search box as the user drives it: ticking categories (OR), adding tags (AND),
    // Backspace and the counts in the dropdown. The rule itself is tested in Core's FamilyFilterTests.
    public class FamilyBrowserSearchTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "rvtuk_search_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { /* best effort */ }
        }

        private FamilyBrowserViewModel Arrange()
        {
            Directory.CreateDirectory(_folder);
            var db = Path.Combine(_folder, "lib.db");
            using (var index = new IndexRepository(db))
            {
                void Add(string path, string? category)
                {
                    var id = index.InsertFamily(path, Path.GetFileName(path));
                    if (category != null) index.UpdateFamilyMetadata(id, category, new List<ParameterModel>(), null);
                }
                Add("Doors/Door_Single.rfa", "Doors");
                Add("Doors/Door_Double.rfa", "Doors");
                Add("Windows/Window_Sliding.rfa", "Windows");
                Add("Furniture/Chair.rfa", "Furniture");
                Add("Misc/Unscanned.rfa", null);
            }
            var repo = new BrowserRepository(db);
            repo.SaveTags(repo.GetAllFamilies().Single(f => f.FileName == "Door_Single.rfa").Id, "interior, דלת פנים");
            repo.SaveTags(repo.GetAllFamilies().Single(f => f.FileName == "Window_Sliding.rfa").Id, "interior");
            // No library folder: the requests part stays off, which this test doesn't need.
            return new FamilyBrowserViewModel(new AppConfig(), repo, null!, null!, null!, null!, null!, null!, null!, null!);
        }

        private static string[] Shown(FamilyBrowserViewModel vm) => vm.FilteredItems.Select(i => i.DisplayName).ToArray();

        private static CategoryFilterOption Category(FamilyBrowserViewModel vm, string name) =>
            vm.CategoryOptions.Single(o => o.Name == name);

        [Fact]
        public void NothingChosen_ShowsEveryFamily_AndNoCategoryIsTicked() => Sta.Run(() =>
        {
            using var vm = Arrange();
            Assert.Equal(5, vm.FilteredItems.Count);
            Assert.All(vm.CategoryOptions, o => Assert.False(o.IsSelected));
            Assert.Empty(vm.FilterChips);
            Assert.False(vm.HasAnyFilter);
        });

        [Fact]
        public void TickedCategories_AreOr_AndShowAsChips() => Sta.Run(() =>
        {
            using var vm = Arrange();
            Category(vm, "Doors").IsSelected = true;
            Category(vm, "Windows").IsSelected = true;

            Assert.Equal(new[] { "Door_Double", "Door_Single", "Window_Sliding" }, Shown(vm));
            Assert.Equal(new[] { "Doors", "Windows" }, vm.FilterChips.Select(c => c.Value));
            Assert.True(vm.FilterChips.All(c => c.IsCategory));
            Assert.True(vm.HasSelectedCategories);
        });

        [Fact]
        public void Tags_AreAnd_AndAddingOneClearsTheTypedWord() => Sta.Run(() =>
        {
            using var vm = Arrange();
            vm.SearchText = "inter";
            Assert.True(vm.AddFirstSuggestion()); // "interior", the only match
            Assert.Equal(string.Empty, vm.SearchText);
            Assert.Equal(new[] { "Door_Single", "Window_Sliding" }, Shown(vm));

            vm.AddTagFilter(BuiltInTags.Library);
            Assert.Equal(new[] { "Door_Single", "Window_Sliding" }, Shown(vm));

            Category(vm, "Doors").IsSelected = true;
            Assert.Equal(new[] { "Door_Single" }, Shown(vm));
        });

        [Fact]
        public void HebrewTag_IsSuggestedAndFilters() => Sta.Run(() =>
        {
            using var vm = Arrange();
            vm.SearchText = "דלת";
            Assert.Contains(vm.TagSuggestions, s => s.Name == "דלת פנים" && s.IsFirst);
            Assert.True(vm.AddFirstSuggestion());
            Assert.Equal(new[] { "Door_Single" }, Shown(vm));
        });

        [Fact]
        public void Backspace_TakesTheLastChipBack_AndUnticksItsCategory() => Sta.Run(() =>
        {
            using var vm = Arrange();
            vm.AddTagFilter("interior");
            Category(vm, "Doors").IsSelected = true;

            Assert.True(vm.RemoveLastChip());
            Assert.False(Category(vm, "Doors").IsSelected);
            Assert.Equal(new[] { "interior" }, vm.FilterChips.Select(c => c.Value));

            Assert.True(vm.RemoveLastChip());
            Assert.False(vm.RemoveLastChip());
            Assert.Equal(5, vm.FilteredItems.Count);
        });

        [Fact]
        public void Dropdown_CountsFollowTheTags_AndFilterByTheTypedText() => Sta.Run(() =>
        {
            using var vm = Arrange();
            Assert.Equal(2, Category(vm, "Doors").Count);
            vm.AddTagFilter("interior");
            Assert.Equal(1, Category(vm, "Doors").Count);
            Assert.Equal(0, Category(vm, "Furniture").Count);

            vm.SearchText = "win";
            Assert.Equal(new[] { "Windows" }, vm.VisibleCategoryOptions.Select(o => o.Name));
        });

        [Fact]
        public void BuiltInTags_AreOffered_ProjectDisabledUntilACompare() => Sta.Run(() =>
        {
            using var vm = Arrange();
            var project = vm.TagSuggestions.Single(s => s.Name == BuiltInTags.Project);
            var library = vm.TagSuggestions.Single(s => s.Name == BuiltInTags.Library);
            Assert.False(project.IsEnabled);   // nothing compared with a project yet
            Assert.Equal(5, library.Count);
            Assert.DoesNotContain(vm.TagSuggestions, s => s.IsFirst); // Enter adds nothing before typing
        });

        [Fact]
        public void SelectedFamily_ShowsItsOwnTagsThenTheBuiltInOnes() => Sta.Run(() =>
        {
            using var vm = Arrange();
            vm.SelectedItem = vm.FilteredItems.Single(i => i.DisplayName == "Door_Single");
            Assert.Equal(new[] { "interior", "דלת פנים", BuiltInTags.Library }, vm.SelectedTagChips.Select(c => c.Name));
            Assert.True(vm.SelectedTagChips.Last().IsBuiltIn);
        });

        [Fact]
        public void ClearSearch_RemovesWordsChipsAndTicks() => Sta.Run(() =>
        {
            using var vm = Arrange();
            vm.SearchText = "door";
            vm.AddTagFilter("interior");
            Category(vm, "Windows").IsSelected = true;
            vm.ClearSearchCommand.Execute(null);
            Assert.False(vm.HasAnyFilter);
            Assert.All(vm.CategoryOptions, o => Assert.False(o.IsSelected));
            Assert.Equal(5, vm.FilteredItems.Count);
        });
    }
}
