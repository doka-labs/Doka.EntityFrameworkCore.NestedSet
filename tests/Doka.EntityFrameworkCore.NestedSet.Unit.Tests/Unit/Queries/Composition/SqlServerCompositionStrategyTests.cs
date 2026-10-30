using System.Text.RegularExpressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects the SQL Server owner-first ancestor form and its selected adapter boundary.</summary>
public sealed partial class SqlServerCompositionStrategyTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains generated SQL as translation evidence without database execution.</summary>
    /// <param name="output">The test-owned diagnostic sink.</param>
    public SqlServerCompositionStrategyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Visible nonleaf owners drive one fenced, index-ordered source range each.</summary>
    /// <param name="treeBound">Whether the query targets one exact tree instead of the scope's forest.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictAncestorsDriveVisibleOwnersIntoFencedSourceRanges(bool treeBound)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>("SqlServer"))
            .AddInterceptors(boundary);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty);
        var query = treeBound
            ? hierarchy.InTree(Guid.Empty).SelectAncestors(node => node.Id >= 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance })
            : hierarchy.SelectAncestors(node => node.Id >= 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        // Act
        var sql = query.ToQueryString();
        _output.WriteLine(sql);
        var owner = Regex.Match(sql, @"FROM \[\w+\] AS (?<owner>\[\w+\])\s+CROSS APPLY \(").Groups["owner"].Value;
        var apply = ApplyBlock(sql);

        // Assert
        Assert.True(NestedSetProviderCapabilities.Resolve(context).UsesIndependentAncestorOwners);
        Assert.NotEmpty(owner);
        Assert.Matches(new Regex(Regex.Escape(owner) + @"\.\[Right\] > CASE"), sql);
        Assert.Matches(@"ORDER BY \[\w+\]\.\[Left\] DESC\s+OFFSET 0 ROWS", apply);
        Assert.Contains(owner + ".[Left] <", apply, StringComparison.Ordinal);
        Assert.Equal(!treeBound, apply.Contains(owner + ".[TreeId] =", StringComparison.Ordinal));
        Assert.DoesNotContain("GROUP BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP(1)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Only SQL Server selects the fenced owner-first APPLY instead of a portable range join.</summary>
    /// <param name="engine">The actual configured provider adapter.</param>
    /// <param name="fenced">Whether owners drive a correlated, fenced source range.</param>
    [Theory]
    [InlineData("SqlServer", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", false)]
    [InlineData("MySql", false)]
    [InlineData("MariaDb", false)]
    public async Task SelectedAdapterKeepsItsAncestorOwnerShape(string engine, bool fenced)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var native = engine == "Sqlite"
            ? new DbContextOptionsBuilder<CompositionContext>()
                .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()).Options
            : ModelCompatibilityDatabase.Options<CompositionContext>(engine);

        var options = new DbContextOptionsBuilder<CompositionContext>(native).AddInterceptors(boundary);
        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        var query = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty)
            .SelectAncestors(node => node.Id >= 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        // Act
        var sql = query.ToQueryString();
        _output.WriteLine(sql);

        // Assert
        Assert.Equal(fenced, sql.Contains("CROSS APPLY", StringComparison.Ordinal));
        Assert.Equal(fenced, sql.Contains("OFFSET 0 ROWS", StringComparison.Ordinal));
        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Ignoring distance in the final DTO keeps the same fenced owner-first correlation.</summary>
    [Fact]
    public async Task UnusedPublicDistanceKeepsFencedOwnerCorrelation()
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<CompositionContext>("SqlServer");
        await using var services = NestedSetTestAdapters.BuildServices(options);
        await using var context = new CompositionContext(new DbContextOptionsBuilder<CompositionContext>(options)
            .UseInternalServiceProvider(services).Options);

        var query = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty)
            .SelectAncestors(node => node.Id >= 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id });

        // Act
        var sql = query.ToQueryString();
        _output.WriteLine(sql);

        // Assert
        Assert.Contains("CROSS APPLY", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET 0 ROWS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP(1)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
    }

    /// <summary>The owner-first strategy is not an optimized reader for the optional ancestor index.</summary>
    [Fact]
    public async Task OwnerFirstAncestorsStillReportSchemaOnlyAncestorIndex()
    {
        // Arrange
        var events = new List<EventData>();
        var options = new DbContextOptionsBuilder<IndexedOwnerContext>(
                ModelCompatibilityDatabase.Options<IndexedOwnerContext>("SqlServer"))
            .LogTo((eventId, _) => eventId.Id == 70_004, events.Add);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new IndexedOwnerContext(options.UseInternalServiceProvider(services).Options);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty);

        // Act
        _ = hierarchy.SelectAncestors(node => node.Id == 3, (source, owner, distance) => owner.Id);

        // Assert
        Assert.True(NestedSetProviderCapabilities.Resolve(context).UsesIndependentAncestorOwners);
        Assert.Equal(70_004, Assert.Single(events).EventId.Id);
    }

    /// <summary>Reads the first CROSS APPLY body, respecting nested query parentheses.</summary>
    private static string ApplyBlock(string sql)
    {
        var start = sql.IndexOf("CROSS APPLY (", StringComparison.Ordinal) + "CROSS APPLY (".Length;
        var end = start;

        // WHY: The closing APPLY parenthesis needs this exact cursor and nesting depth.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var depth = 0; end < sql.Length && (depth > 0 || sql[end] != ')'); end++)
        {
            depth += sql[end] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };
        }

        return sql[start..end];
    }

    /// <summary>Requests the additive index without declaring an optimized adapter reader.</summary>
    private sealed class IndexedOwnerContext : DbContext
    {
        /// <summary>Creates the metadata-only context in its owned provider graph.</summary>
        internal IndexedOwnerContext(DbContextOptions<IndexedOwnerContext> options) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<CompositionNode>();
            node.HasKey(entity => new { entity.Scope, entity.Id });
            node.HasNestedSet(hierarchy => hierarchy
                .HasNodeKey(entity => entity.Id)
                .HasScope(entity => entity.Scope)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position)
                .HasAncestorIndex());
        }
    }
}
