namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Specifies one person-selection alternative.</summary>
/// <param name="Kind">The explicit selection rule.</param>
/// <param name="Number">The one-based person slot; used only by NthPerson.</param>
internal sealed record SupervisorSelection(
    SupervisorSelectionKind Kind,
    int? Number = null
)
{
    /// <summary>Gets the rule retaining every qualifying occurrence.</summary>
    internal static SupervisorSelection All { get; } = new(SupervisorSelectionKind.All);

    /// <summary>Gets the rule requiring exactly one distinct person.</summary>
    internal static SupervisorSelection RequireUnique { get; } = new(SupervisorSelectionKind.RequireUnique);

    /// <summary>Gets the rule selecting the unique person at the smallest business priority.</summary>
    internal static SupervisorSelection HighestBusinessPriority { get; } =
        new(SupervisorSelectionKind.HighestBusinessPriority);

    /// <summary>Creates an explicit one-based person-slot rule.</summary>
    internal static SupervisorSelection NthPerson(
        int number
    ) => new(SupervisorSelectionKind.NthPerson, number);

    /// <summary>Rejects ignored rank arguments and nonpositive slots before I/O.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(Kind)
            || (Kind == SupervisorSelectionKind.NthPerson
                ? Number is null or <= 0
                : Number is not null))
        {
            throw new ArgumentException("Person slots are positive and belong only to NthPerson.");
        }
    }
}
