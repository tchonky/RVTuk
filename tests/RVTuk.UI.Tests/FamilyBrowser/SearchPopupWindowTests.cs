using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.FamilyBrowser.Views;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    // The real window, shown: a click in the dropdown must leave it open. The dropdown is a window
    // of its own, but WPF still routes its mouse events up through the Popup to the Family
    // Browser window, so the window's "click elsewhere closes it" handler sees them too
    // (2026-11-25: ticking a category closed the list on mouse-down, before the tick).
    public class SearchPopupWindowTests : IDisposable
    {
        private readonly string _library = Path.Combine(Path.GetTempPath(), "rvtuk_popup_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_library)) Directory.Delete(_library, true); } catch { /* best effort */ }
        }

        private FamilyBrowserWindow ShowWindow()
        {
            Directory.CreateDirectory(Path.Combine(_library, ".DB"));
            using (var index = new IndexRepository(Path.Combine(_library, ".DB", "RVTuk.db")))
            {
                var id = index.InsertFamily("Furniture/Desk.rfa", "Desk.rfa");
                index.UpdateFamilyMetadata(id, "Furniture", new List<ParameterModel>(), null);
            }
            var window = new FamilyBrowserWindow(new AppConfig { LibraryFolderPath = _library },
                null!, null!, null!, null!, null!, null!, null!, _ => new Dictionary<string, byte[]>())
            {
                Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false,
            };
            window.Show();
            return window;
        }

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        private static void MouseDownOn(UIElement target)
        {
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseDownEvent });
        }

        [Fact]
        public void MouseDownOnACategoryInTheDropdown_LeavesItOpen() => Sta.Run(() =>
        {
            var window = ShowWindow();
            try
            {
                window.ViewModel.IsSearchPopupOpen = true;
                Pump();
                var content = (FrameworkElement)window.FindName("SearchPopupContent");
                var category = Descendants(content).OfType<CheckBox>().First();

                MouseDownOn(category);

                Assert.True(window.ViewModel.IsSearchPopupOpen);
            }
            finally { window.ViewModel?.Dispose(); window.Close(); }
        });

        [Fact]
        public void MouseDownElsewhereInTheWindow_ClosesIt() => Sta.Run(() =>
        {
            var window = ShowWindow();
            try
            {
                window.ViewModel.IsSearchPopupOpen = true;
                Pump();
                var list = Descendants(window).OfType<ListBox>().First();

                MouseDownOn(list);

                Assert.False(window.ViewModel.IsSearchPopupOpen);
            }
            finally { window.ViewModel?.Dispose(); window.Close(); }
        });
    }
}
