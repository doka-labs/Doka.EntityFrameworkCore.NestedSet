namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Directs final owner payload after candidates while keeping source hydration freely reorderable.</summary>
    /// <param name="engine">The configured Doka engine.</param>
    /// <param name="scoped">Whether complete identity includes an application scope.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task InclusiveOwnerHydrationHasItsOwnDirectedSqlEdge(string engine, bool scoped)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<OwnerHydrationContext>(engine,
            "Server=127.0.0.1;Database=owner_hydration;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new OwnerHydrationContext(
            new DbContextOptionsBuilder<OwnerHydrationContext>(options).UseInternalServiceProvider(provider).Options);

        var mapping = scoped ? OwnerHydrationContext.Scoped : OwnerHydrationContext.Unscoped;
        var hierarchy = scoped
            ? context.NestedSet<CompositionNode>(mapping).ForScope(Guid.Empty).InTree(Guid.Empty)
            : context.NestedSet<CompositionNode>(mapping).InTree(Guid.Empty);

        var query = hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
            (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        var enabled = HasIndexedFactCapabilities(context, includeSelf: true);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(enabled ? 2 : 0, sql.Split("STRAIGHT_JOIN", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("SELECT STRAIGHT_JOIN", sql, StringComparison.OrdinalIgnoreCase);

        if (enabled)
        {
            var joins = AncestorIndexHydrationJoins.Read(sql, mapping);

            // WHY: Counting two directed joins alone could also pass if source hydration were
            // constrained instead. Follow the actual public projection aliases to each mapped edge.
            Assert.Equal("INNER JOIN", joins.Source);
            Assert.Equal("STRAIGHT_JOIN", joins.Owner);
        }

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Provides equivalent scoped and unscoped mappings without inheriting a different test model.</summary>
    private sealed class OwnerHydrationContext(DbContextOptions<OwnerHydrationContext> options) : DbContext(options)
    {
        /// <summary>Names the hierarchy whose identity includes Scope, TreeId and NodeKey.</summary>
        internal const string Scoped = "ScopedOwnerHydration";

        /// <summary>Names the hierarchy whose identity includes TreeId and NodeKey.</summary>
        internal const string Unscoped = "UnscopedOwnerHydration";

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            Configure(modelBuilder, Scoped, scoped: true);
            Configure(modelBuilder, Unscoped, scoped: false);
        }

        /// <summary>Maps all payload and hierarchy fields explicitly so SQL filters remain real consumer behavior.</summary>
        private static void Configure(ModelBuilder modelBuilder, string mapping, bool scoped)
        {
            var node = modelBuilder.SharedTypeEntity<CompositionNode>(mapping);
            node.ToTable(mapping);

            if (scoped)
            {
                node.HasKey(value => new { value.Scope, value.Id });
            }
            else
            {
                node.HasKey(value => value.Id);
            }

            node.Property(value => value.Id).ValueGeneratedNever();
            node.Property(value => value.Visible);
            node.HasQueryFilter(value => value.Visible);
            node.HasNestedSet(hierarchy =>
            {
                hierarchy.HasNodeKey(value => value.Id).HasTreeId(value => value.TreeId)
                    .HasParent(value => value.ParentId).HasBounds(value => value.Left, value => value.Right)
                    .HasDepth(value => value.Depth).HasPosition(value => value.Position).HasAncestorIndex("HasChildren");

                if (scoped)
                {
                    hierarchy.HasScope(value => value.Scope);
                }
            });
        }
    }
}
