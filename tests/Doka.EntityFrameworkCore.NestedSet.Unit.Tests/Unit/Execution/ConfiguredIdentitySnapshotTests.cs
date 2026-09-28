namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Verifies configured copies and independent array storage for every mutable structural identity role.
/// </summary>
public sealed class ConfiguredIdentitySnapshotTests
{
    /// <summary>Uses the model's deep snapshot when an identity is converted from a mutable application type.</summary>
    /// <param name="propertyName">The node-key, Scope, or TreeId property whose configured copy is required.</param>
    [Theory]
    [InlineData(nameof(MutableIdentityNode.Id))]
    [InlineData(nameof(MutableIdentityNode.Scope))]
    [InlineData(nameof(MutableIdentityNode.TreeId))]
    public async Task ConvertedIdentityUsesConfiguredSnapshot(
        string propertyName
    )
    {
        // Arrange
        await using var context = new IdentityContext();
        var property = context.Model.FindEntityType(typeof(MutableIdentityNode))!.FindProperty(propertyName)!;
        var input = new MutableIdentity("original");

        // Act
        var snapshot = NestedSetStructuralValue.Snapshot(property, input);
        input.Value = "changed";

        // Assert
        var identity = Assert.IsType<MutableIdentity>(snapshot);
        Assert.NotSame(input, identity);
        Assert.Equal("original", identity.Value);
    }

    /// <summary>Retains independent array storage even when the application configures a shallow snapshot.</summary>
    [Fact]
    public async Task ShallowArrayComparerCannotShareStructuralSnapshotStorage()
    {
        // Arrange
        await using var context = new IdentityContext();
        var property =
            context.Model.FindEntityType(typeof(BinaryIdentityNode))!.FindProperty(nameof(BinaryIdentityNode.TreeId))!;

        byte[] input = [1, 2, 3, 4];
        var configuredSnapshot = property
            .GetValueComparer()
            .Snapshot(input);

        // Act
        var snapshot = NestedSetStructuralValue.Snapshot(property, input);
        input[0] = 9;

        // Assert
        Assert.Same(input, configuredSnapshot);
        var bytes = Assert.IsType<byte[]>(snapshot);
        Assert.NotSame(input, bytes);
        Assert.Equal([1, 2, 3, 4], bytes);
    }

    /// <summary>Configured snapshot semantics do not replace the default CLR sentinel comparison.</summary>
    [Fact]
    public async Task ConfiguredComparerDoesNotChangeStructuralSentinelMatching()
    {
        // Arrange
        await using var context = new IdentityContext();
        var property =
            context.Model.FindEntityType(typeof(MutableIdentityNode))!.FindProperty(
                nameof(MutableIdentityNode.TreeId))!;

        var first = new MutableIdentity("same");
        var second = new MutableIdentity("same");
        var configuredMatch = property
            .GetValueComparer()
            .Equals(first, second);

        // Act
        var matches = NestedSetStructuralValue.Matches(property, first, second);

        // Assert
        Assert.True(configuredMatch);
        Assert.False(matches);
    }

    /// <summary>Finalizes the relational metadata without opening a database connection.</summary>
    private sealed class IdentityContext : DbContext
    {
        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var mutable = modelBuilder.Entity<MutableIdentityNode>();
            mutable.HasKey(node => node.Id);

            foreach (var propertyName in new[]
                     {
                         nameof(MutableIdentityNode.Id),
                         nameof(MutableIdentityNode.Scope),
                         nameof(MutableIdentityNode.TreeId),
                     })
            {
                var property = mutable.Property<MutableIdentity>(propertyName);
                property.HasConversion(value => value.Value, value => new MutableIdentity(value));
                property.Metadata.SetValueComparer(
                    new ValueComparer<MutableIdentity>(
                        (
                            left,
                            right
                        ) => left != null && right != null && left.Value == right.Value,
                        value => StringComparer.Ordinal.GetHashCode(value.Value),
                        value => new MutableIdentity(value.Value)));
            }

            mutable
                .Property(node => node.Id)
                .ValueGeneratedNever();

            var binary = modelBuilder.Entity<BinaryIdentityNode>();
            binary
                .Property(node => node.TreeId)
                .Metadata
                .SetValueComparer(
                    new ValueComparer<byte[]>(
                        (left, right) => left != null
                            && right != null
                            && left
                                .AsEnumerable()
                                .SequenceEqual(right),
                        value => value.Aggregate(0, HashCode.Combine),
                        value => value));
        }
    }

    /// <summary>Represents an application-owned identity requiring its configured copy strategy.</summary>
    private sealed class MutableIdentity(string value)
    {
        /// <summary>Gets or sets the converted identity representation.</summary>
        public string Value { get; set; } = value;
    }

    /// <summary>Maps the three identity roles through the same explicitly configured mutable representation.</summary>
    private sealed class MutableIdentityNode
    {
        /// <summary>Gets or sets the node key.</summary>
        public MutableIdentity Id { get; set; } = new("node");

        /// <summary>Gets or sets the optional partition identity.</summary>
        public MutableIdentity Scope { get; set; } = new("scope");

        /// <summary>Gets or sets the stable tree identity.</summary>
        public MutableIdentity TreeId { get; set; } = new("tree");
    }

    /// <summary>Maps a binary identity whose configured snapshot intentionally returns its argument.</summary>
    private sealed class BinaryIdentityNode
    {
        /// <summary>Gets or sets the ordinary integer key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the binary tree identity.</summary>
        public byte[] TreeId { get; set; } = [];
    }
}
