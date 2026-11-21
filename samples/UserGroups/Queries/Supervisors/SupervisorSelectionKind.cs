namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Names the four distinct person-selection contracts.</summary>
internal enum SupervisorSelectionKind
{
    /// <summary>Retains every qualifying occurrence, with output-proportional memory.</summary>
    All,

    /// <summary>Requires exactly one distinct candidate person.</summary>
    RequireUnique,

    /// <summary>Requires one distinct person at the smallest membership leadership priority.</summary>
    HighestBusinessPriority,

    /// <summary>Selects a one-based person slot while retaining ambiguous occupied tie slots.</summary>
    NthPerson,
}
