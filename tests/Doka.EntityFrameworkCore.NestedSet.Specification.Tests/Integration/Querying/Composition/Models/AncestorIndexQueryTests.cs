namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies opted-in ancestor ranges, occurrence bags and subsequent hierarchy composition.</summary>
[Collection("Model compatibility")]
public abstract partial class AncestorIndexQueryTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the current engine's collection-owned relational database.</summary>
    /// <param name="fixture">The exact provider fixture selected by the concrete suite.</param>
    protected AncestorIndexQueryTests(IProviderFixture<ModelCompatibilityDatabase> fixture) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Seeds one three-level chain and independent equal-coordinate foreign scope/tree adversaries.</summary>
    private async Task<AncestorIndexQueryContext> CreateContextAsync(
        Guid scope,
        Guid tree,
        CompositionCommandProbe probe,
        bool foreignTrees = false
    )
    {
        var context = await _fixture.CreateContextAsync<AncestorIndexQueryContext>(Engine,
            static options => new AncestorIndexQueryContext(options), probe);

        try
        {
            for (var id = 1; id <= 3; id++)
            {
                await context.AddAsync(CreateNode(scope, tree, id), CancellationToken.None);
            }

            if (foreignTrees)
            {
                var otherScope = Guid.NewGuid();
                var otherTree = Guid.NewGuid();

                for (var id = 1; id <= 3; id++)
                {
                    await context.AddAsync(CreateNode(otherScope, otherTree, id), CancellationToken.None);
                    var sameScope = CreateNode(scope, otherTree, id);
                    sameScope.Id += 10;
                    sameScope.ParentId = id == 1 ? null : id + 9;
                    sameScope.Tags.Clear();
                    sameScope.Items.Clear();
                    await context.AddAsync(sameScope, CancellationToken.None);
                }
            }

            await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Creates deterministic coordinates independently of the query's range or aggregate implementation.</summary>
    private static CompositionNavigationNode CreateNode(Guid scope, Guid tree, int id)
    {
        var node = new CompositionNavigationNode
        {
            Scope = scope,
            TreeId = tree,
            Id = id,
            ParentId = id == 1 ? null : id - 1,
            Left = id,
            Right = 7 - id,
            Depth = id - 1,
            Details = new CompositionNavigationDetail { Caption = $"details-{id}" },
        };

        for (var occurrence = 1; occurrence <= 2; occurrence++)
        {
            node.Tags.Add(new CompositionNavigationTag { Id = occurrence, Value = $"tag-{id}-{occurrence}" });
            node.Items.Add(new CompositionNavigationItem
            {
                Scope = scope,
                Id = (id * 10) + occurrence,
                NodeId = id,
                Label = $"item-{id}-{occurrence}",
            });
        }

        return node;
    }

    /// <summary>Prevents a feature-enabled run from accidentally qualifying only the legacy SQL path.</summary>
    private void AssertOptionalSourceStrategy(DbContext context, IQueryable query, bool includeSelf = false)
    {
        var hasBinding = Engine is "MySql" or "MariaDb"
            && NestedSetProviderCapabilities.Resolve(context).CompletedSourceMethod is not null
            && (!includeSelf || NestedSetProviderCapabilities.Resolve(context).OrderedJoinMethod is not null);

        var sql = query.ToQueryString();
        Assert.Equal(hasBinding, sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(hasBinding, sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(hasBinding && includeSelf, sql.Contains("STRAIGHT_JOIN", StringComparison.OrdinalIgnoreCase));
    }
}
