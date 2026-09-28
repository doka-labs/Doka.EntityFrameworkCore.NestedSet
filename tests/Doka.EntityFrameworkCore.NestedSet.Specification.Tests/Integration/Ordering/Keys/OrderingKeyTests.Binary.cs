namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingKeyTests
{
    /// <summary>Places tied binary identities by database value while resolving fresh key arrays.</summary>
    [Fact]
    public async Task BinaryKeyInsertionUsesNativeTieOrderAndContentEquality()
    {
        // Arrange
        await using var scenario = await CreateScenarioAsync<byte[], byte[]?>(Engine, BinaryKey);
        var inserted = scenario.Node(6, "Same");

        // Act
        await scenario.Tree.InsertChildAsync(inserted, scenario.Reference(1), CancellationToken.None);

        // Assert
        await AssertChildrenAsync(scenario, 1, [2, 3, 4, 6]);
        await AssertFilteredTreeAsync(scenario, 1, "Same");
        await AssertIntegrityAsync(scenario);
    }

    /// <summary>Moves a binary-key subtree using fresh arrays and retains descendants and native tie order.</summary>
    [Fact]
    public async Task BinaryKeyMoveRetainsNativeOrderAndDescendants()
    {
        // Arrange
        await using var scenario = await CreateScenarioAsync<byte[], byte[]?>(Engine, BinaryKey);

        // Act
        await scenario.Tree.MoveToAsync(scenario.Reference(2), scenario.Reference(9), CancellationToken.None);

        // Assert
        await AssertChildrenAsync(scenario, 9, [2, 8]);
        await AssertChildrenAsync(scenario, 1, [3, 4]);
        await AssertFilteredTreeAsync(scenario, 9, "Zulu");
        await AssertIntegrityAsync(scenario);
    }

    /// <summary>Reorders a tracked binary-key subtree without relying on reference equality for key values.</summary>
    [Fact]
    public async Task BinaryKeyRenameRetainsNativeTieOrderAndScopeIsolation()
    {
        // Arrange
        await using var scenario = await CreateScenarioAsync<byte[], byte[]?>(Engine, BinaryKey);
        var key = scenario.Reference(2);
        var renamed = await scenario
            .Nodes
            .AsTracking()
            .SingleAsync(node => node.Id == key, CancellationToken.None);

        renamed.Name = "Same";

        // Act
        var saved = await scenario.Context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal(EntityState.Unchanged, scenario.Context.Entry(renamed).State);
        await AssertChildrenAsync(scenario, 1, [2, 3, 4]);
        await AssertFilteredTreeAsync(scenario, 1, "Same");
        await AssertIntegrityAsync(scenario);
    }
}
