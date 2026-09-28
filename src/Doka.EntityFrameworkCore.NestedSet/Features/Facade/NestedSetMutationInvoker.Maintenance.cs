namespace Doka.EntityFrameworkCore.NestedSet.Features.Facade;

internal sealed partial class
    NestedSetMutationInvoker<TEntity, TKey, TTreeId, TScope> : NestedSetMutationInvoker<TEntity>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <inheritdoc />
    internal override Task<NestedSetValidationReport> ValidateTreeAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        NestedSetValidationLevel level,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TKey, TRequestedTreeId, TScope>.ValidateTreeCoreAsync(
        binding,
        treeId,
        level,
        cancellationToken);

    /// <summary>Validates one typed tree through its consistent read boundary.</summary>
    private static Task<NestedSetValidationReport> ValidateTreeCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TTreeId treeId,
        NestedSetValidationLevel level,
        CancellationToken cancellationToken
    )
    {
        var maintenance = Maintenance(binding, treeId);

        return NestedSetTelemetry.ExecuteAsync(
            binding.Context,
            "validate",
            token => maintenance.ValidateAsync(level, token),
            cancellationToken);
    }

    /// <inheritdoc />
    internal override Task<NestedSetRebuildPlan> PlanRebuildAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TKey, TRequestedTreeId, TScope>.PlanRebuildCoreAsync(
        binding,
        treeId,
        cancellationToken);

    /// <summary>Plans repairs for one typed tree without changing its stored coordinates.</summary>
    private static Task<NestedSetRebuildPlan> PlanRebuildCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TTreeId treeId,
        CancellationToken cancellationToken
    )
    {
        var maintenance = Maintenance(binding, treeId);

        return NestedSetTelemetry.ExecuteAsync(
            binding.Context,
            "plan_rebuild",
            maintenance.PlanRebuildAsync,
            cancellationToken);
    }

    /// <inheritdoc />
    internal override Task RebuildTreeAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    ) => NestedSetMutationInvoker<TEntity, TKey, TRequestedTreeId, TScope>.RebuildTreeCoreAsync(
        binding,
        treeId,
        cancellationToken);

    /// <summary>Repairs one typed tree within the existing atomic mutation protocol.</summary>
    private static Task RebuildTreeCoreAsync(
        NestedSetMutationBinding<TEntity> binding,
        TTreeId treeId,
        CancellationToken cancellationToken
    )
    {
        var maintenance = Maintenance(binding, treeId);

        return NestedSetTelemetry.ExecuteAsync(
            binding.Context,
            "rebuild",
            maintenance.RebuildAsync,
            cancellationToken);
    }

    /// <summary>Creates only the maintenance feature required by the selected tree operation.</summary>
    private static NestedSetMaintenance<TEntity, TKey, TTreeId, TScope> Maintenance(
        NestedSetMutationBinding<TEntity> binding,
        TTreeId treeId
    ) => new(
        Store(binding, treeId),
        new NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>(binding.Context, binding.EntityType));
}
