using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks compiled-query reuse through actual asynchronous SQLite execution.</summary>
public sealed class CompositionQueryCacheTests
{
    /// <summary>Captured values reuse one compilation while a different result shape compiles independently.</summary>
    /// <param name="operation">The public composition method whose parameterized query is executed.</param>
    [Theory]
    [InlineData("ancestors")]
    [InlineData("ancestors-or-self")]
    [InlineData("subtrees")]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    [InlineData("path-bound")]
    [InlineData("any-bound")]
    [InlineData("nearest-bound")]
    public async Task FixedCardinalityParameterChangesReuseTheCompiledQuery(
        string operation
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CompositionGuardContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets())
            .Options;

        // WHY: Query-cache ownership belongs to this test's EF service graph. A process-global cache could
        // make the first execution a cache hit and hide both a missing observer and a shape regression.
        await using var provider = CreateServices(options);
        var isolatedOptions = new DbContextOptionsBuilder<CompositionGuardContext>(options)
            .UseInternalServiceProvider(provider)
            .Options;

        await using var context = new CompositionGuardContext(isolatedOptions);
        var probe = context.GetService<IEnumerable<IInterceptor>>().OfType<CompilationProbe>().Single();
        var firstTree = Guid.Parse("04ba1fd5-3159-4c68-b9fb-90f658fc925b");
        var secondTree = Guid.Parse("e1927022-7320-407e-8481-86f7d138c2cd");
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await SeedAsync(context, firstTree, secondTree);
        var first = Query(context, operation, 11, firstTree, [1, 3], 0);
        var second = Query(context, operation, 22, secondTree, [4, 6], 3);
        var changedValue = Query(context, operation, 11, firstTree, [1, 3], 1);
        var expectedFirst = Expected(operation.Replace("-bound", string.Empty, StringComparison.Ordinal));
        var expectedSecond = expectedFirst.Select(id => id + 3).ToArray();
        var expectedChangedValue = operation.StartsWith("nearest", StringComparison.Ordinal)
            ? Array.Empty<int>()
            : expectedFirst.Where(id => id > 1).ToArray();

        probe.Reset();

        // Act
        var firstResult = await first.ToArrayAsync(CancellationToken.None);
        var afterFirst = probe.Compilations;
        var secondResult = await second.ToArrayAsync(CancellationToken.None);
        var afterSecond = probe.Compilations;
        var repeatedResult = await first.ToArrayAsync(CancellationToken.None);
        var afterRepeat = probe.Compilations;
        var changedValueResult = await changedValue.ToArrayAsync(CancellationToken.None);
        var afterValueChange = probe.Compilations;
        var changedShapeResult = await second.Select(id => new { Id = id }).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedFirst, firstResult);
        Assert.Equal(expectedSecond, secondResult);
        Assert.Equal(expectedFirst, repeatedResult);
        Assert.Equal(expectedChangedValue, changedValueResult);
        Assert.Equal(expectedSecond, changedShapeResult.Select(row => row.Id));
        Assert.Equal(1, afterFirst);
        Assert.Equal(1, afterSecond);
        Assert.Equal(1, afterRepeat);
        Assert.Equal(1, afterValueChange);
        Assert.Equal(2, probe.Compilations);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Creates one complete provider service graph with a context-scoped compilation observer.</summary>
    private static ServiceProvider CreateServices(
        DbContextOptions options
    )
    {
        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        services.AddScoped<IInterceptor>(_ => new CompilationProbe());

        return services.BuildServiceProvider();
    }

    /// <summary>Seeds two equal-shaped chains under different scope, tree, and node identities.</summary>
    private static async Task SeedAsync(
        CompositionGuardContext context,
        Guid firstTree,
        Guid secondTree
    )
    {
        for (var group = 0; group < 2; group++)
        {
            var offset = group * 3;

            for (var depth = 0; depth < 3; depth++)
            {
                var node = new CompositionGuardNode
                {
                    Id = offset + depth + 1,
                    Scope = group == 0 ? 11 : 22,
                    TreeId = group == 0 ? firstTree : secondTree,
                    ParentId = depth == 0 ? null : offset + depth,
                    Left = depth + 1,
                    Right = 6 - depth,
                    Depth = depth,
                };

                await context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
                    .AddAsync(node, CancellationToken.None);
            }
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }

    /// <summary>Builds stable query shapes while all application values remain captured parameters.</summary>
    private static IQueryable<int> Query(
        CompositionGuardContext context,
        string operation,
        int scope,
        Guid tree,
        int[] anchors,
        int minimumId
    )
    {
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(scope).InTree(tree);

        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .Where(node => node.Scope == scope && node.TreeId == tree);

        // WHY: Raw sources retain endpoint membership. Bound variants separately exercise its provenance
        // shortcut, so both physical shapes must retain parameterization across scope/tree/value changes.
        var rows = operation.EndsWith("-bound", StringComparison.Ordinal)
            ? hierarchy.SelectSubtrees(upper => Enumerable.Contains(anchors, upper.Id),
                (upper, lower, distance) => new CompositionGuardRow { Upper = upper, Lower = lower })
            : nodes.Where(upper => Enumerable.Contains(anchors, upper.Id))
                .SelectMany(upper => nodes.Where(lower => lower.Left >= upper.Left && lower.Right <= upper.Right),
                    (upper, lower) => new CompositionGuardRow { Upper = upper, Lower = lower });

        var query = operation.Replace("-bound", string.Empty, StringComparison.Ordinal) switch
        {
            "ancestors" => hierarchy.SelectAncestors(node => Enumerable.Contains(anchors, node.Id),
                (node, ancestor, distance) => ancestor.Id),
            "ancestors-or-self" => hierarchy.SelectAncestorsOrSelf(node => Enumerable.Contains(anchors, node.Id),
                (node, ancestor, distance) => ancestor.Id),
            "subtrees" => hierarchy.SelectSubtrees(anchor => Enumerable.Contains(anchors, anchor.Id),
                (anchor, node, distance) => node.Id),
            "path" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Lower,
                (row, parent, child) => child.Id > minimumId).Select(row => row.Lower.Id),
            "any" => hierarchy.WhereAnyAncestorOrSelf(rows, row => row.Lower,
                (row, owner) => owner.Id > minimumId).Select(row => row.Lower.Id),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Lower,
                (row, owner) => owner.Depth == 0, (row, owner) => owner.Id > minimumId)
                .Select(row => row.Lower.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        return query.Where(id => id > minimumId).OrderBy(id => id);
    }

    /// <summary>Lists expected bags from the two explicitly selected chain endpoints.</summary>
    private static int[] Expected(
        string operation
    ) => operation switch
    {
        "ancestors" => [1, 2],
        "ancestors-or-self" => [1, 1, 2, 3],
        "subtrees" or "path" or "any" or "nearest" => [1, 2, 3, 3],
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    /// <summary>Counts actual cache misses without rewriting or executing any query expression.</summary>
    private sealed class CompilationProbe : IQueryExpressionInterceptor
    {
        /// <summary>Gets the number of compilation callbacks in the current measurement window.</summary>
        internal int Compilations { get; private set; }

        /// <inheritdoc />
        public Expression QueryCompilationStarting(
            Expression queryExpression,
            QueryExpressionEventData eventData
        )
        {
            Compilations++;

            return queryExpression;
        }

        /// <summary>Excludes any setup work from the subsequent cache assertions.</summary>
        internal void Reset() => Compilations = 0;
    }
}
