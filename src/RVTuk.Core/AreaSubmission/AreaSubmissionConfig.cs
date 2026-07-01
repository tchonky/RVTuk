using System;

namespace RVTuk.Core.AreaSubmission
{
    /// <summary>
    /// Configuration for area submission export settings.
    /// </summary>
    public class AreaSubmissionConfig
    {
        public int BuildingNo { get; set; } = 1;
        public string? Asset { get; set; }
        public int Scale { get; set; } = 100;
        public string OutputFolder { get; set; } = "";
        public string FileBaseName { get; set; } = "";
    }
}
