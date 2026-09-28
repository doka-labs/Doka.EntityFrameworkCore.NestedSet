namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Validation reports corrupt interval or depth values.</summary>
    /// <param name="corruption">The malformed derived structural value.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("Bounds")]
    [InlineData("NegativeDepth")]
    [InlineData("ExcessiveDepth")]
    public async Task ValidateReportsCorruptStructure(
        string corruption
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedRebuildTreeAsync(context, 1);
        await CorruptStructureAsync(context, 1, corruption);

        // Act
        var report = await tree
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.False(report.IsValid);
        Assert.NotEmpty(report.Issues);
        if (corruption != "Bounds")
        {
            Assert.Contains(
                report.Issues,
                issue => issue.Message.Contains("depth", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Rebuild derives intervals and depth from the ordered adjacency data.</summary>
    /// <param name="corruption">The malformed derived structural value.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("Bounds")]
    [InlineData("NegativeDepth")]
    [InlineData("ExcessiveDepth")]
    public async Task RebuildRestoresBoundsAndDepth(
        string corruption
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedRebuildTreeAsync(context, 1);
        var before = await SnapshotAsync(context, 1);
        await CorruptStructureAsync(context, 1, corruption);

        // Act
        await tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(before, await SnapshotAsync(context, 1));
        await AssertValidAsync(context, 1);
    }

    /// <summary>Rebuild rejects cycles, missing parents and duplicate sibling positions atomically.</summary>
    /// <param name="corruption">The invalid adjacency relation to reject.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("Cycle")]
    [InlineData("Orphan")]
    [InlineData("DuplicateChildPosition")]
    [InlineData("MultipleRoots")]
    public async Task RebuildRejectsInvalidAdjacency(
        string corruption
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedRebuildTreeAsync(context, 1);
        await CorruptAdjacencyAsync(context, 1, Engine, corruption);
        var before = await SnapshotAsync(context, 1);

        // Act
        var error = await Record.ExceptionAsync(() => tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1));
    }
}
