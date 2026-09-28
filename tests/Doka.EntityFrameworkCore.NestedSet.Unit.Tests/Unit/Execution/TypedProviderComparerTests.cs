namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects typed identity comparisons from unnecessary per-pair allocations.</summary>
[Collection("Allocation measurements")]
public sealed class TypedProviderComparerTests
{
    /// <summary>Receives isolated allocation evidence for the current test.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the comparer regression fixture.</summary>
    /// <param name="output">The current test's allocation evidence sink.</param>
    public TypedProviderComparerTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Retains native equality and hashing without boxing every identity comparison.</summary>
    /// <param name="role">The native scalar or present nullable-parent representation.</param>
    [Theory]
    [InlineData("Int")]
    [InlineData("Guid")]
    [InlineData("NullableParent")]
    public void NativeComparisonsDoNotAllocatePerPair(
        string role
    )
    {
        // Arrange
        using var context = new ComparerContext();
        var metadata = context.Model.FindEntityType(typeof(ComparerNode))!;
        var integer = new NestedSetProviderComparer<int>(metadata.FindProperty(nameof(ComparerNode.Id))!);
        var parent = new NestedSetProviderComparer<int>(metadata.FindProperty(nameof(ComparerNode.ParentId))!);
        var tree = new NestedSetProviderComparer<Guid>(metadata.FindProperty(nameof(ComparerNode.TreeId))!);
        var id = Guid.Parse("74100000-0000-0000-0000-000000000001");
        Func<bool> compare = role switch
        {
            "Int" => () => integer.Equals(0, 0) && !integer.Equals(0, 1),
            "Guid" => () => tree.Equals(id, id) && !tree.Equals(id, Guid.Empty),
            "NullableParent" => () => parent.Equals(0, 0) && !parent.Equals(0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        _ = compare();
        var correct = true;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < 20000; index++)
        {
            correct &= compare();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.True(correct);

        // WHY: Model construction and cold comparer creation are outside the measurement. This small fixed
        // allowance tolerates runtime setup, while even one 24-byte box per pair exceeds it by an order of magnitude.
        Assert.InRange(allocated, 0, 16384);
        Assert.Equal(integer.GetHashCode(0), parent.GetHashCode(0));
    }

    /// <summary>Preserves converted equality without boxing twice before the required converter boundary.</summary>
    [Fact]
    public void ConvertedComparisonsAllocateOnlyTheNecessaryConverterBoundary()
    {
        // Arrange
        using var context = new ComparerContext();
        var metadata = context.Model.FindEntityType(typeof(ComparerNode))!;
        var comparer = new NestedSetProviderComparer<ConvertedIdentity>(
            metadata.FindProperty(nameof(ComparerNode.ConvertedId))!);

        var first = new ConvertedIdentity(17);
        var equalProvider = new ConvertedIdentity(27);
        var differentProvider = new ConvertedIdentity(18);
        const int iterations = 20000;

        _ = comparer.Matches(first, equalProvider);
        _ = comparer.Matches(first, differentProvider);
        _ = comparer.Equals(first, equalProvider);
        _ = comparer.Equals(first, differentProvider);
        var baselineCorrect = true;
        var typedCorrect = true;

        // Act
        var baselineBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < iterations; index++)
        {
            baselineCorrect &= comparer.Matches(first, equalProvider) && !comparer.Matches(first, differentProvider);
        }

        var baselineAllocated = GC.GetAllocatedBytesForCurrentThread() - baselineBefore;
        var typedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < iterations; index++)
        {
            typedCorrect &= comparer.Equals(first, equalProvider) && !comparer.Equals(first, differentProvider);
        }

        var typedAllocated = GC.GetAllocatedBytesForCurrentThread() - typedBefore;

        // Assert
        _output.WriteLine($"Converted comparer: baseline={baselineAllocated} B; typed={typedAllocated} B.");
        Assert.True(baselineCorrect);
        Assert.True(typedCorrect);
        Assert.NotEqual(first, equalProvider);
        Assert.Equal(comparer.GetHashCode(first), comparer.GetHashCode(equalProvider));

        // WHY: Direct Matches includes both required input boxes and the identical configured converter work.
        // A fixed setup allowance avoids object-layout assumptions while rejecting redundant boxes per pair.
        Assert.InRange(typedAllocated, 0, baselineAllocated + 16384);
    }

    /// <summary>Supplies actual native and nullable relational property metadata without opening a database.</summary>
    private sealed class ComparerContext : DbContext
    {
        /// <summary>Creates a metadata-only context for the real provider comparer.</summary>
        internal ComparerContext() : base(
            new DbContextOptionsBuilder<ComparerContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<ComparerNode>();
            node
                .Property(entity => entity.Id)
                .ValueGeneratedNever();

            // WHY: Distinct model values intentionally share a normalized provider representation, so skipping
            // the actual configured converter would fail the equality control as well as the allocation budget.
            node
                .Property(entity => entity.ConvertedId)
                .HasConversion(value => value.Value % 10, value => new ConvertedIdentity(value));
        }
    }

    /// <summary>Separates the native scalar key, nullable parent storage and non-null Guid identity.</summary>
    private sealed class ComparerNode
    {
        /// <summary>Gets or sets the assigned native integer key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the nullable integer parent.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the native Guid identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the converted scalar whose provider representation is normalized.</summary>
        public ConvertedIdentity ConvertedId { get; set; }
    }

    /// <summary>Separates model equality from the explicitly configured normalized integer representation.</summary>
    /// <param name="Value">The original model identity.</param>
    private readonly record struct ConvertedIdentity(int Value);
}
