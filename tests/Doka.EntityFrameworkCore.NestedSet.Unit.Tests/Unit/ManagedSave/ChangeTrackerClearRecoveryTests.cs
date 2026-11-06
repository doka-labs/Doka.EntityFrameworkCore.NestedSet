namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks recovery after a callback cleared the whole change tracker.</summary>
public sealed class ChangeTrackerClearRecoveryTests
{
    /// <summary>Tracks every captured entry again, although Clear left them reporting their old state.</summary>
    [Fact]
    public async Task ClearedTrackerRestoresPendingDependentAndItsMembership()
    {
        // Arrange
        await using var context = new ClearContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var parent = new Item
        {
            Id = 1,
            Name = "Parent",
        };

        var child = new Item
        {
            Id = 2,
            Name = "Stored",
            Parent = parent,
        };

        context.AddRange(parent, child);
        await context.SaveChangesAsync(CancellationToken.None);
        child.Name = "Pending";
        context.ChangeTracker.DetectChanges();
        var entry = context.Entry(child);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        context.ChangeTracker.Clear();

        // Act
        snapshot.Restore();
        var restoredState = entry.State;
        var restoredName = child.Name;
        var restoredOriginal = entry.Property(value => value.Name)
            .OriginalValue;

        var restoredMembership = context
            .ChangeTracker
            .Entries<Item>()
            .Count();

        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(EntityState.Modified, restoredState);
        Assert.Equal("Pending", restoredName);
        Assert.Equal("Stored", restoredOriginal);
        Assert.Equal(2, restoredMembership);
        Assert.Equal(1, saved);
        Assert.Equal(EntityState.Unchanged, entry.State);
    }

    /// <summary>Maps a self-referencing foreign key, which EF indexes in a lazy dependent map.</summary>
    private sealed class ClearContext : DbContext
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
            var item = modelBuilder.Entity<Item>();
            item
                .Property(value => value.Id)
                .ValueGeneratedNever();

            item
                .HasOne(value => value.Parent)
                .WithMany()
                .HasForeignKey(value => value.ParentId);
        }
    }

    /// <summary>Provides a tracked dependent.</summary>
    private sealed class Item
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional principal key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the principal.</summary>
        public Item? Parent { get; set; }

        /// <summary>Gets or sets application payload.</summary>
        public string Name { get; set; } = string.Empty;
    }
}
