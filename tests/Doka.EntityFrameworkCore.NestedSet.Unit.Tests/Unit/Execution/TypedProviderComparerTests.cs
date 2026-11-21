namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves native, nullable and converted provider identity comparison semantics.</summary>
public sealed class TypedProviderComparerTests
{

    /// <summary>Retains native equality, inequality and consistent hashing.</summary>
    /// <param name="role">The native scalar or present nullable-parent representation.</param>
    [Theory]
    [InlineData("Int")]
    [InlineData("Guid")]
    [InlineData("NullableParent")]
    public void Compare_NativeScalars_RetainEqualityAndHashSemantics(
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

        // Act
        var correct = compare();

        // Assert
        Assert.True(correct);
        Assert.Equal(integer.GetHashCode(0), parent.GetHashCode(0));
    }

    /// <summary>Preserves both typed and erased equality at the configured converter boundary.</summary>
    [Fact]
    public void Matches_ConvertedComparisons_RetainProviderEquality()
    {
        // Arrange
        using var context = new ComparerContext();
        var metadata = context.Model.FindEntityType(typeof(ComparerNode))!;
        var comparer = new NestedSetProviderComparer<ConvertedIdentity>(
            metadata.FindProperty(nameof(ComparerNode.ConvertedId))!);

        var first = new ConvertedIdentity(17);
        var equalProvider = new ConvertedIdentity(27);
        var differentProvider = new ConvertedIdentity(18);

        // Act
        var baselineCorrect = comparer.Matches(first, equalProvider) && !comparer.Matches(first, differentProvider);
        var typedCorrect = comparer.Equals(first, equalProvider) && !comparer.Equals(first, differentProvider);

        // Assert
        Assert.True(baselineCorrect);
        Assert.True(typedCorrect);
        Assert.NotEqual(first, equalProvider);
        Assert.Equal(comparer.GetHashCode(first), comparer.GetHashCode(equalProvider));


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
            // the actual configured converter would fail the independent equality controls.
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
    private readonly record struct ConvertedIdentity(
        int Value
    );
}
