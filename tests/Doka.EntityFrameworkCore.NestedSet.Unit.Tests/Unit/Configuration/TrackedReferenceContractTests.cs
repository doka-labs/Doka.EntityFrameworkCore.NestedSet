using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks the existing framework boundary's exact reference lookup independently of database I/O.</summary>
public sealed class TrackedReferenceContractTests
{
    /// <summary>An equal-key object remains untracked and leaves unrelated payload untouched.</summary>
    /// <param name="automaticDetection">The caller's detection setting, which the lookup must never change.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameKeyInstanceDoesNotCreateAnEntryOrDetectPayload(
        bool automaticDetection
    )
    {
        // Arrange
        await using var context = CreateContext();
        var contract = NestedSetInsertionContract.Instance;
        var tracked = new ReferenceEntity
        {
            Id = 1,
            Payload = "original",
        };

        var update = contract.Entry(context.Attach(tracked));
        var manager = contract.Manager(update);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var candidate = new ReferenceEntity { Id = tracked.Id };
        tracked.Payload = "undetected";
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        var found = contract.FindTrackedEntry(manager, candidate, update.EntityType);

        // Assert
        Assert.Null(found);
        Assert.Equal(EntityState.Unchanged, update.EntityState);
        Assert.False(update.IsModified(update.EntityType.FindProperty(nameof(ReferenceEntity.Payload))!));
        Assert.Single(updates.Entries);
        Assert.Equal(0, detections);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Identity is based on the exact reference and retains EF's real lifecycle state.</summary>
    /// <param name="state">The tracked or explicitly detached state observed by the reference map.</param>
    [Theory]
    [InlineData(EntityState.Unchanged)]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    [InlineData(EntityState.Detached)]
    public async Task ExactReferenceRetainsItsNativeState(
        EntityState state
    )
    {
        // Arrange
        await using var context = CreateContext();
        var contract = NestedSetInsertionContract.Instance;
        var entity = new ReferenceEntity { Id = 1 };
        var entry = context.Attach(entity);
        var update = contract.Entry(entry);
        var manager = contract.Manager(update);
        entry.State = state;

        // Act
        var found = contract.FindTrackedEntry(manager, entity, update.EntityType);

        // Assert
        if (state == EntityState.Detached)
        {
            Assert.Null(found);
        }
        else
        {
            var exact = Assert.IsType<IUpdateEntry>(found, exactMatch: false);
            Assert.Same(update, exact);
            Assert.Equal(state, exact.EntityState);
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Named shared mappings remain separate even when their CLR types and keys are identical.</summary>
    [Fact]
    public async Task NamedSharedReferencesUseExactNavigationMetadata()
    {
        // Arrange
        await using var context = CreateContext();
        var contract = NestedSetInsertionContract.Instance;
        var first = new Dictionary<string, object> { ["Id"] = 1 };
        var second = new Dictionary<string, object> { ["Id"] = 1 };
        var firstUpdate = contract.Entry(
            context
                .Set<Dictionary<string, object>>("First")
                .Attach(first));

        var secondUpdate = contract.Entry(
            context
                .Set<Dictionary<string, object>>("Second")
                .Attach(second));

        var manager = contract.Manager(firstUpdate);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        // Act
        var firstFound = contract.FindTrackedEntry(manager, first, firstUpdate.EntityType);
        var secondFound = contract.FindTrackedEntry(manager, second, secondUpdate.EntityType);
        var wrongMapping = contract.FindTrackedEntry(manager, first, secondUpdate.EntityType);

        // Assert
        Assert.Same(firstUpdate, firstFound);
        Assert.Same(secondUpdate, secondFound);
        Assert.Null(wrongMapping);
        Assert.Equal(2, updates.Entries.Count());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Declared base navigation metadata resolves a tracked concrete subtype without remapping it.</summary>
    [Fact]
    public async Task BaseMetadataResolvesConcreteReference()
    {
        // Arrange
        await using var context = CreateContext();
        var contract = NestedSetInsertionContract.Instance;
        var entity = new DerivedReferenceEntity { Id = 1 };
        var update = contract.Entry(context.Attach(entity));
        var metadata = context.Model.FindEntityType(typeof(ReferenceEntity))!;

        // Act
        var found = contract.FindTrackedEntry(contract.Manager(update), entity, metadata);

        // Assert
        var exact = Assert.IsType<IUpdateEntry>(found, exactMatch: false);
        Assert.Same(update, exact);
        Assert.Equal(typeof(DerivedReferenceEntity), exact.EntityType.ClrType);
        Assert.Equal(EntityState.Unchanged, exact.EntityState);
    }

    /// <summary>Constructs a tracking-only SQLite context with no automatic detection or connection opening.</summary>
    private static ReferenceContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ReferenceContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var context = new ReferenceContext(options);
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        return context;
    }

    /// <summary>Maps ordinary, inherited, and named shared instances for exact reference lookup.</summary>
    private sealed class ReferenceContext(
        DbContextOptions<ReferenceContext> options
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<ReferenceEntity>()
                .Property(entity => entity.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<DerivedReferenceEntity>();

            // WHY: Identical property-bag shapes isolate exact EF entity-name ownership in the reference lookup.
            foreach (var name in new[] { "First", "Second" })
            {
                modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                    name,
                    builder =>
                    {
                        builder
                            .Property<int>("Id")
                            .ValueGeneratedNever();

                        builder.HasKey("Id");
                    });
            }
        }
    }

    /// <summary>Provides an application-owned key and ordinary payload without hierarchy configuration.</summary>
    private class ReferenceEntity
    {
        /// <summary>Gets or sets the fixed application key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets payload that a lookup must not mark as modified.</summary>
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>Exercises base-navigation assignability without changing exact entry identity.</summary>
    private sealed class DerivedReferenceEntity : ReferenceEntity;
}
