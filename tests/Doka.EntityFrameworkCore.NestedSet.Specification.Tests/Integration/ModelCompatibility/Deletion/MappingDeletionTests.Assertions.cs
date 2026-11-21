namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class MappingDeletionTests
{
    private static async Task VerifyAsync<TNode>(
        DbContext context,
        MappingDeleteScenario scenario,
        MappingDeleteOperation operation,
        CancellationToken cancellationToken,
        params string[] tables
    )
        where TNode : class
    {
        var hierarchy = context.NestedSet<TNode>();
        var seed = scenario.Seed;

        var actual = await context
            .Set<TNode>()
            .AsNoTracking()
            .Where(node => EF.Property<int>(node, "Id") >= seed && EF.Property<int>(node, "Id") <= seed + 4)
            .Select(node => EF.Property<int>(node, "Id"))
            .OrderBy(id => id)
            .ToArrayAsync(cancellationToken);

        int[] expected = operation switch
        {
            MappingDeleteOperation.Node => [seed, seed + 2, seed + 3, seed + 4],
            MappingDeleteOperation.Subtree => [seed, seed + 3, seed + 4],
            MappingDeleteOperation.Tree => [seed + 4],
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        Assert.Equal(expected, actual);
        Assert.True(
            (await hierarchy
                .InTree(scenario.OtherTree)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);

        if (operation != MappingDeleteOperation.Tree)
        {
            Assert.True(
                (await hierarchy
                    .InTree(scenario.TargetTree)
                    .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);
        }

        foreach (var table in tables)
        {
            Assert.Equal(
                expected.Length,
                await MappingDeleteTestSupport.CountRowsAsync(
                    context,
                    table,
                    seed,
                    cancellationToken: cancellationToken,
                    lastOffset: 4));
        }
    }
}
