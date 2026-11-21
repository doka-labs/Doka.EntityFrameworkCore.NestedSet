namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Preserves necessary independent interval limits for one nonempty selected tree.</summary>
    private sealed class AncestorEnvelope
    {
        /// <summary>Gets the native mapped partition point when the hierarchy configures Scope.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the native mapped tree identity used by both complete relations.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the greatest selected start that any matching strict ancestor must precede.</summary>
        public long MaxLeft { get; init; }

        /// <summary>Gets the least selected end that any matching strict ancestor must follow.</summary>
        public long MinRight { get; init; }
    }

    /// <summary>Retains one selected mapped source and the independent ancestor coordinates matched in SQL.</summary>
    private sealed class AncestorCoordinatePair
    {
        /// <summary>Gets the actual selected mapped source alias.</summary>
        public required TEntity Source { get; init; }

        /// <summary>Gets the independent ancestor coordinates matched against this source.</summary>
        public required AncestorCoordinates Coordinate { get; init; }
    }

    /// <summary>Restricts the independent nonleaf relation to identity and interval roles without payload.</summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class AncestorCoordinates
    {
        /// <summary>Gets the ancestor's native mapped key without a CLR equality operator.</summary>
        public required object Key { get; init; }

        /// <summary>Gets the native mapped partition when candidate ranges use an unbound visible root.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the ancestor's mapped tree identity for forest joins.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the ancestor's maintained left coordinate.</summary>
        public long Left { get; init; }

        /// <summary>Gets the ancestor's maintained right coordinate.</summary>
        public long Right { get; init; }
    }
}
