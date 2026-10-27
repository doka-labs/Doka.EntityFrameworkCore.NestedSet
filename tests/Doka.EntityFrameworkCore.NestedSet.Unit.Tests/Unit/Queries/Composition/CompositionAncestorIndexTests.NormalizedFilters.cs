using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Normalized filters match the explicit-root control without adding uncertain endpoint membership.</summary>
    /// <param name="engine">The configured relational translator.</param>
    /// <param name="operation">The source-preserving hierarchy filter.</param>
    /// <param name="ignoreFilters">Whether a rebound capture must retain query-wide modifier handling.</param>
    [Theory]
    [InlineData("MySql", "path", false)]
    [InlineData("MySql", "any", false)]
    [InlineData("MySql", "nearest", false)]
    [InlineData("MariaDb", "path", false)]
    [InlineData("MariaDb", "any", false)]
    [InlineData("MariaDb", "nearest", false)]
    [InlineData("Sqlite", "path", false)]
    [InlineData("Sqlite", "any", false)]
    [InlineData("Sqlite", "nearest", false)]
    [InlineData("PostgreSql", "path", false)]
    [InlineData("PostgreSql", "any", false)]
    [InlineData("PostgreSql", "nearest", false)]
    [InlineData("SqlServer", "path", false)]
    [InlineData("SqlServer", "any", false)]
    [InlineData("SqlServer", "nearest", false)]
    [InlineData("MySql", "path", true)]
    [InlineData("MySql", "any", true)]
    [InlineData("MySql", "nearest", true)]
    [InlineData("MariaDb", "path", true)]
    [InlineData("MariaDb", "any", true)]
    [InlineData("MariaDb", "nearest", true)]
    [InlineData("Sqlite", "path", true)]
    [InlineData("Sqlite", "any", true)]
    [InlineData("Sqlite", "nearest", true)]
    [InlineData("PostgreSql", "path", true)]
    [InlineData("PostgreSql", "any", true)]
    [InlineData("PostgreSql", "nearest", true)]
    [InlineData("SqlServer", "path", true)]
    [InlineData("SqlServer", "any", true)]
    [InlineData("SqlServer", "nearest", true)]
    public Task NormalizedFiltersRetainExplicitSourceSql(string engine, string operation, bool ignoreFilters)
        => CompareNormalizedFilterSqlAsync<IndexCompositionContext>(engine, operation, ignoreFilters,
            static options => new IndexCompositionContext(options));

    /// <summary>Doka also retains exact normalized visibility without opting into the optional ancestor index.</summary>
    /// <param name="engine">The configured Doka engine.</param>
    /// <param name="operation">The source-preserving hierarchy filter.</param>
    /// <param name="ignoreFilters">Whether the capture is rebound to a query-wide filter modifier.</param>
    [Theory]
    [InlineData("MySql", "path", false)]
    [InlineData("MySql", "any", false)]
    [InlineData("MySql", "nearest", false)]
    [InlineData("MariaDb", "path", false)]
    [InlineData("MariaDb", "any", false)]
    [InlineData("MariaDb", "nearest", false)]
    [InlineData("MySql", "path", true)]
    [InlineData("MySql", "any", true)]
    [InlineData("MySql", "nearest", true)]
    [InlineData("MariaDb", "path", true)]
    [InlineData("MariaDb", "any", true)]
    [InlineData("MariaDb", "nearest", true)]
    public Task NormalizedNonIndexedDokaFiltersRetainExplicitSourceSql(string engine, string operation, bool ignoreFilters)
        => CompareNormalizedFilterSqlAsync<NonIndexedCompositionContext>(engine, operation, ignoreFilters,
            static options => new NonIndexedCompositionContext(options));

    /// <summary>Compares the deferred public-method core with the same existing eager explicit-root composition.</summary>
    private static async Task CompareNormalizedFilterSqlAsync<TContext>(
        string engine,
        string operation,
        bool ignoreFilters,
        Func<DbContextOptions<TContext>, TContext> create
    )
        where TContext : DbContext
    {
        // Arrange
        var connection = engine switch
        {
            "Sqlite" => "Data Source=:memory:",
            "PostgreSql" => "Host=localhost;Database=normalized_filters;Username=unused",
            _ => "Server=127.0.0.1;Database=normalized_filters;User ID=unused;Pooling=false",
        };

        var options = ModelCompatibilityDatabase.Options<TContext>(engine, connection);
        await using var provider = SelectionServices(options);
        await using var context = create(
            new DbContextOptionsBuilder<TContext>(options).UseInternalServiceProvider(provider).Options);

        var hierarchy = new NestedSetQuery<CompositionNode>(context, context.Model.FindEntityType(typeof(CompositionNode))!)
            .ForScope(Guid.Empty);

        IQueryable<SelectionFact> facts = context.Set<SelectionFact>();
        var explicitFacts = ignoreFilters ? facts.IgnoreQueryFilters() : facts;
        var selected = FactSelection(explicitFacts, (node, fact) => fact.NodeId == node.Id);
        var ordinary = hierarchy.SelectAncestorsOrSelf(node => facts.Any(fact => fact.NodeId == node.Id),
            (node, owner, distance) => new { Node = node, Owner = owner, Distance = distance });

        // WHY: The control deliberately composes before EF extraction, as an already explicit source
        // did before deferral. Routing both sides through markers could hide identical extra membership.
        var explicitRoot = hierarchy.SelectNormalizedAncestors(selected,
            (node, owner, distance) => new { Node = node, Owner = owner, Distance = distance }, includeSelf: true);

        var queries = new[] { ordinary, explicitRoot }.Select((source, index) => operation switch
        {
            "path" => hierarchy.WherePathEdges(source, row => row.Owner, row => row.Node,
                (row, parent, child) => child.Id > 0, normalized: index == 1),
            "any" => hierarchy.WhereAnyAncestorOrSelf(source, row => row.Node,
                (row, owner) => owner.Id > 0, normalized: index == 1),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(source, row => row.Node,
                (row, owner) => owner.Id > 0, (row, owner) => owner.Visible, normalized: index == 1),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        }).Select(query => query.Select(row => row.Owner.Id)).ToArray();

        // Act
        if (ignoreFilters)
        {
            facts = facts.IgnoreQueryFilters();
        }

        var actual = queries[0].ToQueryString();
        var expected = queries[1].ToQueryString();

        // Assert
        Assert.Equal(expected, actual);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Uses a distinct real EF model identity with the same roles and no ancestor-index annotation.</summary>
    private sealed class NonIndexedCompositionContext(DbContextOptions<NonIndexedCompositionContext> options)
        : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<CompositionNode>();
            node.ToTable("CompositionNonIndexedNodes");
            node.HasKey(entity => new { entity.Scope, entity.Id });
            node.Property(entity => entity.Id).ValueGeneratedNever();
            node.Property(entity => entity.Visible);
            node.HasQueryFilter(entity => entity.Visible);
            node.HasNestedSet(mapping => mapping
                .HasNodeKey(entity => entity.Id)
                .HasScope(entity => entity.Scope)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position));

            var fact = modelBuilder.Entity<SelectionFact>();
            fact.HasKey(entity => entity.Id);
            fact.HasQueryFilter(entity => entity.Visible);
            fact.HasOne(entity => entity.Node).WithMany()
                .HasForeignKey(entity => new { entity.Scope, Id = entity.NodeId });
        }
    }
}
