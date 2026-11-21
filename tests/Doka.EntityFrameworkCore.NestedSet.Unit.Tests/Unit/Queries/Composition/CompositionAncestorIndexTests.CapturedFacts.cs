using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Ordinary captured fact sources reach indexed SQL without any construction-time getter or I/O.</summary>
    /// <param name="engine">The configured native SQL dialect.</param>
    /// <param name="includeSelf">Whether the inclusive ordered edge is required.</param>
    /// <param name="shape">The ordinary C# query source spelling.</param>
    [Theory]
    [InlineData("MySql", false, "query")]
    [InlineData("MySql", true, "query")]
    [InlineData("MariaDb", false, "query")]
    [InlineData("MariaDb", true, "query")]
    [InlineData("MySql", false, "dbset")]
    [InlineData("MySql", true, "dbset")]
    [InlineData("MariaDb", false, "dbset")]
    [InlineData("MariaDb", true, "dbset")]
    [InlineData("MySql", false, "property")]
    [InlineData("MySql", true, "property")]
    [InlineData("MariaDb", false, "property")]
    [InlineData("MariaDb", true, "property")]
    [InlineData("MySql", false, "set")]
    [InlineData("MySql", true, "set")]
    [InlineData("MariaDb", false, "set")]
    [InlineData("MariaDb", true, "set")]
    public async Task ToQueryString_CapturedFactAncestors_UseNormalizedSourceWithoutConstructionIo(
        string engine,
        bool includeSelf,
        string shape
    )
    {
        // Arrange
        var io = new CompositionIoBoundary();
        var capture = new DeferredCompilationProbe();
        var options = new DbContextOptionsBuilder<IndexCompositionContext>(
                ModelCompatibilityDatabase.Options<IndexCompositionContext>(
                    engine,
                    "Server=127.0.0.1;Database=captured_facts;User ID=unused;Pooling=false"))
            .AddInterceptors(io, io.Commands)
            .Options;

        await using var provider = SelectionServices(options, capture);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options)
                .UseInternalServiceProvider(provider)
                .Options);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(Guid.Empty)
            .InTree(Guid.Empty);

        var selected = CapturedSelection(context, shape);
        var enabled = HasIndexedFactCapabilities(context, includeSelf);
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(
                selected,
                (node, owner, distance) => new
                {
                    node.Id,
                    Distance = distance,
                })
            : hierarchy.SelectAncestors(
                selected,
                (node, owner, distance) => new
                {
                    node.Id,
                    Distance = distance,
                });

        var getterCallsAfterConstruction = context.FactGetterCalls;
        var commandsAfterConstruction = io.Commands.Created;

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(0, getterCallsAfterConstruction);
        Assert.Equal(0, commandsAfterConstruction);
        Assert.Equal(enabled, sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(enabled && includeSelf, sql.Contains("STRAIGHT_JOIN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(shape == "property", context.FactGetterCalls > 0);
        Assert.Equal(0, io.ConnectionStarts);
        Assert.Equal(0, io.Commands.Created);
        Assert.Equal(0, capture.RetainedContextOrQueryConstants);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Rebinding an ordinary query capture changes the normalized source on every terminal attempt.</summary>
    /// <param name="includeSelf">Whether the source is its own candidate ancestor.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToQueryString_CapturedFactRebinding_RetainsDeferredValuesAndStrategy(
        bool includeSelf
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>(
            "MySql",
            "Server=127.0.0.1;Database=rebound_facts;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options)
                .UseInternalServiceProvider(provider)
                .Options);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(Guid.Empty)
            .InTree(Guid.Empty);

        var facts = context
            .Set<SelectionFact>()
            .Where(fact => fact.Id == 111);

        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(
                node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => owner.Id)
            : hierarchy.SelectAncestors(
                node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => owner.Id);

        var enabled = HasIndexedFactCapabilities(context, includeSelf);

        // Act
        facts = context
            .Set<SelectionFact>()
            .Where(fact => fact.Id == 222);

        var first = query.ToQueryString();
        facts = context
            .Set<SelectionFact>()
            .Where(fact => fact.Id == 333);

        var second = query.ToQueryString();
        facts = context
            .Set<SelectionFact>()
            .Where(fact => context
                .Set<CompositionNode>()
                .Any(hiddenHierarchy => hiddenHierarchy.Id == fact.NodeId));

        var hierarchyBearing = query.ToQueryString();

        // Assert
        Assert.Contains("222", first, StringComparison.Ordinal);
        Assert.DoesNotContain("111", first, StringComparison.Ordinal);
        Assert.Contains("333", second, StringComparison.Ordinal);
        Assert.DoesNotContain("222", second, StringComparison.Ordinal);
        Assert.Equal(enabled, first.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(enabled, second.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("WITH RECURSIVE", hierarchyBearing, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Deferred fact composition preserves both endpoint aliases for every public path and owner filter.
    /// </summary>
    /// <param name="includeSelf">Whether the first ancestor relation includes self.</param>
    /// <param name="filter">The subsequent generic hierarchy operation.</param>
    [Theory]
    [InlineData(false, "path")]
    [InlineData(true, "path")]
    [InlineData(false, "any")]
    [InlineData(true, "any")]
    [InlineData(false, "nearest")]
    [InlineData(true, "nearest")]
    public async Task ToQueryString_CapturedFactEndpoints_RemainComposable(
        bool includeSelf,
        string filter
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>(
            "MariaDb",
            "Server=127.0.0.1;Database=composed_facts;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options)
                .UseInternalServiceProvider(provider)
                .Options);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(Guid.Empty)
            .InTree(Guid.Empty);

        var facts = context.Set<SelectionFact>();
        var pairs = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(
                node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => new
                {
                    Node = node,
                    Owner = owner,
                    Distance = distance,
                })
            : hierarchy.SelectAncestors(
                node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => new
                {
                    Node = node,
                    Owner = owner,
                    Distance = distance,
                });

        var filtered = filter switch
        {
            "path" => hierarchy.WherePathEdges(
                pairs,
                row => row.Owner,
                row => row.Node,
                (row, parent, child) => child.Id > 0),
            "any" => hierarchy.WhereAnyAncestorOrSelf(
                pairs,
                row => row.Node,
                (row, owner) => owner.Id > 0),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(
                pairs,
                row => row.Node,
                (row, owner) => owner.Id > 0,
                (row, owner) => owner.Visible),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        // Act
        var sql = filtered.ToQueryString();

        // Assert
        Assert.NotEmpty(sql);
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            HasIndexedFactCapabilities(context, includeSelf),
            sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Application getter failures remain deferred and are not swallowed by source analysis.</summary>
    [Fact]
    public async Task ToQueryString_CapturedFactGetter_RemainsUnevaluatedUntilTerminalTranslation()
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>(
            "MySql",
            "Server=127.0.0.1;Database=throwing_facts;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options)
                .UseInternalServiceProvider(provider)
                .Options);

        context.ThrowFactGetter = true;
        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(Guid.Empty)
            .InTree(Guid.Empty);

        var query = hierarchy.SelectAncestorsOrSelf(
            node => context.Facts.Any(fact => fact.NodeId == node.Id),
            (node, owner, distance) => owner.Id);

        var callsAfterConstruction = context.FactGetterCalls;

        // Act
        var failure = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Equal(0, callsAfterConstruction);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("Test fact getter must remain deferred", failure.ToString(), StringComparison.Ordinal);
        Assert.True(context.FactGetterCalls > 0);
    }

    /// <summary>The EF multiple-context guard still rejects a fact source from a different context.</summary>
    [Fact]
    public async Task ToQueryString_CapturedForeignContextFacts_RemainRejectedAtTerminalTranslation()
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>(
            "MySql",
            "Server=127.0.0.1;Database=foreign_facts;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        var ownedOptions = new DbContextOptionsBuilder<IndexCompositionContext>(options)
            .UseInternalServiceProvider(provider)
            .Options;

        await using var context = new IndexCompositionContext(ownedOptions);
        await using var foreign = new IndexCompositionContext(ownedOptions);
        IQueryable<SelectionFact> facts = context.Set<SelectionFact>();
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(Guid.Empty)
            .InTree(Guid.Empty)
            .SelectAncestorsOrSelf(
                node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => owner.Id);

        // Act
        var owned = query.ToQueryString();
        facts = foreign.Set<SelectionFact>();
        var failure = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.NotEmpty(owned);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("multiple context instances", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(foreign.ChangeTracker.Entries());
    }

    /// <summary>Model-only mapping cannot use runtime queries without selecting the native provider adapter.</summary>
    [Fact]
    public async Task SelectAncestorsOrSelf_ModelOnlyRegistration_RejectsRuntimeQueryBeforeIo()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();

        // WHY: Deliberately retain model-only mapping; adding an adapter would hide the invalid runtime configuration.
        var options = new DbContextOptionsBuilder<CompositionGuardContext>()
            .UseMySql(
                "Server=127.0.0.1;Database=model_only_facts;User ID=unused;Pooling=false",
                DatabaseTestTargets.MySql)
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        await using var provider = SelectionServices(options);
        await using var context = new CompositionGuardContext(
            new DbContextOptionsBuilder<CompositionGuardContext>(options)
                .UseInternalServiceProvider(provider)
                .Options);

        var facts = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);

        // Act
        var failure = Record.Exception(() => context
            .NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping)
            .InTree(Guid.Empty)
            .SelectAncestorsOrSelf(
                node => facts.Any(fact => fact.Id == node.Id),
                (node, owner, distance) => owner.Id));

        // Assert
        var rejected = Assert.IsType<InvalidOperationException>(failure);

        Assert.Contains("matching NestedSet provider package", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets()", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Produces only ordinary compiler-generated LINQ, without embedding IQueryable.Expression.</summary>
    private static Expression<Func<CompositionNode, bool>> CapturedSelection(
        IndexCompositionContext context,
        string shape
    )
    {
        var set = context.Set<SelectionFact>();
        var query = set.Where(fact => fact.Id > 0);

        return shape switch
        {
            "query" => node => query.Any(fact => fact.NodeId == node.Id),
            "dbset" => node => set.Any(fact => fact.NodeId == node.Id),
            "property" => node => context.Facts.Any(fact => fact.NodeId == node.Id),
            "set" => node => context
                .Set<SelectionFact>()
                .Any(fact => fact.NodeId == node.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    /// <summary>Retains the existing optional-provider boundary without inventing a server dialect.</summary>
    private static bool HasIndexedFactCapabilities(
        DbContext context,
        bool includeSelf
    )
        => NestedSetProviderCapabilities.Resolve(context)
                .CompletedSourceMethod is not null
            && (!includeSelf
                || NestedSetProviderCapabilities.Resolve(context)
                    .OrderedJoinMethod is not null);
}
