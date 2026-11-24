using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RVTuk.UI.FamilyBrowser.Views
{
    /// <summary>
    /// Outline icons from Tabler Icons 3.19.0 (https://tabler.io/icons, MIT licence, see
    /// THIRD-PARTY-NOTICES.md): each icon's &lt;path d&gt; strings joined, drawn on a 24×24
    /// canvas with a 2 px round-capped stroke and no fill.
    /// </summary>
    public static class TablerIcons
    {
        public static readonly IReadOnlyDictionary<string, string> PathData = new Dictionary<string, string>
        {
            ["door"] = "M14 12v.01 M3 21h18 M6 21v-16a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v16",
            ["window"] = "M12 3c-3.866 0 -7 3.272 -7 7v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1 -1v-10c0 -3.728 -3.134 -7 -7 -7z M5 13l14 0 M12 3l0 18",
            ["cube"] = "M21 16.008v-8.018a1.98 1.98 0 0 0 -1 -1.717l-7 -4.008a2.016 2.016 0 0 0 -2 0l-7 4.008c-.619 .355 -1 1.01 -1 1.718v8.018c0 .709 .381 1.363 1 1.717l7 4.008a2.016 2.016 0 0 0 2 0l7 -4.008c.619 -.355 1 -1.01 1 -1.718z M12 22v-10 M12 12l8.73 -5.04 M3.27 6.96l8.73 5.04",
            ["layout"] = "M4 4m0 2a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v1a2 2 0 0 1 -2 2h-2a2 2 0 0 1 -2 -2z M4 13m0 2a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-2a2 2 0 0 1 -2 -2z M14 4m0 2a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-2a2 2 0 0 1 -2 -2z",
            ["tool"] = "M7 10h3v-3l-3.5 -3.5a6 6 0 0 1 8 8l6 6a2 2 0 0 1 -3 3l-6 -6a6 6 0 0 1 -8 -8l3.5 3.5",
            ["bath"] = "M4 12h16a1 1 0 0 1 1 1v3a4 4 0 0 1 -4 4h-10a4 4 0 0 1 -4 -4v-3a1 1 0 0 1 1 -1z M6 12v-7a2 2 0 0 1 2 -2h3v2.25 M4 21l1 -1.5 M20 21l-1 -1.5",
            ["tag"] = "M7.5 7.5m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M3 6v5.172a2 2 0 0 0 .586 1.414l7.71 7.71a2.41 2.41 0 0 0 3.408 0l5.592 -5.592a2.41 2.41 0 0 0 0 -3.408l-7.71 -7.71a2 2 0 0 0 -1.414 -.586h-5.172a3 3 0 0 0 -3 3z",
            ["note"] = "M13 20l7 -7 M13 20v-6a1 1 0 0 1 1 -1h6v-7a2 2 0 0 0 -2 -2h-12a2 2 0 0 0 -2 2v12a2 2 0 0 0 2 2h7",
            ["elevator"] = "M5 4m0 1a1 1 0 0 1 1 -1h12a1 1 0 0 1 1 1v14a1 1 0 0 1 -1 1h-12a1 1 0 0 1 -1 -1z M10 10l2 -2l2 2 M10 14l2 2l2 -2",
            ["stairs"] = "M22 5h-5v5h-5v5h-5v5h-5",
            ["fence"] = "M4 12v4h16v-4z M6 16v4h4v-4m0 -4v-6l-2 -2l-2 2v6 M14 16v4h4v-4m0 -4v-6l-2 -2l-2 2v6",
            ["bulb"] = "M3 12h1m8 -9v1m8 8h1m-15.4 -6.4l.7 .7m12.1 -.7l-.7 .7 M9 16a5 5 0 1 1 6 0a3.5 3.5 0 0 0 -1 3a2 2 0 0 1 -4 0a3.5 3.5 0 0 0 -1 -3 M9.7 17l4.6 0",
            ["lamp"] = "M9 20h6 M12 20v-8 M5 12h14l-4 -8h-6z",
            ["building-arch"] = "M3 21l18 0 M4 21v-15a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v15 M9 21v-8a3 3 0 0 1 6 0v8",
            ["droplets"] = "M4.072 20.3a2.999 2.999 0 0 0 3.856 0a3.002 3.002 0 0 0 .67 -3.798l-2.095 -3.227a.6 .6 0 0 0 -1.005 0l-2.098 3.227a3.003 3.003 0 0 0 .671 3.798z M16.072 20.3a2.999 2.999 0 0 0 3.856 0a3.002 3.002 0 0 0 .67 -3.798l-2.095 -3.227a.6 .6 0 0 0 -1.005 0l-2.098 3.227a3.003 3.003 0 0 0 .671 3.798z M10.072 10.3a2.999 2.999 0 0 0 3.856 0a3.002 3.002 0 0 0 .67 -3.798l-2.095 -3.227a.6 .6 0 0 0 -1.005 0l-2.098 3.227a3.003 3.003 0 0 0 .671 3.798z",
            ["air-conditioning"] = "M8 16a3 3 0 0 1 -3 3 M16 16a3 3 0 0 0 3 3 M12 16v4 M3 5m0 2a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v4a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2z M7 13v-3a1 1 0 0 1 1 -1h8a1 1 0 0 1 1 1v3",
            ["sofa"] = "M4 11a2 2 0 0 1 2 2v1h12v-1a2 2 0 1 1 4 0v5a1 1 0 0 1 -1 1h-18a1 1 0 0 1 -1 -1v-5a2 2 0 0 1 2 -2z M4 11v-3a3 3 0 0 1 3 -3h10a3 3 0 0 1 3 3v3 M12 5v9",
            ["desk"] = "M3 6h18 M4 6v13 M20 19v-13 M4 10h16 M15 6v8a2 2 0 0 0 2 2h3",
            ["ruler-2"] = "M17 3l4 4l-14 14l-4 -4z M16 7l-1.5 -1.5 M13 10l-1.5 -1.5 M10 13l-1.5 -1.5 M7 16l-1.5 -1.5",
            ["archive"] = "M3 4m0 2a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v0a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2z M5 8v10a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-10 M10 12l4 0",
            ["plant"] = "M7 15h10v4a2 2 0 0 1 -2 2h-6a2 2 0 0 1 -2 -2v-4z M12 9a6 6 0 0 0 -6 -6h-3v2a6 6 0 0 0 6 6h3 M12 11a6 6 0 0 1 6 -6h3v1a6 6 0 0 1 -6 6h-3 M12 15l0 -6",
            ["user"] = "M8 7a4 4 0 1 0 8 0a4 4 0 0 0 -8 0 M6 21v-2a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v2",
            ["mountain"] = "M3 20h18l-6.921 -14.612a2.3 2.3 0 0 0 -4.158 0l-6.921 14.612z M7.5 11l2 2.5l2.5 -2.5l2 3l2.5 -2",
            ["car"] = "M7 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M17 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M5 17h-2v-6l2 -5h9l4 5h1a2 2 0 0 1 2 2v4h-2m-4 0h-6m-6 -6h15m-6 0v-5",
            ["plug"] = "M9.785 6l8.215 8.215l-2.054 2.054a5.81 5.81 0 1 1 -8.215 -8.215l2.054 -2.054z M4 20l3.5 -3.5 M15 4l-3.5 3.5 M20 9l-3.5 3.5",
            ["bolt"] = "M13 3l0 7l6 0l-8 11l0 -7l-6 0l8 -11",
            ["wall"] = "M4 4m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z M4 8h16 M20 12h-16 M4 16h16 M9 4v4 M14 8v4 M8 12v4 M16 12v4 M11 16v4",
            ["vector-spline"] = "M17 3m0 1a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1z M3 17m0 1a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1z M17 5c-6.627 0 -12 5.373 -12 12",
            ["box"] = "M12 3l8 4.5l0 9l-8 4.5l-8 -4.5l0 -9l8 -4.5 M12 12l8 -4.5 M12 12l0 9 M12 12l-8 -4.5",
            ["lock"] = "M5 13a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v6a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-6z M11 16a1 1 0 1 0 2 0a1 1 0 0 0 -2 0 M8 11v-4a4 4 0 1 1 8 0v4",
            ["plus"] = "M12 5l0 14 M5 12l14 0",
            ["inbox"] = "M4 4m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z M4 13h3l3 3h4l3 -3h3",
            ["paperclip"] = "M15 7l-6.5 6.5a1.5 1.5 0 0 0 3 3l6.5 -6.5a3 3 0 0 0 -6 -6l-6.5 6.5a4.5 4.5 0 0 0 9 9l6.5 -6.5",
            ["check"] = "M5 12l5 5l10 -10",
            ["x"] = "M18 6l-12 12 M6 6l12 12",
            ["search"] = "M10 10m-7 0a7 7 0 1 0 14 0a7 7 0 1 0 -14 0 M21 21l-6 -6",
        };

        // Revit category → icon. Matched to the categories in the office library (Nov 2026);
        // any "… Tags" category gets the tag icon, anything else the generic box.
        public static readonly IReadOnlyDictionary<string, string> CategoryIcon =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Doors"] = "door", ["Windows"] = "window", ["Generic Models"] = "cube",
                ["Title Blocks"] = "layout", ["Specialty Equipment"] = "tool", ["Plumbing Fixtures"] = "bath",
                ["Generic Annotations"] = "note", ["Vertical Circulation"] = "elevator", ["Stairs"] = "stairs",
                ["Railings"] = "fence", ["Lighting Fixtures"] = "bulb", ["Lighting Devices"] = "lamp",
                ["Structural Columns"] = "building-arch", ["Columns"] = "building-arch",
                ["Sprinklers"] = "droplets", ["Mechanical Equipment"] = "air-conditioning",
                ["Furniture"] = "sofa", ["Furniture Systems"] = "desk", ["Detail Items"] = "ruler-2",
                ["Casework"] = "archive", ["Planting"] = "plant", ["Entourage"] = "user", ["Site"] = "mountain",
                ["Parking"] = "car", ["Electrical Fixtures"] = "plug", ["Electrical Equipment"] = "bolt",
                ["Walls"] = "wall", ["Profiles"] = "vector-spline",
            };

        private static readonly Dictionary<string, Geometry> Cache = new();

        /// <summary>The named icon as a frozen geometry (null for an unknown name).</summary>
        public static Geometry? Get(string name)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(name, out var g)) return g;
                if (!PathData.TryGetValue(name, out var data)) return null;
                g = Geometry.Parse(data);
                g.Freeze();
                Cache[name] = g;
                return g;
            }
        }

        public static string IconNameForCategory(string? category)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                if (CategoryIcon.TryGetValue(category!, out var name)) return name;
                if (category!.EndsWith(" Tags", StringComparison.OrdinalIgnoreCase)) return "tag";
            }
            return "box";
        }

        public static Geometry? ForCategory(string? category) => Get(IconNameForCategory(category));
    }

    /// <summary>A fixed icon in XAML: <c>Data="{v:Icon plus}"</c>.</summary>
    [System.Windows.Markup.MarkupExtensionReturnType(typeof(Geometry))]
    public sealed class IconExtension : System.Windows.Markup.MarkupExtension
    {
        public IconExtension() { }
        public IconExtension(string name) => Name = name;
        public string Name { get; set; } = "box";
        public override object? ProvideValue(IServiceProvider serviceProvider) => TablerIcons.Get(Name);
    }

    /// <summary>Category name → its Tabler icon, for a Path's Data in XAML.</summary>
    public sealed class CategoryIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => TablerIcons.ForCategory(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Icon name (the bound value) → its Tabler icon.</summary>
    public sealed class TablerIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => TablerIcons.Get((value as string) ?? "box");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
