namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects the selected adapter's exact unique-owner hydration dependency.</summary>
/// <param name="output">The sink retaining real generated SQL for native plan qualification.</param>
public sealed class SqlServerAncestorHydrationTests(ITestOutputHelper output)
{
    /// <summary>Only SQL Server selects a correlated principal lookup instead of a free mapped-owner join.</summary>
    /// <param name="engine">The actual configured provider adapter.</param>
    /// <param name="correlated">Whether principal hydration must retain a correlated one-row query block.</param>
    [Theory]
    [InlineData("SqlServer", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", false)]
    [InlineData("MySql", false)]
    [InlineData("MariaDb", false)]
    public async Task SelectedAdapterKeepsItsOwnerHydrationShape(string engine, bool correlated)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var native = engine == "Sqlite"
            ? new DbContextOptionsBuilder<CompositionContext>().UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()).Options
            : ModelCompatibilityDatabase.Options<CompositionContext>(engine);

        var options = new DbContextOptionsBuilder<CompositionContext>(
            native).AddInterceptors(boundary);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        var query = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty)
            .SelectAncestors(node => node.Id >= 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        // Act
        var sql = query.ToQueryString();
        output.WriteLine(sql);

        // Assert
        Assert.Equal(correlated, sql.Contains("CROSS APPLY", StringComparison.Ordinal));

        if (correlated)
        {
            Assert.Contains("SELECT TOP(1)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Ignoring distance in the final DTO must not restore a whole-owner window or free payload join.</summary>
    [Fact]
    public async Task UnusedPublicDistanceStillKeepsCorrelatedOwnerLookup()
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<CompositionContext>("SqlServer");
        await using var services = NestedSetTestAdapters.BuildServices(options);
        await using var context = new CompositionContext(new DbContextOptionsBuilder<CompositionContext>(options)
            .UseInternalServiceProvider(services).Options);

        var query = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty)
            .SelectAncestors(node => node.Id >= 3, (source, owner, distance) => new { Source = source.Id, Owner = owner.Id });

        // Act
        var sql = query.ToQueryString();
        output.WriteLine(sql);

        // Assert
        Assert.Contains("CROSS APPLY", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP(1)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
    }
}
