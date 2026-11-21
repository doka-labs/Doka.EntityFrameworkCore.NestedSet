namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Captures one validated classification's same-owner scalar predicates.</summary>
/// <param name="TypeId">The owner classification.</param>
/// <param name="Mode">The common owner selection policy.</param>
/// <param name="Criteria">Deduplicated, noncontradictory scalar fields.</param>
internal sealed record TagGroup(
    Guid TypeId,
    TagOwnerMode Mode,
    TagCriterion[] Criteria
);
