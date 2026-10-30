using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Facade;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects compact owned bindings without merging caller filters or replacing earlier identities.</summary>
public sealed partial class CompositionBindingExpressionTests(ITestOutputHelper output)
{
    /// <summary>A scoped tree has one owned predicate rather than repeated EF-visible Where calls.</summary>
    [Fact]
    public void ScopeAndTreeUseOneOwnedPredicate()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = Options().AddInterceptors(boundary, boundary.Commands);
        using var services = Services(options.Options);
        using var context = new CompositionGuardContext(options.UseInternalServiceProvider(services).Options);
        var root = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping);
        var scoped = root.ForScope(11);

        // Act
        var tree = scoped.InTree(Guid.Empty);
        var treeCalls = WhereCalls(BoundRoot(tree).Expression);

        // Assert
        Assert.Equal(1, treeCalls);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Unbound, scope-only and unscoped tree roots retain only the constraints they own.</summary>
    [Fact]
    public void RootsWithoutBothBindingsDoNotAddRedundantPredicates()
    {
        // Arrange
        var options = Options();
        using var services = Services(options.Options);
        using var context = new CompositionGuardContext(options.UseInternalServiceProvider(services).Options);
        var scopedEntity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var unscopedEntity = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        var scoped = new NestedSetQuery<CompositionGuardNode>(context, scopedEntity);
        var unscoped = new NestedSetQuery<CompositionGuardNode>(context, unscopedEntity);
        var operations = new NestedSetMutationBinding<CompositionGuardNode>(context, unscopedEntity);

        // Act
        var rootCalls = WhereCalls(unscoped.OrderedNodes.Expression);
        var scopeCalls = WhereCalls(scoped.ForScope(11).OrderedNodes.Expression);
        var treeCalls = WhereCalls(unscoped.InTree(Guid.Empty, operations).Nodes.Expression);

        // Assert
        Assert.Equal(0, rootCalls);
        Assert.Equal(1, scopeCalls);
        Assert.Equal(1, treeCalls);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Caller predicates remain independent expression occurrences with their own deferred behavior.</summary>
    [Fact]
    public void CallerFiltersRemainSeparateAndDeferred()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = Options().AddInterceptors(boundary, boundary.Commands);
        using var services = Services(options.Options);
        using var context = new CompositionGuardContext(options.UseInternalServiceProvider(services).Options);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(11).InTree(Guid.Empty);

        var first = tree.Nodes.Where(node => DeferredRule(node.Id));

        // Act
        var second = first.Where(node => node.Id > 0);

        // Assert
        var where = Assert.IsAssignableFrom<MethodCallExpression>(second.Expression);
        Assert.Same(first.Expression, where.Arguments[0]);
        Assert.Equal(3, WhereCalls(second.Expression));
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Internal repeated scope binding remains cumulative rather than replacing the earlier scope.</summary>
    /// <param name="sameScope">Whether the second binding agrees with the first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReboundScopeRetainsEarlierScopeAndTree(bool sameScope)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = Options(connection);
        await using var services = Services(options.Options);
        await using var context = new CompositionGuardContext(options.UseInternalServiceProvider(services).Options);
        await SeedBindingsAsync(context);
        var entity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var mutations = new NestedSetMutationBinding<CompositionGuardNode>(context, entity);
        var original = new NestedSetQuery<CompositionGuardNode>(context, entity).ForScope(11)
            .InTree(Guid.Empty, mutations);

        var rebound = Query(original).ForScope(sameScope ? 11 : 22);

        // Act
        var originalRows = await original.Nodes.Select(node => node.Id).ToArrayAsync(CancellationToken.None);
        var reboundRows = await rebound.OrderedNodes.Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], originalRows);
        Assert.Equal(sameScope ? [1] : Array.Empty<int>(), reboundRows);
        Assert.Equal(1, WhereCalls(BoundRoot(original).Expression));
        Assert.Equal(1, WhereCalls(Assert.IsAssignableFrom<MethodCallExpression>(
            rebound.OrderedNodes.Expression).Arguments[0]));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Internal repeated tree binding cannot leak into a second tree in the same scope.</summary>
    /// <param name="sameTree">Whether the second binding agrees with the first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReboundTreeRetainsEarlierScopeAndTree(bool sameTree)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = Options(connection);
        await using var services = Services(options.Options);
        await using var context = new CompositionGuardContext(options.UseInternalServiceProvider(services).Options);
        await SeedBindingsAsync(context);
        var entity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var mutations = new NestedSetMutationBinding<CompositionGuardNode>(context, entity);
        var original = new NestedSetQuery<CompositionGuardNode>(context, entity).ForScope(11)
            .InTree(Guid.Empty, mutations);

        var rebound = Query(original).InTree(sameTree ? Guid.Empty : OtherTree, mutations);

        // Act
        var originalRows = await original.Nodes.Select(node => node.Id).ToArrayAsync(CancellationToken.None);
        var reboundRows = await rebound.Nodes.Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], originalRows);
        Assert.Equal(sameTree ? [1] : Array.Empty<int>(), reboundRows);
        Assert.Equal(1, WhereCalls(BoundRoot(rebound).Expression));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>One fewer visited binding call reduces actual warm EF parameter/SQL work.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public void FusedBindingReducesWarmSqlWorkAllocations()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = Options().AddInterceptors(boundary, boundary.Commands);
        using var services = Services(options.Options);
        using var context = new CompositionGuardContext(options.UseInternalServiceProvider(services).Options);
        var entity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var source = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping).AsNoTracking();
        var scope = NestedSetKeyFilter<CompositionGuardNode>.Equal(entity.FindProperty("Scope")!, 11);
        var treeId = NestedSetKeyFilter<CompositionGuardNode>.Equal(entity.FindProperty("TreeId")!, Guid.Empty);
        var baseline = source.Where(scope).Where(treeId);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(11).InTree(Guid.Empty);

        var current = BoundRoot(tree);

        for (var index = 0; index < 100; index++)
        {
            GC.KeepAlive(baseline.ToQueryString());
            GC.KeepAlive(current.ToQueryString());
        }

        // Act
        var allocated = QueryConstructionAllocation.MeasurePaired(
            () => baseline.ToQueryString(), () => current.ToQueryString());

        // Assert
        output.WriteLine($"Warm SQL work: separate={allocated.Baseline} B/op; fused={allocated.Expanded} B/op.");
        Assert.Equal(baseline.ToQueryString(), current.ToQueryString());
        Assert.Equal(MethodCalls(baseline.Expression) - 1, MethodCalls(current.Expression));
        Assert.InRange(allocated.Expanded, 0, allocated.Baseline - 24);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Gets the second seeded tree without allocating mutable shared identity state.</summary>
    private static Guid OtherTree => new("d74a68e0-0000-4000-8000-000000000002");

    /// <summary>Extracts the exact current binding below its presentation ordering.</summary>
    private static IQueryable<CompositionGuardNode> BoundRoot(NestedSetTree<CompositionGuardNode, Guid> tree)
    {
        var ordered = Assert.IsAssignableFrom<MethodCallExpression>(tree.Nodes.Expression);

        return tree.Nodes.Provider.CreateQuery<CompositionGuardNode>(ordered.Arguments[0]);
    }

    /// <summary>Accesses the internal immutable query solely to guard existing cumulative rebind behavior.</summary>
    private static NestedSetQuery<CompositionGuardNode> Query(NestedSetTree<CompositionGuardNode, Guid> tree)
        => Assert.IsType<NestedSetQuery<CompositionGuardNode>>(tree.GetType()
            .GetField("_query", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tree));

    /// <summary>Creates owned SQLite options for construction or actual in-memory reads.</summary>
    private static DbContextOptionsBuilder<CompositionGuardContext> Options(SqliteConnection? connection = null)
        => connection is null
            ? new DbContextOptionsBuilder<CompositionGuardContext>()
                .UseSqlite(
                    "Data Source=:memory:",
                    provider => provider.UseNestedSets())
            : new DbContextOptionsBuilder<CompositionGuardContext>()
                .UseSqlite(
                    connection,
                    provider => provider.UseNestedSets());

    /// <summary>Keeps new model variants outside EF's process-global service cache.</summary>
    private static ServiceProvider Services(DbContextOptions options)
    {
        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Seeds identical coordinates in independent scopes and trees without hierarchy mutations.</summary>
    private static async Task SeedBindingsAsync(CompositionGuardContext context)
    {
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping).AddRangeAsync(
            [Node(1, 11, Guid.Empty), Node(2, 22, Guid.Empty), Node(3, 11, OtherTree)], CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }

    /// <summary>Creates one leaf while keeping its partition and tree independent of reused bounds.</summary>
    private static CompositionGuardNode Node(int id, int scope, Guid tree)
        => new() { Id = id, Scope = scope, TreeId = tree, Left = 1, Right = 2 };

    /// <summary>Rejects accidental execution of a caller filter during binding construction.</summary>
    private static bool DeferredRule(int id) => throw new InvalidOperationException($"Unexpected caller read: {id}.");

    /// <summary>Counts actual occurrences rather than unique expression object references.</summary>
    private static int WhereCalls(Expression expression)
    {
        var calls = new BindingCallCounter();
        calls.Visit(expression);

        return calls.WhereCalls;
    }

    /// <summary>Counts every visited method occurrence, including repeated references to the same expression.</summary>
    private static int MethodCalls(Expression expression)
    {
        var calls = new BindingCallCounter();
        calls.Visit(expression);

        return calls.MethodCalls;
    }

    /// <summary>Observes the actual method-call nodes that an ordinary expression traversal reaches.</summary>
    private sealed class BindingCallCounter : ExpressionVisitor
    {
        /// <summary>Gets the number of standard filter occurrences.</summary>
        internal int WhereCalls { get; private set; }

        /// <summary>Gets all method occurrences reached by an ordinary parameter-processing traversal.</summary>
        internal int MethodCalls { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            MethodCalls++;

            if (node.Method.DeclaringType == typeof(Queryable) && node.Method.Name == nameof(Queryable.Where))
            {
                WhereCalls++;
            }

            return base.VisitMethodCall(node);
        }
    }
}
