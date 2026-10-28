using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerAncestorBoundsTests
{
    /// <summary>Late hydration preserves every matching owner and rejects hidden or foreign identities.</summary>
    /// <param name="ownerVariant">The adversarial change to the second equal-bound owner.</param>
    /// <param name="expectedOwners">The complete visible owner keys expected for each selected source.</param>
    [Theory]
    [InlineData("duplicate", new[] { 1, 2, 3 })]
    [InlineData("hidden", new[] { 1, 3 })]
    [InlineData("foreign-tree", new[] { 1, 3 })]
    [InlineData("foreign-scope", new[] { 1, 3 })]
    public async Task CoordinateHydrationRetainsNativeIdentityAndOccurrenceSemantics(
        string ownerVariant,
        int[] expectedOwners
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CoordinateBoundsContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets());

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CoordinateBoundsContext(options.UseInternalServiceProvider(services).Options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var duplicate = BoundsNode(2, 1, 10);
        duplicate.TreeId = "A";
        duplicate.Visible = ownerVariant != "hidden";
        duplicate.TreeId = ownerVariant == "foreign-tree" ? "b" : duplicate.TreeId;
        duplicate.Scope = ownerVariant == "foreign-scope" ? 8 : 7;

        await context.AddRangeAsync(
            [
                BoundsNode(1, 1, 10), duplicate, BoundsNode(3, 2, 7, parent: 1, depth: 1),
                BoundsNode(4, 3, 4, parent: 3, depth: 2),
                BoundsNode(5, 1, 10, visible: false), BoundsNode(6, 1, 10, tree: "b"),
                BoundsNode(7, 1, 10, scope: 8), BoundsNode(8, 5, 6, parent: 3, depth: 2),
            ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var root = new NestedSetQuery<CoordinateBoundsNode>(context,
            context.Model.FindEntityType(typeof(CoordinateBoundsNode))!).ForScope(7);

        // Act
        var rows = await CoordinatePairs(root, node => node.Depth == 2).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2 * expectedOwners.Length, rows.Length);
        Assert.Equal(expectedOwners, rows.Where(row => row.Source.Value == 4)
            .Select(row => row.Owner.Value).Order().ToArray());
        Assert.Equal(expectedOwners, rows.Where(row => row.Source.Value == 8)
            .Select(row => row.Owner.Value).Order().ToArray());
        Assert.All(rows, row => Assert.Equal(row.Owner.Value == 3 ? 1 : 2, row.Distance));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An empty source remains empty even when independently fenced owner coordinates exist.</summary>
    [Fact]
    public async Task CoordinateHydrationDoesNotInventSources()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CoordinateBoundsContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets());

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CoordinateBoundsContext(options.UseInternalServiceProvider(services).Options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await context.AddRangeAsync([BoundsNode(1, 1, 10)], CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var root = new NestedSetQuery<CoordinateBoundsNode>(context,
            context.Model.FindEntityType(typeof(CoordinateBoundsNode))!).ForScope(7);

        // Act
        var rows = await CoordinatePairs(root, node => node.Depth == 2).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(rows);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Repairable extreme and reversed intervals keep the existing overflow-safe nonleaf contract.</summary>
    [Fact]
    public async Task CoordinateHydrationPreservesExtremeIntervalClassification()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CoordinateBoundsContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets());

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CoordinateBoundsContext(options.UseInternalServiceProvider(services).Options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);

        // WHY: These fixture-owned corrupt intervals model repair queries. Only this isolated
        // connection disables CHECK enforcement; production schema and query guards remain unchanged.
        await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON", CancellationToken.None);
        await context.AddRangeAsync(
            [
                BoundsNode(1, 1, 10), BoundsNode(2, long.MinValue, long.MaxValue),
                BoundsNode(3, long.MaxValue, long.MaxValue), BoundsNode(4, long.MaxValue - 1, long.MaxValue),
                BoundsNode(5, 9, 1), BoundsNode(6, 3, 4, parent: 1, depth: 1),
            ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var root = new NestedSetQuery<CoordinateBoundsNode>(context,
            context.Model.FindEntityType(typeof(CoordinateBoundsNode))!).ForScope(7);

        // Act
        var rows = await CoordinatePairs(root, node => node.Depth == 1).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2], rows.Select(row => row.Owner.Value).Order().ToArray());
        Assert.All(rows, row => Assert.Equal(6, row.Source.Value));
        Assert.All(rows, row => Assert.Equal(1, row.Distance));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Executes the exact portable strategy without changing SQLite's advertised adapter capability.</summary>
    private static IQueryable<BoundsResult> CoordinatePairs(
        NestedSetQuery<CoordinateBoundsNode> root,
        Expression<Func<CoordinateBoundsNode, bool>> selected
    )
    {
        // WHY: Native SQL Server execution is unavailable on this host. SQLite executes this exact
        // core relation for semantic controls; the separate SQL Server tests inspect its real adapter SQL.
        var method = root.GetType().GetMethod("SelectCoordinateAncestorPairs",
            BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(typeof(BoundsResult));

        Expression<Func<CoordinateBoundsNode, CoordinateBoundsNode, int, BoundsResult>> projection =
            (source, owner, distance) => new BoundsResult(source.Id, owner.Id, distance);

        return Assert.IsAssignableFrom<IQueryable<BoundsResult>>(method.Invoke(root, [selected, projection]));
    }

    /// <summary>Creates independent synthetic coordinates with explicit scope, tree and visibility controls.</summary>
    private static CoordinateBoundsNode BoundsNode(
        int id,
        long left,
        long right,
        int? parent = null,
        int depth = 0,
        bool visible = true,
        string tree = "a",
        int scope = 7
    ) => new()
    {
        Id = new BoundsKey(id), Scope = scope, TreeId = tree,
        ParentId = parent.HasValue ? new BoundsKey(parent.Value) : null,
        Left = left, Right = right, Depth = depth, Visible = visible,
    };

    /// <summary>Combines an operator-free converted key with database-native case-insensitive tree identity.</summary>
    private sealed class CoordinateBoundsContext(DbContextOptions<CoordinateBoundsContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<CoordinateBoundsNode>();
            var converter = new ValueConverter<BoundsKey, int>(key => key.Value, value => new BoundsKey(value));
            node.HasKey(entity => new { entity.Scope, entity.Id });
            node.Property(entity => entity.Id).HasConversion(converter).ValueGeneratedNever();
            node.Property(entity => entity.ParentId).HasConversion(converter);
            node.Property(entity => entity.TreeId).HasMaxLength(40).UseCollation("NOCASE");
            node.HasQueryFilter(entity => entity.Visible);
            node.HasNestedSet(hierarchy => hierarchy
                .HasNodeKey(entity => entity.Id)
                .HasScope(entity => entity.Scope)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position));
        }
    }

    /// <summary>Provides a converted identity without a CLR equality operator.</summary>
    private readonly struct BoundsKey(int value)
    {
        /// <summary>Gets the synthetic scalar identity.</summary>
        public int Value { get; } = value;
    }

    /// <summary>Retains the caller occurrence and mapped owner without converting their identities in SQL.</summary>
    private sealed record BoundsResult(BoundsKey Source, BoundsKey Owner, int Distance);

    /// <summary>Represents only the structural roles needed by the coordinate relation's adversarial controls.</summary>
    private sealed class CoordinateBoundsNode
    {
        /// <summary>Gets or sets the independently converted node key.</summary>
        public BoundsKey Id { get; set; }

        /// <summary>Gets or sets the application partition.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the database-collated tree identity.</summary>
        public string TreeId { get; set; } = "a";

        /// <summary>Gets or sets the optional directly enclosing key.</summary>
        public BoundsKey? ParentId { get; set; }

        /// <summary>Gets or sets the opening bound.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the closing bound.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the structural depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets whether the application's visibility filter admits the owner.</summary>
        public bool Visible { get; set; } = true;
    }
}
