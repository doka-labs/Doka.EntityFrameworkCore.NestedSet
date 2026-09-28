namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects required node keys whose CLR type retains nullable value storage.</summary>
public abstract class RequiredNullableKeyModelTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the existing provider database owner for the distinct nullable-key model.</summary>
    /// <param name="fixture">The fixture that creates this model's isolated tables.</param>
    protected RequiredNullableKeyModelTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A required nullable CLR key can create a root without constructing a second Nullable wrapper.</summary>
    [Fact]
    public async Task RequiredNullableClrKeySupportsRootInsertion()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<NullableKeyContext>(
            Engine,
            options => new NullableKeyContext(options));

        var hierarchy = context.NestedSet<NullableKeyNode>();
        var treeId = Guid.NewGuid();
        var root = new NullableKeyNode { Id = 1 };

        // Act
        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);

        // Assert
        var property = context.Model.FindEntityType(typeof(NullableKeyNode))!.FindProperty(nameof(root.Id))!;
        Assert.False(property.IsNullable);
        Assert.Equal(typeof(int?), property.ClrType);
        var stored = await hierarchy
            .InTree(treeId)
            .Nodes
            .SingleAsync(CancellationToken.None);
        Assert.Equal(1, stored.Id);
        Assert.Null(stored.ParentId);
        Assert.Equal((1L, 2L, 0, 0L), (stored.Left, stored.Right, stored.Depth, stored.Position));
    }

    /// <summary>Parent joins and repair retain the single nullable CLR representation of required keys.</summary>
    [Fact]
    public async Task RequiredNullableClrKeySupportsParentJoinDuringRebuild()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<NullableKeyContext>(
            Engine,
            options => new NullableKeyContext(options));

        var hierarchy = context.NestedSet<NullableKeyNode>();
        var treeId = Guid.NewGuid();

        // WHY: The public import carries assigned nullable CLR keys on entities and binds only the Guid TreeId.
        // This exercises the accepted runtime model without changing scalar key argument annotations.
        await hierarchy.InsertForestAsync(
            [
                new NestedSetTreeImport<NullableKeyNode, Guid>(
                    treeId,
                    new NestedSetBranch<NullableKeyNode>(
                        new NullableKeyNode { Id = 2 },
                        [new NestedSetBranch<NullableKeyNode>(new NullableKeyNode { Id = 3 })])),
            ],
            CancellationToken.None);

        // Act
        await hierarchy
            .InTree(treeId)
            .RebuildAsync(CancellationToken.None);

        // Assert
        var stored = await hierarchy
            .InTree(treeId)
            .Nodes
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(2, stored.Length);
        Assert.Null(stored[0].ParentId);
        Assert.Equal(2, stored[1].ParentId);
        Assert.Equal((1L, 4L, 0, 0L), (stored[0].Left, stored[0].Right, stored[0].Depth, stored[0].Position));
        Assert.Equal((2L, 3L, 1, 0L), (stored[1].Left, stored[1].Right, stored[1].Depth, stored[1].Position));
    }

    /// <summary>Models a database-required key with nullable CLR storage and a compatible nullable parent.</summary>
    private sealed class NullableKeyNode
    {
        /// <summary>Gets or sets the required nullable CLR node key.</summary>
        public int? Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the nullable direct-parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the zero-based depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the dense sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Explicitly marks the nullable CLR primary key as required rather than rejecting the model.</summary>
    private sealed class NullableKeyContext(DbContextOptions<NullableKeyContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<NullableKeyNode>();
            node.ToTable("RequiredNullableKeyNodes");
            node
                .Property(value => value.Id)
                .IsRequired()
                .ValueGeneratedNever();
            node
                .HasOne<NullableKeyNode>()
                .WithMany()
                .HasForeignKey(value => value.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            node.HasNestedSet(value => value
                .HasNodeKey(entity => entity.Id)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position));
        }
    }
}
