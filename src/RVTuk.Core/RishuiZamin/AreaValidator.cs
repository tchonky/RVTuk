using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.RishuiZamin
{
    /// <summary>
    /// The result of validating a set of area records plus their submission config:
    /// blocking errors (submission cannot proceed) and non-blocking warnings.
    /// </summary>
    /// <param name="Errors">Blocking validation errors.</param>
    /// <param name="Warnings">Non-blocking validation warnings.</param>
    public record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Validates <see cref="AreaRecord"/>s and <see cref="AreaSubmissionConfig"/> ahead of
    /// area-submission export: flags missing/invalid usage codes, degenerate boundaries,
    /// zero-or-negative areas, and incomplete config, without touching the Revit API.
    /// </summary>
    public static class AreaValidator
    {
        /// <summary>
        /// Checks a single area record and returns the combination of <see cref="AreaError"/>
        /// flags that apply to it (does not mutate <see cref="AreaRecord.Errors"/>).
        /// </summary>
        public static AreaError CheckArea(AreaRecord a)
        {
            var errors = AreaError.None;

            // The robot requires at least one of USAGE_TYPE / USAGE_TYPE_OLD, and any code
            // actually provided must exist in the catalog (a demolition area legitimately has
            // only UsageCodePrev, with a 300-302 process code or nothing in UsageCode).
            var hasValidCurrent = a.UsageCode != null && UsageCatalog.IsValidCode(a.UsageCode.Value);
            var hasValidPrev = a.UsageCodePrev != null && UsageCatalog.IsValidCode(a.UsageCodePrev.Value);
            var providedButInvalid =
                (a.UsageCode != null && !UsageCatalog.IsValidCode(a.UsageCode.Value)) ||
                (a.UsageCodePrev != null && !UsageCatalog.IsValidCode(a.UsageCodePrev.Value));

            if ((!hasValidCurrent && !hasValidPrev) || providedButInvalid)
            {
                errors |= AreaError.NoUsageCode;
            }

            if (a.AreaValue <= 0)
            {
                errors |= AreaError.ZeroArea;
            }

            if (!a.BoundaryLoops.Any(loop => loop.Count >= 3))
            {
                errors |= AreaError.BadBoundary;
            }

            return errors;
        }

        /// <summary>
        /// Checks the submission config and returns human-readable error messages for any
        /// missing/invalid settings. Empty list means the config is valid.
        /// </summary>
        public static IReadOnlyList<string> CheckConfig(AreaSubmissionConfig c)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(c.OutputFolder))
            {
                errors.Add("Output folder is not set.");
            }

            if (string.IsNullOrWhiteSpace(c.FileBaseName))
            {
                errors.Add("File base name is not set.");
            }

            if (c.Scale <= 0)
            {
                errors.Add("Scale must be greater than zero.");
            }

            if (c.BuildingNo < 1)
            {
                errors.Add("Building number must be at least 1.");
            }

            return errors;
        }

        /// <summary>
        /// Validates a full set of areas plus config, aggregating per-area errors (identified
        /// by number/name) and config errors into <see cref="ValidationResult.Errors"/>, and
        /// non-blocking issues (areas missing a number or name) into
        /// <see cref="ValidationResult.Warnings"/>.
        /// </summary>
        public static ValidationResult Validate(IReadOnlyList<AreaRecord> areas, AreaSubmissionConfig c)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            foreach (var area in areas)
            {
                var label = DescribeArea(area);
                var areaErrors = CheckArea(area);

                if (areaErrors.HasFlag(AreaError.NoUsageCode))
                {
                    errors.Add($"{label}: missing or invalid usage code.");
                }

                if (areaErrors.HasFlag(AreaError.ZeroArea))
                {
                    errors.Add($"{label}: area is zero or negative.");
                }

                if (areaErrors.HasFlag(AreaError.BadBoundary))
                {
                    errors.Add($"{label}: boundary is missing or degenerate.");
                }

                if (string.IsNullOrWhiteSpace(area.Number) || string.IsNullOrWhiteSpace(area.Name))
                {
                    warnings.Add($"{label}: missing number or name.");
                }

                if (c.SheetWidthCm > 0 && c.SheetHeightCm > 0 &&
                    area.BoundaryLoops.Any(loop => loop.Any(p =>
                        p.X < 0 || p.Y < 0 || p.X > c.SheetWidthCm || p.Y > c.SheetHeightCm)))
                {
                    warnings.Add($"{label}: geometry extends outside the sheet frame.");
                }
            }

            // Robot frame caps (physical print size): 910 mm height, 15,000 mm length
            // (14,500 mm for the PAGE_NO=1 sheet). Drawing cm -> paper mm = cm * 10 / scale.
            if (c.SheetWidthCm > 0 && c.SheetHeightCm > 0 && c.Scale > 0)
            {
                var paperHeightMm = c.SheetHeightCm * 10.0 / c.Scale;
                var paperWidthMm = c.SheetWidthCm * 10.0 / c.Scale;
                if (paperHeightMm > 910)
                {
                    warnings.Add($"Sheet height is {paperHeightMm:0} mm on paper — the robot's frame cap is 910 mm.");
                }
                if (paperWidthMm > 14500)
                {
                    warnings.Add($"Sheet length is {paperWidthMm:0} mm on paper — the robot caps page 1 at 14,500 mm (other pages 15,000 mm).");
                }
            }

            errors.AddRange(CheckConfig(c));

            return new ValidationResult(errors, warnings);
        }

        private static string DescribeArea(AreaRecord area)
        {
            var number = string.IsNullOrWhiteSpace(area.Number) ? "?" : area.Number;
            var name = string.IsNullOrWhiteSpace(area.Name) ? "?" : area.Name;
            return $"Area {number} ({name})";
        }
    }
}
