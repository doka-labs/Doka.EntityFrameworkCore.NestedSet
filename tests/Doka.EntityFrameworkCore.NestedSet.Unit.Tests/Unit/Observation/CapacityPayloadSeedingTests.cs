namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks complete capacity payloads across bounded native insert windows.</summary>
public sealed class CapacityPayloadSeedingTests
{
    /// <summary>Whole and selective populations retain exact keys across the second insertion window.</summary>
    /// <param name="facts">The number of trailing source keys receiving payload.</param>
    /// <param name="expectedBatches">The number of bounded native insertion commands.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1)]
    [InlineData(100_000, 1)]
    [InlineData(100_001, 2)]
    public async Task BoundedPayloadRetainsExactSourceKeys(
        int facts,
        int expectedBatches
    )
    {
        // Arrange
        const int nodes = 100_001;
        await using var database = await TestDatabase.CreateAsync("Sqlite");

        await using (var setup = database.CreateContext())
        {
            await CapacitySeed.SeedAsync(setup, nodes);
            setup.Set<UnrelatedRow>().Add(new UnrelatedRow { Id = -1, Value = "previous fixture" });
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        var commands = new EnterpriseProbe();
        await using var context = database.CreateContext(commands);

        // Act
        await CompositionCapacityProfiles.SeedPayloadAsync(context, nodes, facts);
        var totals = await context.Set<UnrelatedRow>()
            .GroupBy(row => 1)
            .Select(group => new
            {
                Count = group.LongCount(),
                First = group.Min(row => row.Id),
                Last = group.Max(row => row.Id),
                Sum = group.Sum(row => (long)row.Id),
            })
            .SingleOrDefaultAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedBatches, commands.Commands.Count(command =>
            command.StartsWith("INSERT INTO", StringComparison.Ordinal)));
        Assert.Empty(context.ChangeTracker.Entries());

        if (facts == 0)
        {
            Assert.Null(totals);

            return;
        }

        var first = nodes - facts + 1;
        Assert.NotNull(totals);
        Assert.Equal(facts, totals.Count);
        Assert.Equal(first, totals.First);
        Assert.Equal(nodes, totals.Last);
        Assert.Equal(((long)first + nodes) * facts / 2, totals.Sum);
    }
}
