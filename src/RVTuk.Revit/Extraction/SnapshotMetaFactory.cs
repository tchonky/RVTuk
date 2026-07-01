using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Revit.Extraction
{
    internal static class SnapshotMetaFactory
    {
        public static SnapshotMeta Build(UIApplication app, Document doc, string sourceKind, string? path)
        {
            int year = int.TryParse(app.Application.VersionNumber, out var y) ? y : 0;
            return new SnapshotMeta
            {
                SourceKind = sourceKind,
                SourceName = string.IsNullOrEmpty(doc.Title) ? "(untitled)" : doc.Title,
                SourcePath = string.IsNullOrEmpty(path) ? null : path,
                RevitYear = year,
                CapturedUtc = DateTime.UtcNow.ToString("o"),
                SchemaVersion = 1,
            };
        }
    }
}
