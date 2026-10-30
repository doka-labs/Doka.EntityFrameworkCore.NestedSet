namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>The opted-in model rejects nonpositive interval widths at the actual database boundary.</summary>
    /// <param name="right">The malformed right bound paired with left bound three.</param>
    [Theory]
    [InlineData(3L)]
    [InlineData(2L)]
    public async Task IndexedModelRejectsNonPositiveWidthAtDatabaseBoundary(long right)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, tree, probe);
        var source = context.Set<CompositionNavigationNode>().Where(node => node.Scope == scope && node.Id == 3);
        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(() => source.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Right, right), CancellationToken.None));

        // Assert
        var databaseError = Assert.IsAssignableFrom<DbException>(error);
        Assert.Contains("CK_NestedSet_RightAfterLeft_", databaseError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Bound ancestry from another named mapping cannot acquire endpoint authority from its CLR type or equal keys.</summary>
    [Fact]
    public async Task IndexedForeignNamedEndpointsAreRejectedBeforeSql()
    {
        // Arrange
        var first = Interlocked.Add(ref s_mappingIdentity, 10);
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexMappedContext>(Engine,
            static options => new AncestorIndexMappedContext(options), probe);

        await SeedNamedQueriesAsync(context, tree, scope, first, includeSelf: true);
        var hierarchy = context.NestedSet<Dictionary<string, object?>>(AncestorIndexMappedContext.First).InTree(tree);
        var foreign = context.NestedSet<Dictionary<string, object?>>(AncestorIndexMappedContext.Second).InTree(tree);
        var pairs = foreign.SelectAncestors(node => EF.Property<int>(node, NestedSetPropertyRoles.Depth) == 12,
            (source, owner, distance) => new { Source = source, Owner = owner });

        probe.Reset();

        // Act
        var error = Record.Exception(() => hierarchy.WherePathEdges(pairs, pair => pair.Owner, pair => pair.Source,
            (pair, parent, child) => true));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
