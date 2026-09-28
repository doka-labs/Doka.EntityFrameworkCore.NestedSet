namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects typed managed-save coordination for exact named shared CLR and property-bag mappings.</summary>
[Collection("Model compatibility")]
public abstract class SharedTypeManagedSaveTests : ProviderTest
{
    private static readonly int[] s_expectedTrackedPositions = [1, 0, 2];
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Reuses provider resources and isolated model databases from the compatibility fixture.</summary>
    /// <param name="fixture">The existing provider resource owner.</param>
    protected SharedTypeManagedSaveTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Renaming a named shared node refreshes three sorted siblings and preserves another mapping.</summary>
    /// <returns>A task that completes after checking geometry, tracking and an unrelated named write.</returns>
    [Fact]
    public async Task NamedSharedClrSaveRefreshesSortedSiblingsAndKeepsOtherMapping()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<SortedSharedContext>(
            Engine,
            static options => new SortedSharedContext(options));
        var treeId = Guid.NewGuid();
        var hierarchy = context.NestedSet<SharedSaveNode>(SortedSharedContext.FolderEntity);
        await hierarchy.InsertRootAsync(
            new SharedSaveNode
            {
                Id = 1,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new SharedSaveNode
            {
                Id = 2,
                Name = "B",
            },
            1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new SharedSaveNode
            {
                Id = 3,
                Name = "C",
            },
            1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new SharedSaveNode
            {
                Id = 4,
                Name = "D",
            },
            1,
            CancellationToken.None);

        var tracked = await context
            .Set<SharedSaveNode>(SortedSharedContext.FolderEntity)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var renamed = tracked.Single(node => node.Id == 3);
        renamed.Name = "A";
        var unrelated = new SharedSaveNode
        {
            Id = 3,
            Name = "Audit",
            TreeId = Guid.NewGuid(),
        };

        context
            .Set<SharedSaveNode>(SortedSharedContext.UnrelatedEntity)
            .Add(unrelated);

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, saved);
        var children = await hierarchy
            .ChildrenOf(1)
            .ToArrayAsync(CancellationToken.None);
        Assert.Collection(
            children,
            first => Assert.Equal(
                (3, "A", 2L, 3L, 0L),
                (first.Id, first.Name, first.Left, first.Right, first.Position)),
            second => Assert.Equal(
                (2, "B", 4L, 5L, 1L),
                (second.Id, second.Name, second.Left, second.Right, second.Position)),
            third => Assert.Equal(
                (4, "D", 6L, 7L, 2L),
                (third.Id, third.Name, third.Left, third.Right, third.Position)));
        Assert.Equal((1L, 8L, 0), (tracked[0].Left, tracked[0].Right, tracked[0].Depth));
        Assert.Equal(
            s_expectedTrackedPositions,
            tracked
                .Skip(1)
                .Select(node => (int)node.Position)
                .ToArray());

        var entries = context
            .ChangeTracker
            .Entries<SharedSaveNode>()
            .ToArray();

        Assert.All(entries, entry => Assert.Equal(EntityState.Unchanged, entry.State));
        var unrelatedEntry = Assert.Single(entries, entry => ReferenceEquals(entry.Entity, unrelated));
        Assert.Equal(SortedSharedContext.UnrelatedEntity, unrelatedEntry.Metadata.Name);
        Assert.Equal(
            "Audit",
            (await context
                .Set<SharedSaveNode>(SortedSharedContext.UnrelatedEntity)
                .AsNoTracking()
                .SingleAsync(CancellationToken.None)).Name);
        var renamedEntry = Assert.Single(entries, entry => ReferenceEquals(entry.Entity, renamed));
        Assert.Equal(SortedSharedContext.FolderEntity, renamedEntry.Metadata.Name);
        Assert.Equal("A", renamedEntry.Property(node => node.Name).OriginalValue);
        Assert.All(renamedEntry.Properties, property => Assert.False(property.IsModified));
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Moving a named property-bag child retains hierarchy metadata alongside another named bag.</summary>
    /// <returns>A task that completes after checking both trees and the unrelated property-bag write.</returns>
    [Fact]
    public async Task NamedPropertyBagParentSaveKeepsExactMetadataAndOtherMapping()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<PropertyBagSaveContext>(
            Engine,
            static options => new PropertyBagSaveContext(options));

        var sourceTree = Guid.NewGuid();
        var targetTree = Guid.NewGuid();
        var hierarchy = context.NestedSet<Dictionary<string, object>>(SharedTypeContext.FolderEntity);
        await hierarchy.InsertRootAsync(Bag(1, "Source"), sourceTree, CancellationToken.None);
        await hierarchy.InsertRootAsync(Bag(2, "Target"), targetTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(Bag(3, "Child"), 1, CancellationToken.None);
        var tracked = await context
            .Set<Dictionary<string, object>>(SharedTypeContext.FolderEntity)
            .OrderBy(node => EF.Property<int>(node, SharedTypeContext.Id))
            .ToArrayAsync(CancellationToken.None);

        var child = tracked.Single(node => (int)node[SharedTypeContext.Id] == 3);
        child[SharedTypeContext.ParentId] = 2;
        child[SharedTypeContext.Name] = "Moved";
        var unrelated = new Dictionary<string, object> { [SharedTypeContext.Id] = 3 };
        context
            .Set<Dictionary<string, object>>(SharedTypeContext.UnrelatedEntity)
            .Add(unrelated);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            (targetTree, 2, 2L, 3L, 1, 0L),
            ((Guid)child[SharedTypeContext.TreeId], (int)child[SharedTypeContext.ParentId],
                (long)child[SharedTypeContext.Left], (long)child[SharedTypeContext.Right],
                (int)child[SharedTypeContext.Depth], (long)child[SharedTypeContext.Position]));
        Assert.Equal("Moved", child[SharedTypeContext.Name]);
        Assert.Equal((1L, 2L), ((long)tracked[0][SharedTypeContext.Left], (long)tracked[0][SharedTypeContext.Right]));
        Assert.Equal((1L, 4L), ((long)tracked[1][SharedTypeContext.Left], (long)tracked[1][SharedTypeContext.Right]));
        var entries = context
            .ChangeTracker
            .Entries<Dictionary<string, object>>()
            .ToArray();
        Assert.All(entries, entry => Assert.Equal(EntityState.Unchanged, entry.State));
        var childEntry = Assert.Single(entries, entry => ReferenceEquals(entry.Entity, child));
        Assert.Equal(SharedTypeContext.FolderEntity, childEntry.Metadata.Name);
        Assert.Equal(2, childEntry.Property<int?>(SharedTypeContext.ParentId).OriginalValue);
        Assert.All(childEntry.Properties, property => Assert.False(property.IsModified));
        var unrelatedEntry = Assert.Single(entries, entry => ReferenceEquals(entry.Entity, unrelated));
        Assert.Equal(SharedTypeContext.UnrelatedEntity, unrelatedEntry.Metadata.Name);
        Assert.Equal(
            1,
            await context
                .Set<Dictionary<string, object>>(SharedTypeContext.UnrelatedEntity)
                .AsNoTracking()
                .CountAsync(CancellationToken.None));
        Assert.True(
            (await hierarchy
                .InTree(sourceTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await hierarchy
                .InTree(targetTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Creates a detached property-bag hierarchy node without assigning managed coordinates.</summary>
    private static Dictionary<string, object> Bag(
        int id,
        string name
    ) => new()
    {
        [SharedTypeContext.Id] = id,
        [SharedTypeContext.Name] = name,
    };

    /// <summary>Coordinates ordinary property-bag saves through the public named mapping contract.</summary>
    private sealed class PropertyBagSaveContext(DbContextOptions<PropertyBagSaveContext> options)
        : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            // WHY: The compatibility resource owner shares databases across model types without deleting rows.
            // Dedicated tables keep this one-save regression independent of existing shared-type insertion tests.
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                SharedTypeContext.FolderEntity,
                node =>
                {
                    node.ToTable("ManagedPropertyBagFolders");
                    node
                        .IndexerProperty<int>(SharedTypeContext.Id)
                        .ValueGeneratedNever();
                    node.IndexerProperty<Guid>(SharedTypeContext.TreeId);
                    node.IndexerProperty<int?>(SharedTypeContext.ParentId);
                    node.IndexerProperty<long>(SharedTypeContext.Left);
                    node.IndexerProperty<long>(SharedTypeContext.Right);
                    node.IndexerProperty<int>(SharedTypeContext.Depth);
                    node.IndexerProperty<long>(SharedTypeContext.Position);
                    node
                        .IndexerProperty<string>(SharedTypeContext.Name)
                        .HasMaxLength(80);
                    node.HasKey(SharedTypeContext.Id);
                    node.HasNestedSet(nestedSet => nestedSet
                        .HasNodeKey(SharedTypeContext.Id)
                        .HasTreeId(SharedTypeContext.TreeId)
                        .HasParent(SharedTypeContext.ParentId)
                        .HasBounds(SharedTypeContext.Left, SharedTypeContext.Right)
                        .HasDepth(SharedTypeContext.Depth)
                        .HasPosition(SharedTypeContext.Position));
                });

            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                SharedTypeContext.UnrelatedEntity,
                node =>
                {
                    node.ToTable("ManagedPropertyBagUnrelated");
                    node
                        .IndexerProperty<int>(SharedTypeContext.Id)
                        .ValueGeneratedNever();
                    node.HasKey(SharedTypeContext.Id);
                });
        }
    }

    /// <summary>Coordinates ordinary named shared CLR saves with public selector-based sorting.</summary>
    private sealed class SortedSharedContext(DbContextOptions<SortedSharedContext> options)
        : NestedSetDbContext(options)
    {
        /// <summary>Names the shared hierarchy independently of its CLR type.</summary>
        internal const string FolderEntity = "ManagedSharedFolder";

        /// <summary>Names a non-hierarchy entity backed by the same CLR type.</summary>
        internal const string UnrelatedEntity = "ManagedSharedUnrelated";

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<SharedSaveNode>(
                FolderEntity,
                node =>
                {
                    node.ToTable("ManagedSharedFolders");
                    node.HasKey(entity => entity.Id);
                    node
                        .Property(entity => entity.Id)
                        .ValueGeneratedNever();
                    node
                        .Property(entity => entity.Name)
                        .HasMaxLength(80);
                    node.HasNestedSet(nestedSet => nestedSet
                        .HasTreeId(entity => entity.TreeId)
                        .HasParent(entity => entity.ParentId)
                        .HasBounds(entity => entity.Left, entity => entity.Right)
                        .HasDepth(entity => entity.Depth)
                        .HasPosition(entity => entity.Position)
                        .OrderBy(entity => entity.Name));
                });

            modelBuilder.SharedTypeEntity<SharedSaveNode>(
                UnrelatedEntity,
                node =>
                {
                    node.ToTable("ManagedSharedUnrelated");
                    node.HasKey(entity => entity.Id);
                    node
                        .Property(entity => entity.Id)
                        .ValueGeneratedNever();
                });
        }
    }

    /// <summary>Provides direct CLR selectors while deliberately appearing under two different EF names.</summary>
    private sealed class SharedSaveNode
    {
        /// <summary>Gets or sets the assigned key within its named entity type.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the stable hierarchy tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the nullable direct parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the managed left coordinate.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the managed right coordinate.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the managed node depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the managed sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the public sibling-order criterion.</summary>
        public string Name { get; set; } = string.Empty;
    }
}
