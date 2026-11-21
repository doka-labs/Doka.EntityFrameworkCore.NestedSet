namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Seeds duplicate selection facts and distinct output occurrences without hierarchy payload.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="scope">The hierarchy scope used by the scenario.</param>
    /// <param name="selected">The selected synthetic node identifiers.</param>
    /// <param name="unrelatedNode">The unrelated node used to seed decoy facts.</param>
    private static async Task SeedConsumerFactsAsync(
        AncestorIndexQueryContext context,
        Guid scope,
        int[] selected,
        int unrelatedNode,
        CancellationToken cancellationToken
    )
    {
        for (var index = 0; index < selected.Length; index++)
        {
            var node = selected[index];
            var offset = 4 * index;
            context.AddRange(
                new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = offset + 1,
                    NodeId = node,
                    Label = "selected",
                },
                new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = offset + 2,
                    NodeId = node,
                    Label = "selected",
                },
                new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = offset + 3,
                    NodeId = node,
                    Label = "occurrence",
                },
                new CompositionNavigationItem
                {
                    Scope = scope,
                    Id = offset + 4,
                    NodeId = node,
                    Label = "occurrence",
                });
        }

        var end = 4 * selected.Length;
        context.AddRange(
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = end + 1,
                NodeId = unrelatedNode,
                Label = "unrelated",
            },
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = end + 2,
                NodeId = 1,
                Label = "owner",
            },
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = end + 3,
                NodeId = 1,
                Label = "owner",
            },
            new CompositionNavigationItem
            {
                Scope = scope,
                Id = end + 4,
                NodeId = selected[0],
                Label = "owner",
            });

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        const string analyze = "ANALYZE TABLE `AncestorIndexQueryItems`";
        await context.Database.ExecuteSqlRawAsync(analyze, cancellationToken);
    }
}
