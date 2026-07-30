using System;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The shared parameter carrying a topo line's shared (survey) elevation. A Length, so the
    /// value is typed and displayed in the project's own units instead of a bare number with an
    /// assumed unit.
    ///
    /// Bound through a temporary shared-parameter file because the API cannot create non-shared
    /// project parameters — the technique <c>UsageKeyScheduleBuilder</c> already uses for the RZ_*
    /// parameters, sharing its temp file so a project only ever grows one.
    /// </summary>
    public static class TopoElevationParameter
    {
        public const string ParamName = "TOPO_Elevation";

        // Stable for the life of the tool: changing it would orphan every value already typed.
        private static readonly Guid ParamGuid = new Guid("b8f4a1d6-3e27-4c95-9a10-2d7c6e5b8f34");

        public static bool IsBound(Document doc)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            var iterator = doc.ParameterBindings.ForwardIterator();
            while (iterator.MoveNext())
            {
                if (iterator.Key is Definition definition &&
                    definition.Name == ParamName &&
                    iterator.Current is ElementBinding binding &&
                    binding.Categories.Contains(linesCategory))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static void EnsureBound(Document doc)
        {
            if (IsBound(doc)) return;

            var app = doc.Application;
            var originalFile = app.SharedParametersFilename;
            try
            {
                var tempFile = Path.Combine(Path.GetTempPath(), "RVTuk_SharedParams.txt");
                if (!File.Exists(tempFile))
                {
                    File.WriteAllText(tempFile, "", Encoding.Unicode);
                }

                app.SharedParametersFilename = tempFile;
                var sharedFile = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException(
                        "Could not open the temporary shared-parameter file.");

                var group = sharedFile.Groups.get_Item("RVTuk") ?? sharedFile.Groups.Create("RVTuk");
                var definition = group.Definitions.get_Item(ParamName) as ExternalDefinition
                    ?? (ExternalDefinition)group.Definitions.Create(
                        new ExternalDefinitionCreationOptions(ParamName, SpecTypeId.Length)
                        {
                            GUID = ParamGuid,
                        });

                var categories = app.Create.NewCategorySet();
                categories.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines));

                // GroupTypeId.General is today's ForgeTypeId for what Revit's UI used to label
                // "Other" — keeps this out of the crowded Identity Data group.
                doc.ParameterBindings.Insert(
                    definition, app.Create.NewInstanceBinding(categories), GroupTypeId.General);
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
            }
        }

        /// <summary>
        /// The line's shared elevation in internal units, or false when it was never filled in.
        /// Judged by <c>HasValue</c>, never by the number: 0.000 is a legitimate shared elevation
        /// and must not read as "forgotten".
        /// </summary>
        public static bool TryGetElevation(Element line, out double elevationFeet)
        {
            elevationFeet = 0;

            var parameter = line.LookupParameter(ParamName);
            if (parameter == null || !parameter.HasValue) return false;
            if (parameter.StorageType != StorageType.Double) return false;

            elevationFeet = parameter.AsDouble();
            return true;
        }
    }
}
