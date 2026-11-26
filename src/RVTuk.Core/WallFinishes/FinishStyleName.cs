using System;

namespace RVTuk.Core.WallFinishes
{
    /// <summary>
    /// Wall Finishes draws each finish on a line style of its own, named after the material, so
    /// the style is the record of which finish a line shows. These are the naming rules.
    /// </summary>
    public static class FinishStyleName
    {
        public const string Prefix = "_DP-Mat ";

        // Revit refuses these in a subcategory name.
        private const string Forbidden = "{}[]|;<>?`~\\:";

        public static string For(string materialName)
        {
            var chars = materialName.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Forbidden.IndexOf(chars[i]) >= 0) chars[i] = '-';
            return Prefix + new string(chars).Trim();
        }

        public static bool IsFinishStyle(string styleName) =>
            styleName.StartsWith(Prefix, StringComparison.Ordinal);

        public static string MaterialOf(string styleName) =>
            IsFinishStyle(styleName) ? styleName.Substring(Prefix.Length) : styleName;
    }
}
