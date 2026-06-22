using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using ReviTchucky.Core.Models;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace ReviTchucky.Revit.Extraction
{
    /// <summary>
    /// Must be called only from Revit's main thread (inside an ExternalEvent handler).
    /// </summary>
    public class FamilyMetadataExtractor
    {
        private readonly RevitApplication _app;

        public FamilyMetadataExtractor(RevitApplication app)
        {
            _app = app;
        }

        public (string? Category, IReadOnlyList<ParameterModel> Parameters) ExtractMetadata(string rfaPath)
        {
            string? tmpFile = null;
            try
            {
                tmpFile = Path.GetTempFileName();
                _app.ExtractPartAtomFromFamilyFile(rfaPath, tmpFile);
                string xml = File.ReadAllText(tmpFile);
                return ParseAtomXml(xml);
            }
            catch
            {
                return (null, Array.Empty<ParameterModel>());
            }
            finally
            {
                if (tmpFile != null && File.Exists(tmpFile))
                    File.Delete(tmpFile);
            }
        }

        private static (string? Category, IReadOnlyList<ParameterModel> Parameters) ParseAtomXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return (null, Array.Empty<ParameterModel>());

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("atom", "http://www.w3.org/2005/Atom");
            nsMgr.AddNamespace("A", "urn:schemas-autodesk-com:partatom");

            string? category =
                doc.SelectSingleNode("//A:category", nsMgr)?.InnerText?.Trim()
                ?? doc.SelectSingleNode("//*[local-name()='category'][@term]", nsMgr)?.Attributes?["term"]?.Value;

            var parameters = new List<ParameterModel>();
            var paramNodes = doc.SelectNodes("//*[local-name()='param' or local-name()='parameter']", nsMgr);

            if (paramNodes != null)
            {
                foreach (XmlNode node in paramNodes)
                {
                    string? name = node.Attributes?["pname"]?.Value
                                ?? node.Attributes?["name"]?.Value;
                    string dataType = node.Attributes?["datatype"]?.Value
                                   ?? node.Attributes?["dataType"]?.Value
                                   ?? node.Attributes?["type"]?.Value
                                   ?? "Unknown";
                    string? isInstanceStr = node.Attributes?["isInstance"]?.Value
                                         ?? node.Attributes?["isinstance"]?.Value
                                         ?? "0";

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    parameters.Add(new ParameterModel
                    {
                        ParameterName = name!,
                        DataType = dataType,
                        IsInstance = isInstanceStr == "1"
                    });
                }
            }

            return (category, parameters);
        }
    }
}
