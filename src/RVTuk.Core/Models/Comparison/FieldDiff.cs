using System;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>A single field's value on each side of a comparison.
    /// A null value means "not controlled / absent on that side".</summary>
    public class FieldDiff
    {
        public FieldDiff(string fieldId, string label, string? valueA, string? valueB)
        {
            FieldId = fieldId;
            Label = label;
            ValueA = valueA;
            ValueB = valueB;
        }

        public string FieldId { get; }
        public string Label { get; }
        public string? ValueA { get; }
        public string? ValueB { get; }

        public bool IsEqual => string.Equals(ValueA, ValueB, StringComparison.Ordinal);
    }
}
