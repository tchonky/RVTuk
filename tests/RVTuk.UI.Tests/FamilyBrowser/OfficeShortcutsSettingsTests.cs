using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using RVTuk.Core.FamilyBrowser.Config;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.FamilyBrowser.ViewModels;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    // The Settings panel's two shortcut buttons, with the message boxes answered by the test.
    public class OfficeShortcutsSettingsTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rvtuk_keysui_" + Guid.NewGuid().ToString("N"));
        private readonly List<string> _messages = new();
        private const string Export = "<Shortcuts><ShortcutItem CommandId=\"ID_OBJECTS_WALL\" Shortcuts=\"WA\" /></Shortcuts>";

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
        }

        private ConfigViewModel Arrange(bool readOnly, MessageBoxResult answer = MessageBoxResult.OK) =>
            new(new AppConfig { LibraryFolderPath = Path.Combine(_root, "Library") }, (_, __) => { })
            {
                IsReadOnlyLibrary = readOnly,
                UserRevitRoot = Path.Combine(_root, "Revit"),
                AnswerMessage = (text, _) => { _messages.Add(text); return answer; },
            };

        private string UserFile(int year, string? content)
        {
            var folder = Path.Combine(_root, "Revit", "Autodesk Revit " + year);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, OfficeShortcuts.FileName);
            if (content != null) File.WriteAllText(file, content);
            return file;
        }

        [Fact]
        public void Publish_ThenInstall_ReplacesAUsersShortcuts()
        {
            UserFile(2024, Export);
            Arrange(readOnly: false).PublishShortcutsCommand.Execute(null);
            Assert.True(File.Exists(OfficeShortcuts.OfficeFile(Path.Combine(_root, "Library"))));

            File.WriteAllText(UserFile(2024, null), "<Shortcuts><ShortcutItem CommandId=\"ID_OBJECTS_WALL\" Shortcuts=\"WW\" /></Shortcuts>");
            var viewer = Arrange(readOnly: true);
            Assert.True(viewer.InstallShortcutsCommand.CanExecute(null));
            Assert.False(viewer.PublishShortcutsCommand.CanExecute(null)); // only the maintainer publishes
            viewer.InstallShortcutsCommand.Execute(null);

            Assert.Equal(Export, File.ReadAllText(UserFile(2024, null)));
            Assert.Contains(_messages, m => m.StartsWith("Installed the office keyboard shortcuts for Revit 2024"));
        }

        [Fact]
        public void Install_Cancelled_ChangesNothing()
        {
            UserFile(2024, Export);
            Arrange(readOnly: false).PublishShortcutsCommand.Execute(null);
            File.WriteAllText(UserFile(2024, null), "<Shortcuts><ShortcutItem Shortcuts=\"WW\" /></Shortcuts>");

            Arrange(readOnly: true, answer: MessageBoxResult.Cancel).InstallShortcutsCommand.Execute(null);

            Assert.Contains("WW", File.ReadAllText(UserFile(2024, null)));
        }

        [Fact]
        public void Install_WithNothingPublished_SaysSo()
        {
            UserFile(2024, Export);
            Arrange(readOnly: true).InstallShortcutsCommand.Execute(null);
            Assert.StartsWith("There are no office keyboard shortcuts to install yet.", Assert.Single(_messages));
            Assert.Equal(Export, File.ReadAllText(UserFile(2024, null)));
        }
    }
}
