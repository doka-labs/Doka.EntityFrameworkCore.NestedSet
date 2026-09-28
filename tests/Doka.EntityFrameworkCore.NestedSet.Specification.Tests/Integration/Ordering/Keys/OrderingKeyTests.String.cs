namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingKeyTests
{
    /// <summary>Places tied string identities by their database collation while accepting parent-key aliases.</summary>
    [Fact]
    public async Task StringKeyInsertionUsesNativeOrderAndCollationAliases()
    {
        // Arrange
        await using var scenario = await CreateScenarioAsync<string, string?>(
            Engine,
            StringKey,
            static key => key.ToUpperInvariant());

        var inserted = scenario.Node(6, "Same");

        // Act
        await scenario.Tree.InsertChildAsync(inserted, scenario.Reference(1), CancellationToken.None);

        // Assert
        await AssertChildrenAsync(scenario, 1, [2, 3, 4, 6]);
        await AssertFilteredTreeAsync(scenario, 1, "Same");
        await AssertIntegrityAsync(scenario);
    }

    /// <summary>Moves a subtree using aliased string keys while preserving native tie order.</summary>
    [Fact]
    public async Task StringKeyMoveUsesNativeOrderAndCollationAliases()
    {
        // Arrange
        await using var scenario = await CreateScenarioAsync<string, string?>(
            Engine,
            StringKey,
            static key => key.ToUpperInvariant());

        // Act
        await scenario.Tree.MoveToAsync(scenario.Reference(2), scenario.Reference(9), CancellationToken.None);

        // Assert
        await AssertChildrenAsync(scenario, 9, [2, 8]);
        await AssertChildrenAsync(scenario, 1, [3, 4]);
        await AssertFilteredTreeAsync(scenario, 9, "Zulu");
        await AssertIntegrityAsync(scenario);
    }

    /// <summary>Reorders a renamed string-key subtree through an ordinary tracked save.</summary>
    [Fact]
    public async Task StringKeyRenameRetainsNativeTieOrderAndScopeIsolation()
    {
        // Arrange
        await using var scenario = await CreateScenarioAsync<string, string?>(
            Engine,
            StringKey,
            static key => key.ToUpperInvariant());

        var alias = scenario.Reference(2);
        var renamed = await scenario
            .Nodes
            .AsTracking()
            .SingleAsync(node => node.Id == alias, CancellationToken.None);

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
