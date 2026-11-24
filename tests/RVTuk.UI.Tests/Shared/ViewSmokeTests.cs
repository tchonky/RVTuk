using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RVTuk.Core.DwgExporter;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.DwgExporter.ViewModels;
using RVTuk.UI.DwgExporter.Views;
using RVTuk.UI.FamilyBrowser.ViewModels;
using RVTuk.UI.FamilyBrowser.Views;
using RVTuk.UI.RishuiZamin.ViewModels;
using RVTuk.UI.RishuiZamin.Views;
using RVTuk.UI.Shared.Themes;
using Xunit;

namespace RVTuk.UI.Tests.Shared
{
    // Every view must construct and merge the theme through DarkThemeDictionary, so it keeps
    // working when phase 3 compiles the views into assemblies with other names.
    public class ViewSmokeTests
    {
        [Fact]
        public void EveryView_ConstructsWithTheDarkTheme() => Sta.Run(() =>
        {
            var views = typeof(DarkThemeDictionary).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && (typeof(Window).IsAssignableFrom(t) || typeof(UserControl).IsAssignableFrom(t)))
                .ToList();
            // 10 since the Topo Tools pane gave way to the ribbon edit session; 11 with the Area
            // Calc usage picker; 12 with the Area Calc table.
            Assert.Equal(12, views.Count);

            var folder = Path.Combine(Path.GetTempPath(), "rvtuk_smoke_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var db = Path.Combine(folder, "editor.db");
            long id;
            using (var index = new IndexRepository(db))
                id = index.InsertFamily("Doors/A.rfa", "A.rfa");
            var repo = new BrowserRepository(db);
            var built = new List<FrameworkElement>();
            try
            {
                var special = new Dictionary<Type, Func<FrameworkElement>>
                {
                    [typeof(DwgExportWindow)] = () => new DwgExportWindow(new DwgExportViewModel(
                        new PdfSetupItem[0], new string[0], new SheetSetItem[0], "", new ModelSetupInventory[0], "",
                        null!, null!, null!)),
                    [typeof(RishuiZaminWindow)] = () => new RishuiZaminWindow(new RishuiZaminViewModel(null!, null!, null!)),
                    [typeof(ImageCropWindow)] = () => new ImageCropWindow(
                        BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4)),
                    [typeof(FamilyBrowserWindow)] = () => new FamilyBrowserWindow(
                        new AppConfig { LibraryFolderPath = folder }, null!, null!, null!, null!, null!, null!, null!, null!),
                    [typeof(InstructionsEditorWindow)] = () => new InstructionsEditorWindow(
                        new FamilyBrowserItemViewModel(new FamilyBrowserItem { Id = id, FileName = "A.rfa" }),
                        Path.Combine(folder, "A.rfa"), repo),
                };

                var failures = new List<string>();
                foreach (var type in views)
                {
                    FrameworkElement view;
                    try
                    {
                        if (special.TryGetValue(type, out var make)) view = make();
                        else if (type.GetConstructor(Type.EmptyTypes) != null) view = (FrameworkElement)Activator.CreateInstance(type)!;
                        else { failures.Add($"{type.Name}: no way to construct it"); continue; }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{type.Name}: construction threw {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
                        continue;
                    }
                    built.Add(view);
                    if (!view.Resources.MergedDictionaries.OfType<DarkThemeDictionary>().Any())
                        failures.Add($"{type.Name}: does not merge DarkThemeDictionary");
                    else if (((SolidColorBrush)view.FindResource("Brush.Bg")).Color != Color.FromRgb(0x2B, 0x2B, 0x2B))
                        failures.Add($"{type.Name}: Brush.Bg is not the dark theme's");
                }
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally
            {
                // Never Show() or Close() a view: closing an unshown InstructionsEditorWindow
                // asks about unsaved changes in a message box, and the test hangs.
                foreach (var fb in built.OfType<FamilyBrowserWindow>()) fb.ViewModel?.Dispose();
                repo.Dispose();
                try { Directory.Delete(folder, true); } catch { /* best effort */ }
            }
        });
    }
}
