using System.Collections;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects binary provider equality and bounded structural hashing without boxed bytes.</summary>
[Collection("Allocation measurements")]
public sealed class BinaryProviderComparerTests
{
    /// <summary>Distinct model values compare and hash through their complete binary provider representation.</summary>
    /// <param name="shape">The native, converted, or metadata-erased comparer route.</param>
    [Theory]
    [InlineData("native")]
    [InlineData("erased")]
    [InlineData("converted")]
    [InlineData("converted-erased")]
    public void EqualProviderValuesShareEqualityAndHash(
        string shape
    )
    {
        // Arrange
        using var context = new BinaryContext();
        var operations = Operations(context, shape);
        var first = operations.Model([1, 2, 3, 4], 10)!;
        var second = operations.Model([1, 2, 3, 4], 20)!;

        // Act
        var equal = operations.Compare(first, second);
        var firstHash = operations.Hash(first);
        var secondHash = operations.Hash(second);

        // Assert
        Assert.NotSame(first, second);
        Assert.True(equal);
        Assert.Equal(firstHash, secondHash);
    }

    /// <summary>Equality examines prefix, suffix and length even when bounded structural hashes collide.</summary>
    /// <param name="shape">The configured comparer route.</param>
    /// <param name="change">The independently changed part of the provider value.</param>
    [Theory]
    [InlineData("native", "prefix")]
    [InlineData("native", "suffix")]
    [InlineData("native", "length")]
    [InlineData("erased", "prefix")]
    [InlineData("erased", "suffix")]
    [InlineData("erased", "length")]
    [InlineData("converted", "prefix")]
    [InlineData("converted", "suffix")]
    [InlineData("converted", "length")]
    [InlineData("converted-erased", "prefix")]
    [InlineData("converted-erased", "suffix")]
    [InlineData("converted-erased", "length")]
    public void DifferentProviderValuesRemainUnequal(
        string shape,
        string change
    )
    {
        // Arrange
        using var context = new BinaryContext();
        var operations = Operations(context, shape);
        var firstBytes = Enumerable.Repeat((byte)7, 32).ToArray();
        var secondBytes = firstBytes.ToArray();

        switch (change)
        {
            case "prefix":
                secondBytes[0] = 9;
                break;
            case "suffix":
                secondBytes[^1] = 9;
                break;
            case "length":
                secondBytes = secondBytes[..^1];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        var first = operations.Model(firstBytes, 10)!;
        var second = operations.Model(secondBytes, 20)!;

        // Act
        var equal = operations.Compare(first, second);

        // Assert
        Assert.False(equal);
        Assert.Equal(
            StructuralComparisons.StructuralEqualityComparer.GetHashCode(firstBytes),
            operations.Hash(first));
        Assert.Equal(
            StructuralComparisons.StructuralEqualityComparer.GetHashCode(secondBytes),
            operations.Hash(second));
    }

    /// <summary>Null and same-reference controls retain the existing pre-conversion equality boundary.</summary>
    /// <param name="shape">The configured comparer route.</param>
    [Theory]
    [InlineData("native")]
    [InlineData("erased")]
    [InlineData("converted")]
    [InlineData("converted-erased")]
    public void NullAndReferenceComparisonsRetainTheirMeaning(
        string shape
    )
    {
        // Arrange
        using var context = new BinaryContext();
        var operations = Operations(context, shape);
        var value = operations.Model([], 10)!;

        // Act
        var results = new[]
        {
            operations.Compare(null, null),
            operations.Compare(value, value),
            operations.Compare(null, value),
            operations.Compare(value, null),
        };

        // Assert
        Assert.Equal([true, true, false, false], results);
    }

    /// <summary>Native and erased binary hashing exactly retain the framework's bounded array algorithm.</summary>
    /// <param name="length">A length below, at, or above the structural hash window.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(1024)]
    [InlineData(65_536)]
    public void HashesMatchFrameworkStructuralArrayHash(
        int length
    )
    {
        // Arrange
        using var context = new BinaryContext();
        var native = Operations(context, "native");
        var erased = Operations(context, "erased");
        var converted = Operations(context, "converted");
        var bytes = Enumerable.Range(0, length).Select(index => (byte)(index % 251)).ToArray();
        var model = converted.Model(bytes, 10)!;
        var expected = StructuralComparisons.StructuralEqualityComparer.GetHashCode(bytes);

        // Act
        var hashes = new[] { native.Hash(bytes), erased.Hash(bytes), converted.Hash(model) };

        // Assert
        Assert.All(hashes, hash => Assert.Equal(expected, hash));
    }

    /// <summary>Converters producing null keep the framework fallback instead of treating null as an empty array.</summary>
    [Fact]
    public void NullProviderOutputsRetainFrameworkEqualityAndHash()
    {
        // Arrange
        using var context = new BinaryContext();
        var property = context.Model.FindEntityType(typeof(BinaryNode))!
            .FindProperty(nameof(BinaryNode.Converted))!;

        var comparer = new NestedSetProviderComparer<BinaryIdentity>(property);
        var first = new BinaryIdentity(null, 10);
        var second = new BinaryIdentity(null, 20);
        var present = new BinaryIdentity([], 30);
        var framework = ValueComparer.CreateDefault<byte[]>(favorStructuralComparisons: true);

        // Act
        var equal = comparer.Equals(first, second);
        var nullEqualsEmpty = comparer.Equals(first, present);
        var hash = comparer.GetHashCode(first);

        // Assert
        Assert.True(equal);
        Assert.False(nullEqualsEmpty);
        Assert.Equal(framework.GetHashCode(null!), hash);
    }

    /// <summary>An object provider must not acquire binary structural semantics from its runtime input values.</summary>
    [Fact]
    public void ObjectProviderRetainsReferenceEqualityForBinaryInputs()
    {
        // Arrange
        var comparer = new NestedSetProviderComparer<object>(null);
        byte[] first = [1, 2, 3];
        byte[] second = [1, 2, 3];

        // Act
        var equal = comparer.Equals(first, second);
        var erasedEqual = comparer.Matches(first, second);
        var hash = comparer.GetHashCode(first);

        // Assert
        Assert.False(equal);
        Assert.False(erasedEqual);
        Assert.Equal(first.GetHashCode(), hash);
    }

    /// <summary>Unexpected runtime values still use the framework's type-error behavior.</summary>
    [Fact]
    public void BinaryProviderRejectsIncompatibleErasedValues()
    {
        // Arrange
        using var context = new BinaryContext();
        var property = context.Model.FindEntityType(typeof(BinaryNode))!
            .FindProperty(nameof(BinaryNode.Native))!;

        var comparer = new NestedSetProviderComparer<object>(property);

        // Act
        var error = Record.Exception(() => comparer.Matches(new object(), new object()));

        // Assert
        Assert.IsType<InvalidCastException>(error);
    }

    /// <summary>Repeated equal-content checks and bounded hashes do not allocate boxed bytes.</summary>
    /// <param name="shape">The warmed native, converted or erased comparer route.</param>
    [Theory]
    [InlineData("native")]
    [InlineData("erased")]
    [InlineData("converted")]
    [InlineData("converted-erased")]
    public void BinaryComparisonsAndHashesAvoidPerByteAllocation(
        string shape
    )
    {
        // Arrange
        using var context = new BinaryContext();
        var operations = Operations(context, shape);
        var first = operations.Model(Enumerable.Repeat((byte)7, 32).ToArray(), 10)!;
        var second = operations.Model(Enumerable.Repeat((byte)7, 32).ToArray(), 20)!;
        const int repetitions = 1000;
        var expectedHash = operations.Hash(first);

        for (var index = 0; index < repetitions; index++)
        {
            _ = operations.Compare(first, second);
            _ = operations.Hash(first);
        }

        var correct = true;
        var hashSum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < repetitions; index++)
        {
            correct &= operations.Compare(first, second);
            hashSum = unchecked(hashSum + operations.Hash(first));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.True(correct);
        Assert.Equal(unchecked(expectedHash * repetitions), hashSum);

        // WHY: A fixed allowance tolerates runtime setup but excludes even one boxed byte per operation.
        Assert.InRange(allocated, 0, 4096);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Builds delegates once so measurements include the real comparer but no new model wrappers.</summary>
    private static ComparerOperations Operations(
        BinaryContext context,
        string shape
    )
    {
        var metadata = context.Model.FindEntityType(typeof(BinaryNode))!;
        var converted = shape is "converted" or "converted-erased";
        var property = metadata.FindProperty(converted ? nameof(BinaryNode.Converted) : nameof(BinaryNode.Native))!;
        Func<byte[]?, int, object?> model = converted
            ? static (bytes, tag) => bytes is null ? null : new BinaryIdentity(bytes, tag)
            : static (bytes, _) => bytes;

        if (shape is "erased" or "converted-erased")
        {
            var comparer = new NestedSetProviderComparer<object>(property);

            return new ComparerOperations(comparer.Matches, comparer.GetHashCode, model);
        }

        if (converted)
        {
            var comparer = new NestedSetProviderComparer<BinaryIdentity>(property);

            return new ComparerOperations(
                (first, second) => comparer.Equals((BinaryIdentity?)first, (BinaryIdentity?)second),
                value => comparer.GetHashCode((BinaryIdentity)value),
                model);
        }

        var native = new NestedSetProviderComparer<byte[]>(property);

        return new ComparerOperations(
            (first, second) => native.Equals((byte[]?)first, (byte[]?)second),
            value => native.GetHashCode((byte[])value),
            model);
    }

    /// <summary>Retains prepared comparer and conversion delegates outside the measured loop.</summary>
    /// <param name="Compare">The actual equality entrypoint.</param>
    /// <param name="Hash">The actual provider hash entrypoint.</param>
    /// <param name="Model">The setup-only model-value factory.</param>
    private sealed record ComparerOperations(
        Func<object?, object?, bool> Compare,
        Func<object, int> Hash,
        Func<byte[]?, int, object?> Model);

    /// <summary>Provides native and converted binary property metadata without opening SQLite.</summary>
    private sealed class BinaryContext : DbContext
    {
        /// <summary>Creates a model-only comparer context.</summary>
        internal BinaryContext() : base(new DbContextOptionsBuilder<BinaryContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<BinaryNode>();
            node.Property(value => value.Id).ValueGeneratedNever();

            // WHY: Different tags retain different model identities but do not affect the provider bytes.
            node.Property(value => value.Converted)
                .HasConversion(value => value.Bytes, bytes => new BinaryIdentity(bytes, 0));
        }
    }

    /// <summary>Exposes independent native and converter-backed binary properties.</summary>
    private sealed class BinaryNode
    {
        /// <summary>Gets or sets the scalar model key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the native binary provider value.</summary>
        public byte[] Native { get; set; } = [];

        /// <summary>Gets or sets the reference identity normalized by its converter.</summary>
        public BinaryIdentity Converted { get; set; } = new([], 0);
    }

    /// <summary>Separates model equality from the binary provider representation.</summary>
    /// <param name="Bytes">The provider value, including the null-output fallback control.</param>
    /// <param name="Tag">Model-only identity data omitted by the configured converter.</param>
    private sealed record BinaryIdentity(byte[]? Bytes, int Tag);
}
