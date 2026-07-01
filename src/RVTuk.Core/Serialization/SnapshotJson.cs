using System.IO;
using System.Text;

namespace RVTuk.Core.Serialization
{
    /// <summary>
    /// JSON round-trip for snapshot payloads, isolating the multi-target split.
    /// net48 (Revit 2023/24, REVIT2024) has no usable System.Text.Json — its transitive
    /// polyfills clash with Revit's preloaded assemblies — so it uses DataContractJsonSerializer
    /// (hence the [DataContract]/[DataMember] DTOs). net8 (Revit 2025) uses System.Text.Json.
    ///
    /// The DTOs are flat with member-name = property-name, so the two serializers produce
    /// compatible JSON for the shared .Setup\RVTuk.Standards.db across Revit years.
    /// </summary>
    public static class SnapshotJson
    {
        public static string Serialize<T>(T value)
        {
#if REVIT2024
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
#else
            return System.Text.Json.JsonSerializer.Serialize(value);
#endif
        }

        public static T Deserialize<T>(string json)
        {
#if REVIT2024
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(ms)!;
            }
#else
            return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
#endif
        }
    }
}
