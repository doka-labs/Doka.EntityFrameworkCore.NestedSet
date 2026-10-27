using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Proves identical independent seeds have the same complete logical row identity.</summary>
    [Fact]
    public async Task CanonicalLogicalRowsAgreeAcrossIndependentSeeds()
    {
        // Arrange
        await using var firstDatabase = new ModelCompatibilityDatabase();
        await using var first = await ConsumerProofContextAsync(firstDatabase, s_consumerScope);
        var expected = await ConsumerPhysicalTablesAsync(first);
        await using var secondDatabase = new ModelCompatibilityDatabase();
        await using var second = await ConsumerProofContextAsync(secondDatabase, s_consumerScope);

        // Act
        var actual = await ConsumerPhysicalTablesAsync(second);

        // Assert
        _output.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    Expected = expected,
                    Actual = actual,
                },
                s_consumerEvidenceJson));

        Assert.Equal(2, actual.Length);

        for (var index = 0; index < actual.Length; index++)
        {
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal(expected[index].Definition, actual[index].Definition);
            Assert.Equal(index == 0 ? 8 : 12, actual[index].LogicalRowCount);
            Assert.Equal(expected[index].LogicalRowCount, actual[index].LogicalRowCount);
            Assert.Equal(expected[index].LogicalRowsSha256, actual[index].LogicalRowsSha256);
        }
    }

    /// <summary>Distinguishes scopes even when row counts, schema, node keys and bounds are equal.</summary>
    [Fact]
    public async Task CanonicalLogicalRowsDistinguishIndependentScopes()
    {
        // Arrange
        await using var firstDatabase = new ModelCompatibilityDatabase();
        await using var first = await ConsumerProofContextAsync(firstDatabase, s_consumerScope);
        var expected = await ConsumerPhysicalTablesAsync(first);
        await using var secondDatabase = new ModelCompatibilityDatabase();
        var otherScope = new Guid("63636363-6363-6363-6363-636363636363");
        await using var second = await ConsumerProofContextAsync(secondDatabase, otherScope);

        // Act
        var actual = await ConsumerPhysicalTablesAsync(second);

        // Assert
        _output.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    Expected = expected,
                    Actual = actual,
                },
                s_consumerEvidenceJson));

        for (var index = 0; index < actual.Length; index++)
        {
            Assert.Equal(expected[index].Definition, actual[index].Definition);
            Assert.Equal(expected[index].LogicalRowCount, actual[index].LogicalRowCount);
            Assert.NotEqual(expected[index].LogicalRowsSha256, actual[index].LogicalRowsSha256);
        }
    }

    /// <summary>Rejects changed structural or fact payload without relying on counts or query filters.</summary>
    /// <param name="column">The fixed native column mutation covered by this rejection case.</param>
    [Theory]
    [InlineData("Id")]
    [InlineData("TreeId")]
    [InlineData("ParentId")]
    [InlineData("Bounds")]
    [InlineData("Depth")]
    [InlineData("Position")]
    [InlineData("Visible")]
    [InlineData("Caption")]
    [InlineData("FactNode")]
    [InlineData("FactLabel")]
    public async Task CanonicalLogicalRowsDetectChangedColumn(string column)
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await ConsumerProofContextAsync(database, s_consumerScope);
        var before = await ConsumerPhysicalTablesAsync(context);
        var mutation = ConsumerProofMutation(column);

        // Act
        await context.Database.ExecuteSqlRawAsync(mutation.Change, CancellationToken.None);
        var after = await ConsumerPhysicalTablesAsync(context);

        // Assert
        _output.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    Before = before,
                    After = after,
                },
                s_consumerEvidenceJson));

        Assert.Equal(before[mutation.Table].Definition, after[mutation.Table].Definition);
        Assert.Equal(before[mutation.Table].LogicalRowCount, after[mutation.Table].LogicalRowCount);
        Assert.NotEqual(before[mutation.Table].LogicalRowsSha256, after[mutation.Table].LogicalRowsSha256);
    }

    /// <summary>Restores the original proof after the same exact field values and key order are restored.</summary>
    /// <param name="column">The fixed native column mutation repaired by this case.</param>
    [Theory]
    [InlineData("Id")]
    [InlineData("TreeId")]
    [InlineData("ParentId")]
    [InlineData("Bounds")]
    [InlineData("Depth")]
    [InlineData("Position")]
    [InlineData("Visible")]
    [InlineData("Caption")]
    [InlineData("FactNode")]
    [InlineData("FactLabel")]
    public async Task CanonicalLogicalRowsReturnAfterColumnIsRestored(string column)
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await ConsumerProofContextAsync(database, s_consumerScope);
        var original = await ConsumerPhysicalTablesAsync(context);
        var mutation = ConsumerProofMutation(column);
        await context.Database.ExecuteSqlRawAsync(mutation.Change, CancellationToken.None);
        var changed = await ConsumerPhysicalTablesAsync(context);

        // Act
        await context.Database.ExecuteSqlRawAsync(mutation.Restore, CancellationToken.None);
        var restored = await ConsumerPhysicalTablesAsync(context);

        // Assert
        _output.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    Original = original,
                    Changed = changed,
                    Restored = restored,
                },
                s_consumerEvidenceJson));

        Assert.NotEqual(changed[mutation.Table].LogicalRowsSha256, restored[mutation.Table].LogicalRowsSha256);
        Assert.Equal(original[mutation.Table].Definition, restored[mutation.Table].Definition);
        Assert.Equal(original[mutation.Table].LogicalRowCount, restored[mutation.Table].LogicalRowCount);
        Assert.Equal(original[mutation.Table].LogicalRowsSha256, restored[mutation.Table].LogicalRowsSha256);
    }

    /// <summary>Owns a small foreign-key-valid seed until its caller receives the ready context.</summary>
    private async Task<AncestorIndexQueryContext> ConsumerProofContextAsync(
        ModelCompatibilityDatabase database,
        Guid scope
    )
    {
        var context = await database.CreateContextAsync<AncestorIndexQueryContext>(
            Engine,
            static options => new AncestorIndexQueryContext(options));

        try
        {
            await SeedAsync(context, scope, s_consumerTree, forest: false, nodes: 8);
            await SeedConsumerFactsAsync(context, scope, [2, 3], unrelatedNode: 8);
            await context.Database.OpenConnectionAsync(CancellationToken.None);

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Uses fixed foreign-key-valid statements rather than interpolating caller-supplied SQL.</summary>
    private static (int Table, string Change, string Restore) ConsumerProofMutation(string column)
        => column switch
        {
            "Id" => (0,
                "UPDATE `AncestorIndexQueryNodes` SET `Id`=9 WHERE `Id`=7",
                "UPDATE `AncestorIndexQueryNodes` SET `Id`=7 WHERE `Id`=9"),
            "TreeId" => (0,
                "UPDATE `AncestorIndexQueryNodes` SET `TreeId`=CAST(REPEAT('c',16) AS BINARY(16)) WHERE `Id`=7",
                "UPDATE `AncestorIndexQueryNodes` SET `TreeId`=CAST(REPEAT('b',16) AS BINARY(16)) WHERE `Id`=7"),
            "ParentId" => (0,
                "UPDATE `AncestorIndexQueryNodes` SET `ParentId`=2 WHERE `Id`=7",
                "UPDATE `AncestorIndexQueryNodes` SET `ParentId`=1 WHERE `Id`=7"),
            "Bounds" => (0,
                "UPDATE `AncestorIndexQueryNodes` SET `Right`=14 WHERE `Id`=7",
                "UPDATE `AncestorIndexQueryNodes` SET `Right`=13 WHERE `Id`=7"),
            "Depth" => (0,
                "UPDATE `AncestorIndexQueryNodes` SET `Depth`=2 WHERE `Id`=7",
                "UPDATE `AncestorIndexQueryNodes` SET `Depth`=1 WHERE `Id`=7"),
            "Position" => (0,
                "UPDATE `AncestorIndexQueryNodes` SET `Position`=6 WHERE `Id`=7",
                "UPDATE `AncestorIndexQueryNodes` SET `Position`=5 WHERE `Id`=7"),
            "Visible" => (0,
                "UPDATE `AncestorIndexQueryNodes` SET `Visible`=0 WHERE `Id`=7",
                "UPDATE `AncestorIndexQueryNodes` SET `Visible`=1 WHERE `Id`=7"),
            "Caption" => (0,
                "UPDATE `AncestorIndexQueryNodes` SET `Details_Caption`='changed|value' WHERE `Id`=7",
                "UPDATE `AncestorIndexQueryNodes` SET `Details_Caption`='fixture' WHERE `Id`=7"),
            "FactNode" => (1,
                "UPDATE `AncestorIndexQueryItems` SET `NodeId`=3 WHERE `Id`=1",
                "UPDATE `AncestorIndexQueryItems` SET `NodeId`=2 WHERE `Id`=1"),
            "FactLabel" => (1,
                "UPDATE `AncestorIndexQueryItems` SET `Label`='changed|value' WHERE `Id`=1",
                "UPDATE `AncestorIndexQueryItems` SET `Label`='selected' WHERE `Id`=1"),
            _ => throw new InvalidOperationException("The logical row proof requires a known mutation case."),
        };
}
