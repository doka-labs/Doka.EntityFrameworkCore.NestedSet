using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerPathFailureTranslationTests
{
    /// <summary>Named shadow roles and a converted tree identity keep one exact scoped or unscoped mapping.</summary>
    /// <param name="scoped">Whether the named property-bag mapping declares an application scope.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedShadowRolesKeepLocalRelationalGrouping(bool scoped)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<NamedPathContext>("SqlServer");
        await using var services = NestedSetTestAdapters.BuildServices(options);
        await using var context = new NamedPathContext(
            new DbContextOptionsBuilder<NamedPathContext>(options).UseInternalServiceProvider(services).Options);

        var mapping = scoped ? "ScopedPaths" : "UnscopedPaths";
        var root = new NestedSetQuery<Dictionary<string, object>>(context, context.Model.FindEntityType(mapping)!);

        if (scoped)
        {
            root = root.ForScope(7);
        }

        var nodes = root.OrderedNodes;
        var rows = nodes.Where(node => EF.Property<int>(node, "Key") == 0)
            .SelectMany(_ => nodes.Where(node => EF.Property<int>(node, "Level") == 12),
                (upper, lower) => new { Upper = upper, Lower = lower });

        var query = root.WherePathEdges(rows, row => row.Upper, row => row.Lower,
            (_, parent, child) => EF.Property<bool>(child, "Open"), normalized: true);

        // Act
        var sql = query.Select(row => EF.Property<int>(row.Lower, "Key")).ToQueryString();
        var grouping = GroupingClauses(sql);
        output.WriteLine(sql);

        // Assert
        Assert.NotEmpty(grouping);
        Assert.Contains(mapping, sql, StringComparison.Ordinal);
        Assert.DoesNotContain(scoped ? "UnscopedPaths" : "ScopedPaths", sql, StringComparison.Ordinal);
        Assert.Equal(scoped, sql.Contains("[Tenant]", StringComparison.Ordinal));
        Assert.DoesNotMatch(@"MAX\s*\(\s*CASE", sql);

        foreach (var clause in grouping)
        {
            Assert.DoesNotContain("SELECT", clause, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MAX(", clause, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Maps two independent property bags with renamed shadow roles and native converted tree storage.</summary>
    private sealed class NamedPathContext(DbContextOptions<NamedPathContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var scoped in new[] { false, true })
            {
                var mapping = scoped ? "ScopedPaths" : "UnscopedPaths";
                var node = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(mapping);
                node.ToTable(mapping);
                node.Property<int>("Key").ValueGeneratedNever();
                node.Property<Guid>("Tree").HasConversion<string>();
                node.Property<int?>("Parent");
                node.Property<long>("Begin");
                node.Property<long>("End");
                node.Property<int>("Level");
                node.Property<long>("Ordinal");
                node.Property<bool>("Open");

                if (scoped)
                {
                    node.Property<int>("Tenant");
                    node.HasKey("Tenant", "Key");
                }
                else
                {
                    node.HasKey("Key");
                }

                node.HasNestedSet(hierarchy =>
                {
                    hierarchy.HasNodeKey("Key").HasTreeId("Tree").HasParent("Parent")
                        .HasBounds("Begin", "End").HasDepth("Level").HasPosition("Ordinal");

                    if (scoped)
                    {
                        hierarchy.HasScope("Tenant");
                    }
                });
            }
        }
    }
}
