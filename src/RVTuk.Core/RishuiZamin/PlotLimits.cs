using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RVTuk.Core.RishuiZamin
{
    /// <summary>The plot area and the plan's allowed % of plot, as typed in the Area Calc
    /// window's Table tab and kept in the Revit project. Null means not filled in.</summary>
    public sealed record PlotLimits(double? PlotAreaM2, double? PrimaryPercent, double? ServicePercent, double? TotalPercent)
    {
        public static readonly PlotLimits Empty = new PlotLimits(null, null, null, null);

        private static readonly Regex ThousandsGrouped = new Regex(@"^\d{1,3}(,\d{3})+(\.\d+)?$");

        /// <summary>Reads one box: empty → true with null; an invariant number ≥ 0, optionally with
        /// one trailing "%" and with commas only as thousands separators ("2,370.00") → true;
        /// anything else (including "35,5", read by .NET as 355) → false.</summary>
        public static bool TryParseValue(string? text, out double? value)
        {
            value = null;
            var t = (text ?? "").Trim();
            if (t.Length == 0) return true;
            if (t.EndsWith("%")) t = t.Substring(0, t.Length - 1).TrimEnd();
            if (t.Contains(",") && !ThousandsGrouped.IsMatch(t)) return false;
            if (!double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v)
                || v < 0 || double.IsNaN(v) || double.IsInfinity(v))
                return false;
            value = v;
            return true;
        }
    }

    public enum LimitStatus
    {
        NotChecked,
        Ok,
        Close,
        Over
    }

    /// <summary>One chip of the limits strip, e.g. "Primary 32.1% / 35%".</summary>
    public sealed record PlotLimitChip(string Text, LimitStatus Status);

    public static class PlotLimitCheck
    {
        public const string NoPlotHint = "Type the plot area to see % of plot";

        /// <summary>Used at or above this share of the allowed % is <see cref="LimitStatus.Close"/>.</summary>
        public const double CloseShare = 0.95;

        // Float noise (e.g. 474 m² of 2370 at 20%) must not turn "at the limit" into "over".
        private const double Tolerance = 1e-9;

        public static double? UsedPercent(double m2, double? plotAreaM2) =>
            plotAreaM2 is { } plot && plot > 0 ? m2 * 100 / plot : null;

        public static LimitStatus Status(double? usedPercent, double? allowedPercent)
        {
            if (usedPercent is not { } used || allowedPercent is not { } allowed) return LimitStatus.NotChecked;
            if (used > allowed + Tolerance) return LimitStatus.Over;
            return used >= CloseShare * allowed - Tolerance ? LimitStatus.Close : LimitStatus.Ok;
        }

        /// <summary>Primary, Service and Total (Primary + Service) of the <b>Building</b> row
        /// (above + below ground) against <paramref name="limits"/>.</summary>
        public static IReadOnlyList<PlotLimitChip> Chips(AreaTable table, PlotLimits limits)
        {
            var t = table.Rows.Single(r => r.Kind == AreaTableRowKind.Building).Totals;
            return new[]
            {
                Chip("Primary", t.Primary, limits.PlotAreaM2, limits.PrimaryPercent),
                Chip("Service", t.Service, limits.PlotAreaM2, limits.ServicePercent),
                Chip("Total", t.Total, limits.PlotAreaM2, limits.TotalPercent),
            };
        }

        private static PlotLimitChip Chip(string label, double m2, double? plot, double? allowed)
        {
            var used = UsedPercent(m2, plot);
            if (used == null) return new PlotLimitChip(label + " —", LimitStatus.NotChecked);
            var text = label + " " + used.Value.ToString("F1", CultureInfo.InvariantCulture) + "%";
            if (allowed is { } a) text += " / " + a.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            return new PlotLimitChip(text, Status(used, allowed));
        }
    }
}
