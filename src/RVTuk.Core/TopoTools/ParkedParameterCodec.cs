using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// The parked parameters as one Extensible Storage string:
    /// <c>kind|key|storage|value;kind|key|storage|value</c>, with the kind as B, S or P and the
    /// storage as I, D, T or E.
    ///
    /// Keys and values are free text, and a project parameter's name or a Comments value can hold
    /// anything, including the separators. So <c>%</c>, <c>|</c> and <c>;</c> are escaped as
    /// <c>%25</c>, <c>%7C</c> and <c>%3B</c>, and nothing else is touched. Hebrew is stored as
    /// the UTF-16 it already is. Decoding never throws: a damaged entry is skipped, so a bad
    /// record costs the parked values and never the conversion.
    /// </summary>
    public static class ParkedParameterCodec
    {
        public static string Encode(IReadOnlyList<ParkedParameter> entries)
        {
            if (entries == null) return "";

            return string.Join(";", entries.Select(entry =>
                KindCode(entry.KeyKind) + "|" + Escape(entry.Key) + "|" +
                StorageCode(entry.Storage) + "|" + Escape(entry.Value)));
        }

        public static IReadOnlyList<ParkedParameter> Decode(string? encoded)
        {
            var entries = new List<ParkedParameter>();
            if (string.IsNullOrEmpty(encoded)) return entries;

            foreach (var text in encoded!.Split(';'))
            {
                var fields = text.Split('|');
                if (fields.Length != 4) continue;
                if (!TryKind(fields[0], out var kind)) continue;
                if (!TryStorage(fields[2], out var storage)) continue;

                var key = Unescape(fields[1]);
                if (string.IsNullOrEmpty(key)) continue;

                var value = Unescape(fields[3]);
                if (value == null) continue;

                entries.Add(new ParkedParameter(kind, key!, storage, value));
            }

            return entries;
        }

        private static string KindCode(ParkedKeyKind kind) => kind switch
        {
            ParkedKeyKind.BuiltIn => "B",
            ParkedKeyKind.Shared => "S",
            _ => "P",
        };

        private static bool TryKind(string code, out ParkedKeyKind kind)
        {
            switch (code)
            {
                case "B": kind = ParkedKeyKind.BuiltIn; return true;
                case "S": kind = ParkedKeyKind.Shared; return true;
                case "P": kind = ParkedKeyKind.Project; return true;
                default: kind = default; return false;
            }
        }

        private static string StorageCode(ParkedStorage storage) => storage switch
        {
            ParkedStorage.Integer => "I",
            ParkedStorage.Double => "D",
            ParkedStorage.String => "T",
            _ => "E",
        };

        private static bool TryStorage(string code, out ParkedStorage storage)
        {
            switch (code)
            {
                case "I": storage = ParkedStorage.Integer; return true;
                case "D": storage = ParkedStorage.Double; return true;
                case "T": storage = ParkedStorage.String; return true;
                case "E": storage = ParkedStorage.ElementId; return true;
                default: storage = default; return false;
            }
        }

        private static string Escape(string text) =>
            (text ?? "").Replace("%", "%25").Replace("|", "%7C").Replace(";", "%3B");

        /// <returns>Null on any escape this codec never writes.</returns>
        private static string? Unescape(string text)
        {
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '%')
                {
                    result.Append(c);
                    continue;
                }

                if (i + 2 >= text.Length) return null;
                string code = text.Substring(i + 1, 2);
                if (code == "25") result.Append('%');
                else if (code == "7C") result.Append('|');
                else if (code == "3B") result.Append(';');
                else return null;
                i += 2;
            }
            return result.ToString();
        }
    }
}
