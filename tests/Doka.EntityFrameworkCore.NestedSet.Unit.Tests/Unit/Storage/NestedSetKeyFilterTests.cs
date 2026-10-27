using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects metadata-aware native collection eligibility and converted scalar equality.</summary>
public sealed class NestedSetKeyFilterTests
{
    /// <summary>Empty and singleton batches avoid collections while qualified native batches use membership.</summary>
    /// <param name="count">The bounded number of principal keys.</param>
    /// <param name="expected">The expected predicate category.</param>
    [Theory]
    [InlineData(0, ExpressionType.Constant)]
    [InlineData(1, ExpressionType.Equal)]
    [InlineData(2, ExpressionType.Call)]
    [InlineData(64, ExpressionType.Call)]
    public void NativeMappedBatchUsesBoundedPredicate(
        int count,
        ExpressionType expected
    )
    {
        // Arrange
        using var services = CreateServices();
        using var context = CreateContext(services);
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
        using var services = CreateServices();
        using var context = CreateContext(services);
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
        Assert.Equal(ExpressionType.OrElse, predicate.Body.NodeType);
        Assert.Contains(" OR ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
    }

    /// <summary>A string-to-string converter also excludes an otherwise qualified native CLR collection.</summary>
    [Fact]
    public void ConvertedStringBatchRetainsScalarEquality()
    {
        // Arrange
        using var services = CreateServices();
        using var context = CreateContext(services);
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
        Assert.Equal(ExpressionType.OrElse, predicate.Body.NodeType);
        Assert.Contains(" OR ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
    }

    /// <summary>Native PostgreSQL array keys retain scalar equality without jagged collection translation.</summary>
    [Fact]
    public void NativeArrayBatchRetainsEqualityFallback()
    {
        // Arrange
        using var services = CreateServices();
        using var context = CreateContext(services);
        var property = Property(context, nameof(KeyFilterNode.Array));
        int[][] keys = [[1, 2], [3, 4]];

        // Act
        var predicate = NestedSetKeyFilter<KeyFilterNode>.Matches(property, keys);

        // Assert
        Assert.Null(property.GetTypeMapping().Converter);
        Assert.False(NestedSetKeyFilter<KeyFilterNode>.SupportsCollection<int[]>(property));
        Assert.Equal(ExpressionType.OrElse, predicate.Body.NodeType);
    }

    /// <summary>A generic value mismatch fails before a property expression can be translated or executed.</summary>
    [Fact]
    public void MismatchedMappedTypeIsRejected()
    {
        // Arrange
        using var services = CreateServices();
        using var context = CreateContext(services);
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
        using var services = CreateServices();
        using var context = CreateContext(services);

        // Act
        var predicate = NestedSetKeyFilter<KeyFilterNode>.Equal(nameof(KeyFilterNode.Id), 12);
        var sql = context
            .Set<KeyFilterNode>()
            .Where(predicate)
            .ToQueryString();

        // Assert
        Assert.Equal(ExpressionType.Equal, predicate.Body.NodeType);
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
    }

    /// <summary>Metadata equality reuses the exact surrounding alias without invoking its CLR accessors.</summary>
    [Fact]
    public void MetadataEqualityRetainsTheSuppliedAlias()
    {
        // Arrange
        using var services = CreateServices();
        using var context = CreateContext(services);
        var property = Property(context, nameof(KeyFilterNode.Id));
        var alias = Expression.Parameter(typeof(KeyFilterNode), "existingAlias");

        // Act
        var body = NestedSetKeyFilter<KeyFilterNode>.Equal(alias, property, 12);
        var query = context.Set<KeyFilterNode>().Where(Expression.Lambda<Func<KeyFilterNode, bool>>(body, alias));
        var sql = query.ToQueryString();

        // Assert
        var equality = Assert.IsAssignableFrom<BinaryExpression>(body);
        var access = Assert.IsAssignableFrom<MemberExpression>(equality.Left);
        Assert.Same(alias, access.Expression);
        Assert.Equal(property.PropertyInfo, access.Member);
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Nullable direct-reference comparisons preserve SQL null and non-null semantics.</summary>
    /// <param name="value">The nullable mapped reference value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(12)]
    public void MetadataEqualityPreservesNullableReferences(int? value)
    {
        // Arrange
        using var services = CreateServices();
        using var context = CreateContext(services);
        var property = Property(context, nameof(KeyFilterNode.ParentId));
        var alias = Expression.Parameter(typeof(KeyFilterNode), "existingAlias");

        // Act
        var body = NestedSetKeyFilter<KeyFilterNode>.Equal(alias, property, value);
        var sql = context.Set<KeyFilterNode>().Where(Expression.Lambda<Func<KeyFilterNode, bool>>(body, alias))
            .ToQueryString();

        // Assert
        Assert.Equal(typeof(int?), property.ClrType);
        Assert.Equal(value is null, sql.Contains("IS NULL", StringComparison.Ordinal));
        Assert.Contains("\"ParentId\"", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Reused aliases retain converted value equality without equality operators.</summary>
    [Fact]
    public void MetadataEqualityRetainsOperatorFreeConversion()
    {
        // Arrange
        using var services = CreateServices();
        using var context = CreateContext(services);
        var property = Property(context, nameof(KeyFilterNode.OperatorFree));
        var alias = Expression.Parameter(typeof(KeyFilterNode), "existingAlias");

        // Act
        var body = NestedSetKeyFilter<KeyFilterNode>.Equal(alias, property, new OperatorFreeKey(12));
        var sql = context.Set<KeyFilterNode>().Where(Expression.Lambda<Func<KeyFilterNode, bool>>(body, alias))
            .ToQueryString();

        // Assert
        Assert.NotNull(property.GetTypeMapping().Converter);
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.Contains("\"OperatorFree\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The reused-alias route rejects a mismatched value before building a comparison.</summary>
    [Fact]
    public void MetadataEqualityRejectsMismatchedValueTypes()
    {
        // Arrange
        using var services = CreateServices();
        using var context = CreateContext(services);
        var property = Property(context, nameof(KeyFilterNode.Id));
        var alias = Expression.Parameter(typeof(KeyFilterNode), "existingAlias");

        // Act
        var error = Record.Exception(() => NestedSetKeyFilter<KeyFilterNode>.Equal(alias, property, 12L));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Finalizes real provider mappings without opening a database connection.</summary>
    private static KeyFilterContext CreateContext(ServiceProvider provider)
    {
        var options = new DbContextOptionsBuilder<KeyFilterContext>().UseNpgsql(
                "Host=localhost;Database=key_filter_metadata;Username=unused;Password=unused")
            .UseInternalServiceProvider(provider)
            .Options;

        return new KeyFilterContext(options);
    }

    /// <summary>Owns the public PostgreSQL services used only by this metadata probe.</summary>
    private static ServiceProvider CreateServices()
    {
        // WHY: Other test families legitimately use different provider graphs. Keeping these probes
        // outside EF's global cache makes their unchanged warning policy independent of test order.
        return new ServiceCollection().AddEntityFrameworkNpgsql().BuildServiceProvider();
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
            node.Property(value => value.OperatorFree)
                .HasConversion(value => value.Value, value => new OperatorFreeKey(value));
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

        /// <summary>Gets or sets a nullable direct reference independently of the ordinary key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets an immutable converted value that deliberately has no equality operators.</summary>
        public OperatorFreeKey OperatorFree { get; set; }
    }

    /// <summary>Exposes value equality without operator overloads for metadata-aware SQL comparison.</summary>
    private readonly struct OperatorFreeKey(int value) : IEquatable<OperatorFreeKey>
    {
        /// <summary>Gets the scalar representation used by the provider converter.</summary>
        public int Value { get; } = value;

        /// <inheritdoc />
        public bool Equals(OperatorFreeKey other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is OperatorFreeKey other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Value;
    }
}
