namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

public sealed partial class DerivedRowVersionTests
{
    /// <summary>Seeds a concrete base, an ordinary sibling, and three token-bearing descendants.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static async Task<DerivedVersionBaseNode[]> SeedAsync(
        DerivedRowVersionContext context,
        CancellationToken cancellationToken
    )
    {
        var hierarchy = context
            .NestedSet<DerivedVersionBaseNode>()
            .ForScope(1);

        await hierarchy.InsertForestAsync(
            [
                new NestedSetTreeImport<DerivedVersionBaseNode, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<DerivedVersionBaseNode>(
                        new DerivedVersionBaseNode
                        {
                            Id = 1,
                            Name = "Root",
                        },
                        [
                            new NestedSetBranch<DerivedVersionBaseNode>(
                                new DerivedVersionTokenNode
                                {
                                    Id = 2,
                                    Name = "Alpha",
                                },
                                [
                                    new NestedSetBranch<DerivedVersionBaseNode>(
                                        new DerivedVersionTokenNode
                                        {
                                            Id = 4,
                                            Name = "Leaf",
                                        }),
                                ]),
                            new NestedSetBranch<DerivedVersionBaseNode>(
                                new DerivedVersionPlainNode
                                {
                                    Id = 3,
                                    Name = "Middle",
                                }),
                            new NestedSetBranch<DerivedVersionBaseNode>(
                                new DerivedVersionTokenNode
                                {
                                    Id = 5,
                                    Name = "Other",
                                }),
                        ])),
                new NestedSetTreeImport<DerivedVersionBaseNode, Guid>(
                    s_destinationTree,
                    new NestedSetBranch<DerivedVersionBaseNode>(
                        new DerivedVersionBaseNode
                        {
                            Id = 6,
                            Name = "Destination root",
                        })),
            ],
            cancellationToken);

        return await context
            .Set<DerivedVersionBaseNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(cancellationToken);
    }
}
