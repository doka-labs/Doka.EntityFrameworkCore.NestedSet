namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares test-free ordering arrangements and exact finalized cache-context identity.</summary>
internal static class OrderingRefreshTestSupport
{
    /// <summary>
    ///     Retains the fixture's strict model and provider options inside the experiment's owned cache graph.
    /// </summary>
    /// <param name="template">The exact finalized ordering model and provider options.</param>
    /// <param name="services">The independently owned cache and provider service graph.</param>
    /// <param name="interceptors">The observers installed only for this context.</param>
    /// <returns>A strict context retaining the original model and shared experimental cache.</returns>
    internal static StrictOrderingContext CreateCacheContext(
        OrderingContext template,
        IServiceProvider services,
        params IInterceptor[] interceptors
    )
    {
        var extensions = template
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings()
            .UseModel(template.Model)
            .UseInternalServiceProvider(services)
            .AddInterceptors(interceptors)
            .Options;

        // WHY: Warm and measured contexts must keep the same finalized model even if the fixture cache evicts it.

        return new StrictOrderingContext(options);
    }

    /// <summary>
    ///     Seeds valid ordered structure directly so setup does not measure repeated hierarchy mutations.
    /// </summary>
    /// <param name="fixture">The independently owned ordering database.</param>
    /// <param name="engine">The concrete suite's immutable provider engine.</param>
    /// <param name="nodes">The measured child cardinality excluding its enclosing root.</param>
    /// <returns>A task completing after deterministic row replacement and one precomputed hierarchy save.</returns>
    internal static async Task SeedAsync(
        OrderingFixture fixture,
        string engine,
        int nodes
    )
    {
        await using var setup = await fixture.ResetAsync(engine);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings()
            .UseModel(setup.Model)
            .Options;

        // WHY: An untracked enclosing root preserves every measured child and the original refresh cardinalities.
        await using var seed = new DbContext(options);
        var treeId = Guid.NewGuid();
        var root = new OrderingNode
        {
            Id = 0,
            Scope = 1,
            TreeId = treeId,
            Name = "Root",
            Left = 1,
            Right = (nodes + 1L) * 2,
        };

        var input = Enumerable
            .Range(1, nodes)
            .Select(id => new OrderingNode
            {
                Id = id,
                Scope = 1,
                TreeId = treeId,
                ParentId = 0,
                Name = $"node-{id:D8}",
                Left = id * 2,
                Right = (id * 2) + 1,
                Depth = 1,
                Position = id - 1,
            })
            .ToArray();

        await seed.AddRangeAsync(input.Prepend(root), CancellationToken.None);
        await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
    }
}
