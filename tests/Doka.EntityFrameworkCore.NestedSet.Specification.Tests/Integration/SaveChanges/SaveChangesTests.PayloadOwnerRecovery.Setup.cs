namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Prepares a marked ordering candidate without assigning its ordinary payload relationship.</summary>
    private static async Task<PayloadOwnerRecoveryGraph> PreparePayloadOwnerRecoveryAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        var node = await context
            .Set<NavigationNode>()
            .SingleAsync(value => value.Id == 2, cancellationToken);

        var owners = await context
            .Set<NavigationPayloadOwner>()
            .Include(value => value.Nodes)
            .Where(value => value.Scope == node.Scope)
            .ToArrayAsync(cancellationToken);

        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(cancellationToken);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var entry = context.Entry(node);
        var name = entry.Property(value => value.Name);
        var foreignKey = entry.Property(value => value.PayloadOwnerId);
        var noteValue = context
            .Entry(note)
            .Property(value => value.Value);

        node.Name = "Zulu";
        name.IsModified = true;

        return new PayloadOwnerRecoveryGraph(
            node,
            entry,
            owners.Single(owner => owner.Id == 1),
            owners.Single(owner => owner.Id == 2),
            foreignKey,
            noteValue);
    }

    /// <summary>Selects native save completion or the first later structural UPDATE as the failing boundary.</summary>
    private static (IInterceptor Interceptor, Exception Error) CreatePayloadOwnerRecoveryFailure(
        bool hierarchy
    )
    {
        if (hierarchy)
        {
            var failure = new PayloadOwnerHierarchyFailure();

            return (failure, failure.Error);
        }

        var saved = new PlannedParentFailure();

        return (saved, saved.Error);
    }
}
