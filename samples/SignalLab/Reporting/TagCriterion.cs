namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Specifies one scalar field on one owner classification.</summary>
/// <param name="TypeId">The required owner classification.</param>
/// <param name="FieldCode">The exact catalog field code.</param>
/// <param name="Value">The fixed permitted value, passed to SQL as a parameter.</param>
/// <param name="OwnerMode">The owner selection policy.</param>
/// <param name="Operator">The allowed scalar comparison.</param>
internal sealed record TagCriterion(
    Guid TypeId,
    string FieldCode,
    string Value,
    TagOwnerMode OwnerMode = TagOwnerMode.Any,
    TagOperator Operator = TagOperator.Equal
);
