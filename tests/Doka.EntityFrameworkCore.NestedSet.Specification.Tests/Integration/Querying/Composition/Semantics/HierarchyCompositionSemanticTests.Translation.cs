namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>A simple MemberInit preserves mapped aliases through a legitimate independent payload join.</summary>
    [Fact]
    public async Task MemberInitAndIndependentPayloadRetainTheSelectedMapping()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        await context.AddAsync(new CompositionInputOne
        {
            Scope = scope,
            NodeId = 3,
            Date = new DateOnly(2044, 10, 1),
            Amount = 17,
        }, CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectAncestors(
            node => node.Id == 3,
            (source, upper, distance) => new CompositionSemanticRow
            {
                Source = source,
                Other = upper,
                Value = distance,
            });

        var rows = pairs.Join(
            context.Set<CompositionInputOne>(),
            pair => new { pair.Source.Scope, NodeId = pair.Source.Id },
            fact => new { fact.Scope, fact.NodeId },
            (pair, fact) => new CompositionSemanticRow
            {
                Source = pair.Other,
                Other = pair.Source,
                Value = fact.Amount,
            });

        // Act
        var actual = await hierarchy.WhereAnyAncestorOrSelf(
                rows,
                row => row.Source,
                (row, owner) => owner.Id == 2 && row.Value == 17)
            .Select(row => new { Upper = row.Source.Id, Leaf = row.Other.Id, row.Value })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        var row = Assert.Single(actual);
        Assert.Equal((2, 3, 17), (row.Upper, row.Leaf, row.Value));
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A constructed entity endpoint is rejected before a valid EF source can issue SQL.</summary>
    /// <param name="composer">The endpoint filter attempting to bind the constructed entity.</param>
    [Theory]
    [InlineData(CompositionEndpointFilter.Path)]
    [InlineData(CompositionEndpointFilter.Any)]
    [InlineData(CompositionEndpointFilter.Nearest)]
    public async Task ConstructedEndpointIsRejectedBeforeSql(
        CompositionEndpointFilter composer
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>().Where(node => node.Scope == scope);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => composer switch
        {
            CompositionEndpointFilter.Path => hierarchy.WherePathEdges(
                source, node => node,
                node => new CompositionNode { Scope = node.Scope, Id = node.Id, TreeId = node.TreeId },
                (node, parent, child) => true),
            CompositionEndpointFilter.Any => hierarchy.WhereAnyAncestorOrSelf(
                source,
                node => new CompositionNode { Scope = node.Scope, Id = node.Id, TreeId = node.TreeId },
                (node, owner) => true),
            _ => hierarchy.WhereNearestAncestorOrSelf(
                source,
                node => new CompositionNode { Scope = node.Scope, Id = node.Id, TreeId = node.TreeId },
                (node, owner) => true, (node, owner) => true),
        });

        // Assert
        Assert.Contains("mapped", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.CommandCount);
    }

    /// <summary>A second context hidden in a payload join still fails before either context sends SQL.</summary>
    [Fact]
    public async Task NestedForeignContextRootFailsBeforeEitherReader()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var otherProbe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        await using var other = await CreateContextAsync(Guid.NewGuid(), otherProbe);
        probe.Reset();
        otherProbe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>().Where(node => node.Scope == scope)
            .Join(other.Set<CompositionNode>(), left => left.Id, right => right.Id,
                (left, right) => new { Node = left, Payload = right.Id });

        var query = hierarchy.WhereAnyAncestorOrSelf(source, row => row.Node, (row, owner) => true)
            .Select(row => row.Payload);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Contains("context", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.CommandCount);
        Assert.Equal(0, otherProbe.CommandCount);
    }

    /// <summary>Unsupported consumer predicates fail translation instead of loading candidate entities.</summary>
    /// <param name="composer">The endpoint filter containing the unsupported predicate.</param>
    [Theory]
    [InlineData(CompositionEndpointFilter.Path)]
    [InlineData(CompositionEndpointFilter.Any)]
    [InlineData(CompositionEndpointFilter.Nearest)]
    public async Task NonTranslatablePredicateFailsBeforeSql(
        CompositionEndpointFilter composer
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 3);
        var root = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 1);
        var pairs = source.Join(root, node => node.Scope, upper => upper.Scope,
            (node, upper) => new { Node = node, Upper = upper });

        var query = composer switch
        {
            CompositionEndpointFilter.Path => hierarchy.WherePathEdges(pairs, row => row.Upper, row => row.Node,
                (row, parent, child) => ClientOnlyComparison(child.Id)),
            CompositionEndpointFilter.Any => hierarchy.WhereAnyAncestorOrSelf(pairs, row => row.Node,
                (row, owner) => ClientOnlyComparison(owner.Id)),
            _ => hierarchy.WhereNearestAncestorOrSelf(pairs, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => ClientOnlyComparison(owner.Id)),
        };

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.Select(row => row.Node.Id).ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Contains("translat", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>Scalar values change bindings while the already composed SQL shape remains stable.</summary>
    [Fact]
    public async Task ChangingScalarCriteriaKeepsCommandShapeAndBindings()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var sql = new EnterpriseProbe("CompositionNodes");
        await using var context = await CreateContextAsync(scope, probe, sql: sql);
        sql.Commands.Clear();
        sql.ParameterValues.Clear();
        sql.ParameterNames.Clear();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var candidateId = 2;
        var query = hierarchy.SelectSubtrees(
                node => node.Id == candidateId,
                (upper, node, distance) => new { Upper = upper, Node = node })
            .Select(row => row.Node.Id)
            .OrderBy(id => id);

        // Act
        // WHY: Reusing the same composed query is necessary to detect a stale cached scalar binding.
        var first = await query.ToArrayAsync(CancellationToken.None);
        candidateId = 5;
        var second = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([2, 3, 4], first);
        Assert.Equal([5, 6], second);
        Assert.Equal(2, probe.CommandCount);
        Assert.Equal(2, sql.Commands.Count);
        Assert.Equal(sql.Commands[0], sql.Commands[1]);
        Assert.Equal(sql.ParameterNames[0], sql.ParameterNames[1]);
        Assert.Contains(sql.ParameterValues[0], value => value is int id && id == 2);
        Assert.Contains(sql.ParameterValues[1], value => value is int id && id == 5);
    }

    /// <summary>Intentionally has no database translation so negative tests observe the ordinary EF boundary.</summary>
    private static bool ClientOnlyComparison(
        int key
    ) => key.ToString(CultureInfo.InvariantCulture).StartsWith("10", StringComparison.Ordinal);
}
