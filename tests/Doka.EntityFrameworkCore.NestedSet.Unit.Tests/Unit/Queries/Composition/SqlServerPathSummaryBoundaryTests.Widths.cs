using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerPathSummaryBoundaryTests
{
    /// <summary>The relational maximum stays deferred and excludes hidden nodes, other scopes and other trees.</summary>
    /// <param name="changed">Whether the visible maximum changes after query construction.</param>
    /// <param name="empty">Whether the selected scope has no visible nodes.</param>
    /// <param name="expected">The exact expected width, or null for an empty relational width source.</param>
    [Theory]
    [InlineData(false, false, 5L)]
    [InlineData(true, false, 33L)]
    [InlineData(false, true, null)]
    [InlineData(true, true, null)]
    public async Task RelationalWidthUsesTheCurrentExactVisibleRoot(bool changed, bool empty, long? expected)
    {
        // Arrange
        var probe = new CompositionCommandProbe();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CompositionContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets());

        // WHY: EF requires singleton materialization observers in the caller-owned internal service graph.
        await using var services = NestedSetTestAdapters.BuildServices(options.Options,
            owned => owned.AddSingleton<IInterceptor>(TestMaterializationObserver.Instance));
        // WHY: SQLite's async schema creation performs a synchronous HasTables control. The strict
        // command guard belongs to the measured scalar context, not the separately owned fixture setup.
        await using var setup = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        await setup.Database.EnsureCreatedAsync(CancellationToken.None);
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        await setup.AddRangeAsync([
            new CompositionNode { Scope = scope, TreeId = tree, Id = 1, Left = 1, Right = 16 },
            new CompositionNode { Scope = scope, TreeId = tree, Id = 2, Left = 20, Right = 10_000, Visible = false },
            new CompositionNode { Scope = Guid.NewGuid(), TreeId = tree, Id = 3, Left = 1, Right = 400 },
            new CompositionNode { Scope = scope, TreeId = Guid.NewGuid(), Id = 4, Left = 1, Right = 10_000 },
        ], CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = new CompositionContext(options.AddInterceptors(probe).Options);
        var selectedScope = empty ? Guid.NewGuid() : scope;
        var unbound = context.Set<CompositionNode>().AsNoTracking();
        var visible = unbound.Where(node => node.Scope == selectedScope && node.TreeId == tree);
        var root = new NestedSetQuery<CompositionNode>(context,
            context.Model.FindEntityType(typeof(CompositionNode))!, visible, unbound, treeBound: true);

        // WHY: This invokes the actual private relational-width builder without changing SQLite's adapter
        // capabilities or adding a production testing API. Execution still uses EF and the real mapped filter.
        var widths = (IQueryable<long>)typeof(NestedSetQuery<CompositionNode>)
            .GetMethod("VisibleCoordinateBlockWidths", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(root, BindingFlags.DoNotWrapExceptions, null, [], null)!;

        if (changed)
        {
            await context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 1)
                .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Right, 1024L), CancellationToken.None);
        }

        var materialized = 0;
        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        probe.Reset();

        // Act
        var actual = await widths.ToArrayAsync(CancellationToken.None);

        // Assert
        if (expected is { } width)
        {
            Assert.Equal(width, Assert.Single(actual));
        }
        else
        {
            Assert.Empty(actual);
        }

        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, materialized);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
