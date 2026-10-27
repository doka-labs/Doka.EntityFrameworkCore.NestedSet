namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks exact selected mapping identity without rejecting unrelated entities carried as payload.</summary>
public sealed class CompositionEndpointTests
{
    /// <summary>Ordinary projections preserve endpoint aliases and may carry another mapping as payload.</summary>
    /// <param name="shape">The ordinary projection carrying the endpoint.</param>
    /// <param name="operation">The composition filter being checked.</param>
    [Theory]
    [InlineData("anonymous", "path")]
    [InlineData("anonymous", "any")]
    [InlineData("anonymous", "nearest")]
    [InlineData("initializer", "path")]
    [InlineData("initializer", "any")]
    [InlineData("initializer", "nearest")]
    [InlineData("join-payload", "path")]
    [InlineData("join-payload", "any")]
    [InlineData("join-payload", "nearest")]
    [InlineData("select-many-payload", "path")]
    [InlineData("select-many-payload", "any")]
    [InlineData("select-many-payload", "nearest")]
    public void MappedAliasesAllowIndependentPayloadRoots(
        string shape,
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = shape switch
        {
            "anonymous" => first.Select(node => new { Endpoint = node })
                .Select(row => new CompositionGuardRow { Upper = row.Endpoint, Lower = row.Endpoint }),
            "initializer" => first.Select(node => new CompositionGuardRow { Upper = node, Lower = node }),
            "join-payload" => first.Join(second, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            "select-many-payload" => first.SelectMany(node => second.Where(payload => payload.Id == node.Id),
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var query = Filter(hierarchy, source, operation);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A different named mapping cannot supply an endpoint despite identical CLR types and keys.</summary>
    /// <param name="operation">The composition filter whose endpoint is deliberately foreign.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void SameClrTypeDoesNotMakeNamedMappingsInterchangeable(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = first.Join(second, node => node.Id, other => other.Id,
            (node, other) => new CompositionGuardRow { Upper = node, Lower = other });

        // Act
        var error = Record.Exception(() => Filter(hierarchy, source, operation));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Contains(CompositionGuardContext.FirstMapping, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Copying a mapped key into a newly constructed entity never creates a mapped endpoint alias.</summary>
    /// <param name="operation">The composition filter whose lower endpoint is fabricated.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void ConstructedEntityCannotImpersonateMappedEndpoint(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var source = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping)
            .Select(node => new CompositionGuardRow
            {
                Upper = node,
                Lower = new CompositionGuardNode { Id = node.Id },
            });

        // Act
        var error = Record.Exception(() => Filter(hierarchy, source, operation));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A concrete TPC hierarchy accepts its actual alias after a base query is narrowed with OfType.</summary>
    [Fact]
    public void ConcreteTpcAliasRetainsMappingThroughOfType()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<ConcreteTpcContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        using var context = new ConcreteTpcContext(options);
        var hierarchy = context.NestedSet<TpcFolderNode>();
        var source = context.Set<TpcNode>().OfType<TpcFolderNode>();

        // Act
        var query = hierarchy.WhereAnyAncestorOrSelf(source, node => node, (row, owner) => owner.Id == row.Id);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A derived TPH alias belongs to the hierarchy configured on its base entity.</summary>
    [Fact]
    public void DerivedTphAliasRetainsConfiguredBaseHierarchy()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<TphContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        using var context = new TphContext(options);
        var hierarchy = context.NestedSet<InheritanceNode>();
        var source = context.Set<TphFolderNode>();

        // Act
        var query = hierarchy.WhereAnyAncestorOrSelf(source, node => node, (row, owner) => owner.Id == row.Id);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Calls the actual public endpoint filter selected by a test case.</summary>
    internal static IQueryable<CompositionGuardRow> Filter(
        NestedSet<CompositionGuardNode> hierarchy,
        IQueryable<CompositionGuardRow> source,
        string operation
    ) => operation switch
    {
        "path" => hierarchy.WherePathEdges(
            source, row => row.Upper, row => row.Lower, (row, parent, child) => parent.Id != child.Id),
        "any" => hierarchy.WhereAnyAncestorOrSelf(source, row => row.Lower, (row, owner) => owner.Id == row.Upper.Id),
        "nearest" => hierarchy.WhereNearestAncestorOrSelf(source, row => row.Lower, (row, owner) => owner.Depth == 1,
            (row, owner) => owner.Id == row.Upper.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
