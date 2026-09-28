namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares native lock requests and command selection across provider-owned ordering tests.</summary>
internal static class OrderingLockTestSupport
{
    /// <summary>Identifies the first seeded registry tree consistently across shared and local suites.</summary>
    internal static readonly Guid FirstTreeId = Guid.Parse("1cb86965-fac5-4310-b642-c450900cc28e");

    /// <summary>Creates a reservation request from the same validated mapping consumed by structural writes.</summary>
    internal static NestedSetTreeLockRequest<Guid, TScope> Request<TScope, THierarchy>(
        OrderingLockContext context,
        TScope scope,
        Guid treeId
    )
        where TScope : notnull
    {
        var entityType = context.Model.FindEntityType(typeof(OrderingLockNode<TScope, THierarchy>))!;
        var mapping = NestedSetMapping<OrderingLockNode<TScope, THierarchy>, int, TScope>.For(context, entityType);

        return new NestedSetTreeLockRequest<Guid, TScope>(mapping.EntityType, scope, treeId, NestedSetTreeLockMode.New);
    }

    /// <summary>Selects canonical tree-identity ordering commands without matching ordinary tree reads.</summary>
    internal static IEnumerable<OrderingLockCommand> CanonicalQueries(
        OrderingLockProbe probe
    ) => probe.Commands.Where(command => command.Sql.Contains("RequestedTrees", StringComparison.Ordinal));

    /// <summary>Selects the final lifecycle read that owns each registry row lock.</summary>
    internal static IEnumerable<OrderingLockCommand> RegistryLifecycleLocks(
        OrderingLockProbe probe
    ) => probe.Commands.Where(command =>
        command.Sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
        && command.Sql.Contains("Lifecycle", StringComparison.Ordinal));

    /// <summary>Starts the transaction required by the provider's write-lock contract.</summary>
    internal static Task<IDbContextTransaction> BeginAsync(
        OrderingLockContext context
    ) => context.Database.BeginTransactionAsync(
        context.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
        CancellationToken.None);
}
