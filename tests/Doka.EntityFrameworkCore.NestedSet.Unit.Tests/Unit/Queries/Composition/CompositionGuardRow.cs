namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Carries selected hierarchy aliases together with independently mapped application payload.</summary>
internal sealed class CompositionGuardRow
{
    /// <summary>Gets or initializes the upper endpoint alias.</summary>
    public required CompositionGuardNode Upper { get; init; }

    /// <summary>Gets or initializes the lower endpoint alias.</summary>
    public required CompositionGuardNode Lower { get; init; }

    /// <summary>Gets or initializes payload whose mapping need not belong to the selected hierarchy.</summary>
    public CompositionGuardNode? Payload { get; init; }

    /// <summary>Gets or initializes structural distance without executing the query.</summary>
    public int Distance { get; init; }
}
