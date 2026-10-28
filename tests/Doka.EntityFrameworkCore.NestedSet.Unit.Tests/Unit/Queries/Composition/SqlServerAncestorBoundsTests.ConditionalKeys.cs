using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerAncestorBoundsTests
{
    /// <summary>Operator-free converted keys retain native CASE and equality without an index-defeating CAST.</summary>
    [Fact]
    public async Task ConvertedOwnerKeyRetainsNativeConditionalSql()
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<CoordinateBoundsContext>("SqlServer");
        await using var services = NestedSetTestAdapters.BuildServices(options);
        await using var context = new CoordinateBoundsContext(
            new DbContextOptionsBuilder<CoordinateBoundsContext>(options).UseInternalServiceProvider(services).Options);

        var query = context.NestedSet<CoordinateBoundsNode>().ForScope(7)
            .SelectAncestors(node => node.Depth >= 1, (source, owner, distance) => owner.Id);

        // Act
        var sql = query.ToQueryString();

        // Assert
        AssertConditionalNativeKey(sql);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("THEN", sql, StringComparison.Ordinal);
        Assert.Contains("END AS [Key]", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"CAST\(\s*\[[^]]+\]\.\[Id\]|CONVERT\([^)]*\[[^]]+\]\.\[Id\]", sql);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Binary-collated text and binary keys retain their native SQL representation through CASE.</summary>
    /// <param name="binary">Whether the mapped key is binary rather than binary-collated text.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeOwnerKeyRetainsConditionalSqlWithoutTextConversion(bool binary)
    {
        // Arrange
        Func<Task<string>> translate = binary ? NativeConditionalSqlAsync<byte[]> : NativeConditionalSqlAsync<string>;

        // Act
        var sql = await translate();

        // Assert
        AssertConditionalNativeKey(sql);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("END AS [Key]", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"CAST\(\s*\[[^]]+\]\.\[Id\]|CONVERT\([^)]*\[[^]]+\]\.\[Id\]", sql);
    }

    /// <summary>Translates the exact selected SQL Server strategy using native reference-valued keys.</summary>
    private static async Task<string> NativeConditionalSqlAsync<TKey>() where TKey : class
    {
        var options = ModelCompatibilityDatabase.Options<NativeConditionalContext<TKey>>("SqlServer");
        await using var services = NestedSetTestAdapters.BuildServices(options);
        await using var context = new NativeConditionalContext<TKey>(
            new DbContextOptionsBuilder<NativeConditionalContext<TKey>>(options)
                .UseInternalServiceProvider(services).Options);

        return context.NestedSet<NativeConditionalNode<TKey>>().ForScope(7)
            .SelectAncestors(node => node.Depth >= 1, (source, owner, distance) => owner.Id).ToQueryString();
    }

    /// <summary>Checks the actual eligibility CASE and native key branch independently of select-list ordering.</summary>
    private static void AssertConditionalNativeKey(string sql)
        => Assert.Matches(new Regex(
            @"CASE\s+WHEN\s+(?<owner>\[[^]]+\])\.\[Right\]\s*>\s*CASE\b.*?\bEND\s+THEN\s+\k<owner>\.\[Id\]\s+END AS \[Key\]",
            RegexOptions.Singleline), sql);

    /// <summary>Maps only native identity roles needed to qualify the conditional ancestor key.</summary>
    private sealed class NativeConditionalContext<TKey>(DbContextOptions<NativeConditionalContext<TKey>> options)
        : DbContext(options) where TKey : class
    {
        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<NativeConditionalNode<TKey>>();
            node.HasKey(entity => new { entity.Scope, entity.Id });
            node.Property(entity => entity.Id).ValueGeneratedNever().HasMaxLength(40);
            node.Property(entity => entity.ParentId).HasMaxLength(40);

            if (typeof(TKey) == typeof(string))
            {
                node.Property(entity => entity.Id).UseCollation("Latin1_General_100_BIN2");
                node.Property(entity => entity.ParentId).UseCollation("Latin1_General_100_BIN2");
            }

            node.HasNestedSet(hierarchy => hierarchy.HasNodeKey(entity => entity.Id)
                .HasScope(entity => entity.Scope).HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId).HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth).HasPosition(entity => entity.Position));
        }
    }

    /// <summary>Supplies strongly mapped reference-valued keys without implicit string conversion.</summary>
    private sealed class NativeConditionalNode<TKey> where TKey : class
    {
        /// <summary>Gets or sets the native node identity.</summary>
        public required TKey Id { get; set; }

        /// <summary>Gets or sets the independent scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the exact tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional native parent identity.</summary>
        public TKey? ParentId { get; set; }

        /// <summary>Gets or sets the opening bound.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the closing bound.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the structural depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }
}
