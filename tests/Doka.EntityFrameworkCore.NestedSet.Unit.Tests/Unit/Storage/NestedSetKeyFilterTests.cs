namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects metadata-aware native collection eligibility and converted scalar equality.</summary>
public sealed class NestedSetKeyFilterTests
{
    /// <summary>Empty and singleton batches avoid collections while qualified native batches use membership.</summary>
    /// <param name="count">The bounded number of principal keys.</param>
    /// <param name="expected">The expected predicate category.</param>
    [Theory]
    [InlineData(0, System.Linq.Expressions.ExpressionType.Constant)]
    [InlineData(1, System.Linq.Expressions.ExpressionType.Equal)]
    [InlineData(2, System.Linq.Expressions.ExpressionType.Call)]
    [InlineData(64, System.Linq.Expressions.ExpressionType.Call)]
    public void NativeMappedBatchUsesBoundedPredicate(
        int count,
        System.Linq.Expressions.ExpressionType expected
    )
    {
        // Arrange
        using var context = CreateContext();
        var property = Property(context, nameof(KeyFilterNode.Id));
        var keys = Enumerable
            .Range(0, count)
            .ToArray();

        // Act
        var predicate = NestedSetKeyFilter<KeyFilterNode>.Matches(property, keys);

        // Assert
        Assert.Null(property.GetTypeMapping().Converter);
        Assert.Equal(expected, predicate.Body.NodeType);
    }

    /// <summary>A converter on a qualified CLR type selects scalar equality using the actual mapping.</summary>
    [Fact]
    public void ConvertedNativeClrBatchRetainsScalarEquality()
    {
        // Arrange
        using var context = CreateContext();
        var property = Property(context, nameof(KeyFilterNode.Converted));
        int[] keys = [12, 23];

        // Act
        var predicate = NestedSetKeyFilter<KeyFilterNode>.Matches(property, keys);
        var sql = context
            .Set<KeyFilterNode>()
            .Where(predicate)
            .ToQueryString();

        // Assert
        Assert.NotNull(property.GetTypeMapping().Converter);
        Assert.False(NestedSetKeyFilter<KeyFilterNode>.SupportsCollection<int>(property));
        Assert.Equal(System.Linq.Expressions.ExpressionType.OrElse, predicate.Body.NodeType);
        Assert.Contains(" OR ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
    }

    /// <summary>A string-to-string converter also excludes an otherwise qualified native CLR collection.</summary>
    [Fact]
    public void ConvertedStringBatchRetainsScalarEquality()
    {
        // Arrange
        using var context = CreateContext();
        var property = Property(context, nameof(KeyFilterNode.ConvertedText));
        string[] keys = ["first", "second"];

        // Act
        var predicate = NestedSetKeyFilter<KeyFilterNode>.Matches(property, keys);
        var sql = context
            .Set<KeyFilterNode>()
            .Where(predicate)
            .ToQueryString();

        // Assert
        Assert.NotNull(property.GetTypeMapping().Converter);
        Assert.False(NestedSetKeyFilter<KeyFilterNode>.SupportsCollection<string>(property));
        Assert.Equal(System.Linq.Expressions.ExpressionType.OrElse, predicate.Body.NodeType);
        Assert.Contains(" OR ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
    }

    /// <summary>Native PostgreSQL array keys retain scalar equality without jagged collection translation.</summary>
    [Fact]
    public void NativeArrayBatchRetainsEqualityFallback()
    {
        // Arrange
        using var context = CreateContext();
        var property = Property(context, nameof(KeyFilterNode.Array));
        int[][] keys = [[1, 2], [3, 4]];

        // Act
        var predicate = NestedSetKeyFilter<KeyFilterNode>.Matches(property, keys);

        // Assert
        Assert.Null(property.GetTypeMapping().Converter);
        Assert.False(NestedSetKeyFilter<KeyFilterNode>.SupportsCollection<int[]>(property));
        Assert.Equal(System.Linq.Expressions.ExpressionType.OrElse, predicate.Body.NodeType);
    }

    /// <summary>A generic value mismatch fails before a property expression can be translated or executed.</summary>
    [Fact]
    public void MismatchedMappedTypeIsRejected()
    {
        // Arrange
        using var context = CreateContext();
        var property = Property(context, nameof(KeyFilterNode.Id));
        long[] keys = [1, 2];

        // Act
        var error = Record.Exception(() => NestedSetKeyFilter<KeyFilterNode>.Matches(property, keys));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>The explicit scalar helper compares one captured value without a collection allocation.</summary>
    [Fact]
    public void ScalarEqualityUsesOneMappedComparison()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var predicate = NestedSetKeyFilter<KeyFilterNode>.Equal(nameof(KeyFilterNode.Id), 12);
        var sql = context
            .Set<KeyFilterNode>()
            .Where(predicate)
            .ToQueryString();

        // Assert
        Assert.Equal(System.Linq.Expressions.ExpressionType.Equal, predicate.Body.NodeType);
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
    }

    /// <summary>Finalizes real provider mappings without opening a database connection.</summary>
    private static KeyFilterContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<KeyFilterContext>().UseNpgsql(
                "Host=localhost;Database=key_filter_metadata;Username=unused;Password=unused")
            .Options;

        return new KeyFilterContext(options);
    }

    /// <summary>Resolves the exact finalized property used by each predicate case.</summary>
    private static IProperty Property(
        KeyFilterContext context,
        string name
    ) => context.Model.FindEntityType(typeof(KeyFilterNode))!.FindProperty(name)!;

    /// <summary>Provides native integer, converted integer, and native array mappings for policy boundaries.</summary>
    private sealed class KeyFilterContext(DbContextOptions<KeyFilterContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<KeyFilterNode>();
            node
                .Property(value => value.Converted)
                .HasConversion<string>();
            node
                .Property(value => value.ConvertedText)
                .HasConversion(value => value.ToUpperInvariant(), value => value);
        }
    }

    /// <summary>Separates native and converted properties while retaining real Npgsql array support.</summary>
    private sealed class KeyFilterNode
    {
        /// <summary>Gets or sets the native integer principal key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the integer value stored as text.</summary>
        public int Converted { get; set; }

        /// <summary>Gets or sets a string value stored through an explicit case-normalizing converter.</summary>
        public string ConvertedText { get; set; } = string.Empty;

        /// <summary>Gets or sets a provider-native array value.</summary>
        public int[] Array { get; set; } = [];
    }
}
