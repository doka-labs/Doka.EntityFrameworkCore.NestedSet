using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Facade;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionBindingExpressionTests
{
    /// <summary>Internal repeated scope binding remains cumulative rather than replacing the earlier scope.</summary>
    /// <param name="sameScope">Whether the second binding agrees with the first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Select_ReboundScope_RetainsEarlierScopeAndTree(
        bool sameScope
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = Options(connection);
        await using var services = Services(options.Options);
        await using var context = new CompositionGuardContext(
            options
                .UseInternalServiceProvider(services)
                .Options);

        await SeedBindingsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var entity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var mutations = new NestedSetMutationBinding<CompositionGuardNode>(context, entity);
        var original = new NestedSetQuery<CompositionGuardNode>(context, entity)
            .ForScope(11)
            .InTree(Guid.Empty, mutations);

        var rebound = Query(original)
            .ForScope(
                sameScope
                    ? 11
                    : 22);

        // Act
        var originalRows = await original
            .Nodes
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var reboundRows = await rebound
            .OrderedNodes
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], originalRows);
        Assert.Equal(
            sameScope
                ? [1]
                : Array.Empty<int>(),
            reboundRows);

        Assert.Equal(
            1,
            WhereCalls(
                BoundRoot(original)
                    .Expression));

        Assert.Equal(
            1,
            WhereCalls(
                Assert
                    .IsAssignableFrom<MethodCallExpression>(
                        rebound.OrderedNodes.Expression)
                    .Arguments[0]));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Internal repeated tree binding cannot leak into a second tree in the same scope.</summary>
    /// <param name="sameTree">Whether the second binding agrees with the first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Select_ReboundTree_RetainsEarlierScopeAndTree(
        bool sameTree
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = Options(connection);
        await using var services = Services(options.Options);
        await using var context = new CompositionGuardContext(
            options
                .UseInternalServiceProvider(services)
                .Options);

        await SeedBindingsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var entity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var mutations = new NestedSetMutationBinding<CompositionGuardNode>(context, entity);
        var original = new NestedSetQuery<CompositionGuardNode>(context, entity)
            .ForScope(11)
            .InTree(Guid.Empty, mutations);

        var rebound = Query(original)
            .InTree(
                sameTree
                    ? Guid.Empty
                    : OtherTree,
                mutations);

        // Act
        var originalRows = await original
            .Nodes
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var reboundRows = await rebound
            .Nodes
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], originalRows);
        Assert.Equal(
            sameTree
                ? [1]
                : Array.Empty<int>(),
            reboundRows);

        Assert.Equal(
            1,
            WhereCalls(
                BoundRoot(rebound)
                    .Expression));

        Assert.Empty(context.ChangeTracker.Entries());
    }
}
