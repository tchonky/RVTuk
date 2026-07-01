using System;
using System.Collections.Generic;
using System.IO;

namespace RVTuk.Core.AreaSubmission
{
    /// <summary>
    /// Validates a set of area records plus submission config, then writes the paired
    /// <c>.dxf</c> (<see cref="DxfWriter"/>) and <c>.dat</c> (<see cref="DatWriter"/>) files
    /// the Rishui Zamin robot expects. Pure file I/O — no Revit API dependency, so it lives in
    /// RVTuk.Core and is fully unit-testable.
    /// </summary>
    public static class AreaSubmissionExporter
    {
        /// <summary>
        /// Validates <paramref name="areas"/>/<paramref name="cfg"/> via <see cref="AreaValidator.Validate"/>.
        /// If there are blocking errors, writes nothing and returns <c>(false, joined errors)</c>.
        /// Otherwise writes <c>{FileBaseName}.dxf</c> and <c>{FileBaseName}.dat</c> into
        /// <see cref="AreaSubmissionConfig.OutputFolder"/> and returns <c>(true, ...)</c> naming
        /// the written paths. IO failures are caught and returned as <c>(false, ex.Message)</c>.
        /// </summary>
        public static (bool ok, string message) Export(IReadOnlyList<AreaRecord> areas, AreaSubmissionConfig cfg)
        {
            var validation = AreaValidator.Validate(areas, cfg);
            if (validation.Errors.Count > 0)
            {
                return (false, string.Join(Environment.NewLine, validation.Errors));
            }

            var dxfPath = Path.Combine(cfg.OutputFolder, cfg.FileBaseName + ".dxf");
            var datPath = Path.Combine(cfg.OutputFolder, cfg.FileBaseName + ".dat");

            try
            {
                var dxfText = DxfWriter.Build(areas, cfg);
                File.WriteAllText(dxfPath, dxfText);

                var datBytes = DatWriter.Build(cfg.Scale);
                File.WriteAllBytes(datPath, datBytes);

                return (true, $"Wrote {dxfPath} and {datPath}.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
