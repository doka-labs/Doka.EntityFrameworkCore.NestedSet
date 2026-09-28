namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies cheap facade preflight and the later detecting mutation boundary remain distinct.</summary>
public sealed class MutationContextGuardTests
{
    /// <summary>Registered writes fail without a detection pass and preserve the caller's detection setting.</summary>
    /// <param name="state">The registered application write state.</param>
    /// <param name="automaticDetection">The caller-owned automatic-detection setting.</param>
    [Theory]
    [InlineData(EntityState.Added, true)]
    [InlineData(EntityState.Modified, true)]
    [InlineData(EntityState.Deleted, true)]
    [InlineData(EntityState.Added, false)]
    [InlineData(EntityState.Modified, false)]
    [InlineData(EntityState.Deleted, false)]
    public void CheapGuardRejectsRegisteredWritesWithoutDetecting(
        EntityState state,
        bool automaticDetection
    )
    {
        // Arrange
        using var context = CreateContext();
        var entry = context.Attach(
            new UnrelatedRow
            {
                Id = 1,
                Value = "unchanged",
            });

        entry.State = state;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var detections = 0;
        context.ChangeTracker.DetectingAllChanges += (_, _) => detections++;

        // Act
        var error = Record.Exception(() =>
            NestedSetMutationExecutor<TreeNode, int, Guid, int>.RequireNoPendingWrites(context));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(0, detections);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(state, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Cheap preflight leaves ordinary CLR edits for the detecting boundary after identity lookup.</summary>
    [Fact]
    public void CheapGuardLeavesClrEditsUndetected()
    {
        // Arrange
        using var context = CreateContext();
        var row = new UnrelatedRow
        {
            Id = 1,
            Value = "original",
        };

        var entry = context.Attach(row);
        row.Value = "edited";
        var detections = 0;
        context.ChangeTracker.DetectingAllChanges += (_, _) => detections++;

        // Act
        var error = Record.Exception(() =>
            NestedSetMutationExecutor<TreeNode, int, Guid, int>.RequireNoPendingWrites(context));

        // Assert
        Assert.Null(error);
        Assert.Equal(0, detections);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal("original", entry.Property(value => value.Value).OriginalValue);
        Assert.Equal("edited", row.Value);
        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>The mutation boundary detects ordinary CLR edits exactly once and rejects pending writes.</summary>
    [Fact]
    public void FullGuardDetectsClrEditsExactlyOnce()
    {
        // Arrange
        using var context = CreateContext();
        var row = new UnrelatedRow
        {
            Id = 1,
            Value = "original",
        };

        var entry = context.Attach(row);
        row.Value = "edited";
        var detections = 0;
        context.ChangeTracker.DetectingAllChanges += (_, _) => detections++;

        // Act
        var error = Record.Exception(() =>
            NestedSetMutationExecutor<TreeNode, int, Guid, int>.RequireMutationContext(context));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(1, detections);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.True(entry.Property(value => value.Value).IsModified);
        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>The detecting boundary respects applications that explicitly disable automatic detection.</summary>
    [Fact]
    public void FullGuardPreservesDisabledAutomaticDetection()
    {
        // Arrange
        using var context = CreateContext();
        var row = new UnrelatedRow
        {
            Id = 1,
            Value = "original",
        };

        var entry = context.Attach(row);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        row.Value = "edited";
        var detections = 0;
        context.ChangeTracker.DetectingAllChanges += (_, _) => detections++;

        // Act
        var error = Record.Exception(() =>
            NestedSetMutationExecutor<TreeNode, int, Guid, int>.RequireMutationContext(context));

        // Assert
        Assert.Null(error);
        Assert.Equal(0, detections);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Creates a configured model without opening a database connection.</summary>
    private static TreeContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        return new TreeContext(options);
    }
}
