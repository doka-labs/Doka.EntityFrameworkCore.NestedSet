namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Defines how an acquired registry identity may transition.</summary>
internal enum NestedSetTreeLockMode
{
    /// <summary>The tree must already have an active registry row.</summary>
    Existing = 0,

    /// <summary>The tree must never have existed and is reserved by this operation.</summary>
    New = 1,

    /// <summary>The tree must exist as a tombstone reserved by a completed delete.</summary>
    Tombstoned = 2,
}

/// <summary>Exposes only the metadata and erased values required by heterogeneous native registry operations.</summary>
internal interface INestedSetTreeLockRequest
{
    /// <summary>Gets the exact cached registry mapping shared by hierarchy aliases.</summary>
    NestedSetTreeRegistryMapping Mapping { get; }

    /// <summary>Gets the Scope at the native parameter boundary, or null only when no Scope is mapped.</summary>
    object? ScopeValue { get; }

    /// <summary>Gets the non-null TreeId at the native parameter boundary.</summary>
    object TreeIdValue { get; }

    /// <summary>Gets the required registry lifecycle transition.</summary>
    NestedSetTreeLockMode Mode { get; }
}

/// <summary>Owns one immutable, complete typed tree identity and its required registry transition.</summary>
/// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
/// <typeparam name="TScope">The exact Scope type, or the internal scopeless marker.</typeparam>
internal sealed class NestedSetTreeLockRequest<TTreeId, TScope> : INestedSetTreeLockRequest
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Creates and snapshots one typed lock request from finalized hierarchy metadata.</summary>
    /// <param name="entityType">The configured hierarchy entity type.</param>
    /// <param name="scope">The exact bound Scope, or the scopeless marker when no Scope is mapped.</param>
    /// <param name="treeId">The exact non-null stable TreeId.</param>
    /// <param name="mode">Whether the tree must be active, new, or tombstoned.</param>
    internal NestedSetTreeLockRequest(
        IEntityType entityType,
        TScope scope,
        TTreeId treeId,
        NestedSetTreeLockMode mode
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(treeId);

        Mapping = NestedSetTreeRegistryMapping.For(entityType);
        RequireType<TTreeId>(Mapping.SourceTreeId, NestedSetTreeRegistryMetadata.TreeId);

        if (Mapping.SourceScope is { } scopeProperty)
        {
            RequireType<TScope>(scopeProperty, NestedSetTreeRegistryMetadata.Scope);
            RequireMappedLength(scope, scopeProperty, NestedSetTreeRegistryMetadata.Scope);
            Scope = NestedSetTypedValue<TScope>.Snapshot(scopeProperty, scope);
        }
        else
        {
            if (typeof(TScope) != typeof(NestedSetNoScope))
            {
                throw new ArgumentException(
                    "A scopeless hierarchy requires the internal scopeless marker.",
                    nameof(scope));
            }

            Scope = scope;
        }

        RequireMappedLength(treeId, Mapping.SourceTreeId, NestedSetTreeRegistryMetadata.TreeId);

        // WHY: Caller-owned mutable identities must not change while asynchronous ordering and lock commands run.
        // Only heterogeneous native consumers erase these same snapshots; typed consumers never cast them back.
        TreeId = NestedSetTypedValue<TTreeId>.Snapshot(Mapping.SourceTreeId, treeId);
        Mode = mode;
    }

    /// <summary>Gets the model-lifetime registry mapping.</summary>
    internal NestedSetTreeRegistryMapping Mapping { get; }

    /// <summary>Gets the immutable typed Scope, including the scopeless marker.</summary>
    internal TScope Scope { get; }

    /// <summary>Gets the immutable typed TreeId.</summary>
    internal TTreeId TreeId { get; }

    /// <summary>Gets the required registry lifecycle transition.</summary>
    internal NestedSetTreeLockMode Mode { get; }

    /// <inheritdoc />
    NestedSetTreeRegistryMapping INestedSetTreeLockRequest.Mapping => Mapping;

    /// <inheritdoc />
    object? INestedSetTreeLockRequest.ScopeValue => Mapping.SourceScope is null ? null : Scope;

    /// <inheritdoc />
    object INestedSetTreeLockRequest.TreeIdValue => TreeId;

    /// <inheritdoc />
    NestedSetTreeLockMode INestedSetTreeLockRequest.Mode => Mode;

    /// <summary>Rejects generic roles that cannot use the exact mapped relational type mapping.</summary>
    private static void RequireType<TValue>(
        IProperty property,
        string role
    )
        where TValue : notnull
    {
        if (property.ClrType != typeof(TValue))
        {
            throw new ArgumentException(
                $"{role} must have model type '{property.ClrType.FullName}', but received '{typeof(TValue).FullName}'.",
                role);
        }
    }

    /// <summary>Rejects identity values that the mapped store cannot represent without truncation.</summary>
    private static void RequireMappedLength<TValue>(
        TValue value,
        IProperty property,
        string role
    )
        where TValue : notnull
    {
        if (property.GetMaxLength() is not { } maximum)
        {
            return;
        }

        // WHY: Relational conversion is an erased framework boundary. Length validation checks the actual stored
        // representation, while the retained request and every structural consumer keep their exact model types.
        var converter = property.GetRelationalTypeMapping().Converter;
        var providerValue = converter is null ? value : converter.ConvertToProvider(value);
        var length = providerValue switch
        {
            string text => text.Length, byte[] binary => binary.Length, _ => 0,
        };

        if (length > maximum)
        {
            throw new ArgumentException($"{role} exceeds the mapped maximum length of {maximum}.", role);
        }
    }
}
