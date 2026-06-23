using System;
using System.IO;

#if REVIT2024
using System.Runtime.Serialization.Json;
using System.Text;
#else
using System.Text.Json;
#endif

namespace RVTuk.Core.Config
{
    public static class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "Revit", "Addins", "RVTuk");

        public static string ConfigFilePath => Path.Combine(ConfigDir, "config.json");

        public static AppConfig LoadConfig()
        {
            if (!File.Exists(ConfigFilePath))
                return new AppConfig();

            try
            {
                var json = File.ReadAllText(ConfigFilePath);
#if REVIT2024
                var bytes = Encoding.UTF8.GetBytes(json);
                using var ms = new MemoryStream(bytes);
                return (AppConfig?)new DataContractJsonSerializer(typeof(AppConfig)).ReadObject(ms) ?? new AppConfig();
#else
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
#endif
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static void SaveConfig(AppConfig config)
        {
            Directory.CreateDirectory(ConfigDir);
#if REVIT2024
            using var ms = new MemoryStream();
            new DataContractJsonSerializer(typeof(AppConfig)).WriteObject(ms, config);
            File.WriteAllText(ConfigFilePath, Encoding.UTF8.GetString(ms.ToArray()));
#else
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
#endif
        }

        public static bool IsConfigured(AppConfig config) =>
            !string.IsNullOrWhiteSpace(config.LibraryFolderPath);
    }
}
