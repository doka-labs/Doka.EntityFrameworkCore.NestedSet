namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexLifecycleTests
{
    /// <summary>Reads the complete structural and generated state without tracker identity resolution.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static Task<LifecycleState[]> ReadLifecycleStatesAsync(
        AncestorIndexContext context,
        CancellationToken cancellationToken
    ) => context
        .Set<MigrationNode>()
        .AsNoTracking()
        .OrderBy(x => x.NodeId)
        .Select(x => new LifecycleState(
            x.NodeId,
            x.Scope,
            x.TreeId,
            x.ParentId,
            x.Left,
            x.Right,
            x.Depth,
            x.Position,
            EF.Property<bool>(x, AncestorIndexContext.FlagProperty)))
        .ToArrayAsync(cancellationToken);

    /// <summary>Captures CLR structure and the current generated shadow sidecar.</summary>
    private static LifecycleState[] ReadTrackedLifecycleStates(
        AncestorIndexContext context
    ) => context
        .ChangeTracker
        .Entries<MigrationNode>()
        .OrderBy(x => x.Entity.NodeId)
        .Select(x => new LifecycleState(
            x.Entity.NodeId,
            x.Entity.Scope,
            x.Entity.TreeId,
            x.Entity.ParentId,
            x.Entity.Left,
            x.Entity.Right,
            x.Entity.Depth,
            x.Entity.Position,
            x.Property<bool>(AncestorIndexContext.FlagProperty)
                .CurrentValue))
        .ToArray();

    /// <summary>Captures original structure and generated sidecars independently of pending CLR edits.</summary>
    private static LifecycleState[] ReadOriginalLifecycleStates(
        AncestorIndexContext context
    ) => context
        .ChangeTracker
        .Entries<MigrationNode>()
        .OrderBy(x => x.Entity.NodeId)
        .Select(x => new LifecycleState(
            x.Property(row => row.NodeId)
                .OriginalValue,
            x.Property(row => row.Scope)
                .OriginalValue,
            x.Property(row => row.TreeId)
                .OriginalValue,
            x.Property(row => row.ParentId)
                .OriginalValue,
            x.Property(row => row.Left)
                .OriginalValue,
            x.Property(row => row.Right)
                .OriginalValue,
            x.Property(row => row.Depth)
                .OriginalValue,
            x.Property(row => row.Position)
                .OriginalValue,
            x.Property<bool>(AncestorIndexContext.FlagProperty)
                .OriginalValue))
        .ToArray();
}
