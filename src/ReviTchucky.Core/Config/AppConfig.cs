using System.IO;

namespace ReviTchucky.Core.Config
{
    public class AppConfig
    {
        public string LibraryFolderPath { get; set; } = string.Empty;

        // Derived — never stored separately; always lives inside the library folder.
        public string DatabasePath => Path.Combine(LibraryFolderPath, ".Setup", "ReviTchucky.db");
    }
}
