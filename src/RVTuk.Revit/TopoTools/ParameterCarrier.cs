using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>What happened to one element's parameter values.</summary>
    public sealed record CarryOutcome(int Copied, int Parked, int Failed);

    /// <summary>
    /// Carries instance parameter values from a converted element to its replacement.
    ///
    /// A parameter is found again by its built-in id, its shared GUID, or, for a project
    /// parameter, its name. A value the new element has a writable home for is set. Any other is
    /// parked on the new element (<see cref="ParkedParameterStore"/>), and every conversion offers
    /// the parked values again, so a Floors-only value survives floor → toposolid → floor. Nothing
    /// here is fatal: a value Revit refuses is counted and the conversion goes on.
    /// </summary>
    public static class ParameterCarrier
    {
        /// <summary>Set explicitly by the converter, or never meaningful to copy.</summary>
        private static readonly HashSet<BuiltInParameter> Handled = new HashSet<BuiltInParameter>
        {
            BuiltInParameter.LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM,
            BuiltInParameter.TOPOSOLID_HEIGHTABOVELEVEL_PARAM,
            BuiltInParameter.ELEM_TYPE_PARAM,
            BuiltInParameter.ELEM_FAMILY_PARAM,
            BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
            BuiltInParameter.SYMBOL_ID_PARAM,
            BuiltInParameter.DESIGN_OPTION_ID,
        };

        private static readonly string PhaseCreatedKey =
            ((int)BuiltInParameter.PHASE_CREATED).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The source's own writable instance values, then whatever an earlier conversion parked on
        /// it. Read before the source is deleted.
        /// </summary>
        public static IReadOnlyList<ParkedParameter> Snapshot(Element source)
        {
            var values = new List<ParkedParameter>();
            var seen = new HashSet<string>();

            foreach (Parameter parameter in source.Parameters)
            {
                var value = Read(parameter);
                if (value != null && seen.Add(Identity(value))) values.Add(value);
            }

            foreach (var parked in ParkedParameterStore.Read(source))
            {
                if (seen.Add(Identity(parked))) values.Add(parked);
            }

            // Phase Created first: Revit refuses a demolition phase earlier than the creation phase.
            return values
                .OrderBy(v => v.KeyKind == ParkedKeyKind.BuiltIn && v.Key == PhaseCreatedKey ? 0 : 1)
                .ToList();
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static CarryOutcome Apply(Element target, IReadOnlyList<ParkedParameter> values)
        {
            int copied = 0;
            int failed = 0;
            var parked = new List<ParkedParameter>();

            foreach (var value in values)
            {
                var parameter = Find(target, value);
                if (parameter == null || parameter.IsReadOnly || StorageOf(parameter) != value.Storage)
                {
                    parked.Add(value);
                    continue;
                }

                if (TrySet(parameter, value)) copied++;
                else failed++;
            }

            ParkedParameterStore.Write(target, parked);
            return new CarryOutcome(copied, parked.Count, failed);
        }

        private static string Identity(ParkedParameter value) => value.KeyKind + ":" + value.Key;

        private static ParkedParameter? Read(Parameter parameter)
        {
            if (parameter.IsReadOnly || !parameter.HasValue) return null;

            var definition = parameter.Definition;
            if (definition == null) return null;

            ParkedKeyKind kind;
            string key;
            var builtIn = (definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID;
            if (builtIn != BuiltInParameter.INVALID)
            {
                if (Handled.Contains(builtIn)) return null;
                kind = ParkedKeyKind.BuiltIn;
                key = ((int)builtIn).ToString(CultureInfo.InvariantCulture);
            }
            else if (parameter.IsShared)
            {
                kind = ParkedKeyKind.Shared;
                key = parameter.GUID.ToString("D");
            }
            else
            {
                kind = ParkedKeyKind.Project;
                key = definition.Name;
            }

            switch (parameter.StorageType)
            {
                case StorageType.Integer: return ParkedParameter.OfInteger(kind, key, parameter.AsInteger());
                case StorageType.Double: return ParkedParameter.OfDouble(kind, key, parameter.AsDouble());
                case StorageType.String: return ParkedParameter.OfString(kind, key, parameter.AsString() ?? "");
                case StorageType.ElementId: return ParkedParameter.OfElementId(kind, key, parameter.AsElementId().Value);
                default: return null;
            }
        }

        private static Parameter? Find(Element target, ParkedParameter value)
        {
            switch (value.KeyKind)
            {
                case ParkedKeyKind.BuiltIn:
                    return int.TryParse(value.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                        ? target.get_Parameter((BuiltInParameter)id)
                        : null;
                case ParkedKeyKind.Shared:
                    return Guid.TryParse(value.Key, out var guid) ? target.get_Parameter(guid) : null;
                default:
                    return target.GetParameters(value.Key).FirstOrDefault(p =>
                        !p.IsShared &&
                        ((p.Definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID)
                            == BuiltInParameter.INVALID);
            }
        }

        private static ParkedStorage? StorageOf(Parameter parameter) => parameter.StorageType switch
        {
            StorageType.Integer => ParkedStorage.Integer,
            StorageType.Double => ParkedStorage.Double,
            StorageType.String => ParkedStorage.String,
            StorageType.ElementId => ParkedStorage.ElementId,
            _ => null,
        };

        private static bool TrySet(Parameter parameter, ParkedParameter value)
        {
            try
            {
                switch (value.Storage)
                {
                    case ParkedStorage.Integer: return value.TryGetInteger(out var i) && parameter.Set(i);
                    case ParkedStorage.Double: return value.TryGetDouble(out var d) && parameter.Set(d);
                    case ParkedStorage.String: return parameter.Set(value.Value);
                    case ParkedStorage.ElementId: return value.TryGetElementId(out var e) && parameter.Set(new ElementId(e));
                    default: return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
