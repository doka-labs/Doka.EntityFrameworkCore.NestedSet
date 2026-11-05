namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Provides each independently verified prefix of the deterministic mutation sequence.</summary>
    public static TheoryData<int> MutationPrefixes
    {
        get
        {
            var cases = new TheoryData<int>();
            for (var prefix = 0; prefix < 36; prefix++)
            {
                cases.Add(prefix);
            }

            return cases;
        }
    }

    /// <summary>Each generated insertion or move preserves all forest invariants.</summary>
    /// <param name="prefix">The number of earlier operations used only to arrange this case.</param>
    /// <returns>A task that completes when the selected mutation has been verified.</returns>
    [Theory]
    [MemberData(nameof(MutationPrefixes))]
    public async Task GeneratedMutationPreservesForestInvariants(
        int prefix
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var random = new Random(1729);

        // WHY: Replaying only the prefix preserves the old intermediate checks as independently discoverable cases.
        for (var step = 0; step < prefix; step++)
        {
            await ApplyGeneratedMutationAsync(context, random, step,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // Act
        await ApplyGeneratedMutationAsync(context, random, prefix,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await AssertValidAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Applies one deterministic generated operation without assertions or further test phases.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task ApplyGeneratedMutationAsync(
        TreeContext context,
        Random random,
        int step,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .OrderBy(x => x.NodeId)
            .ToListAsync(cancellationToken);

        if (step < 12)
        {
            var id = step + 501;
            if (nodes.Count == 0
                || random.Next(3) == 0)
            {
                await tree.InsertRootAsync(Node(id), Guid.NewGuid(), cancellationToken);
            }
            else
            {
                var parent = nodes[random.Next(nodes.Count)].NodeId;
                if (random.Next(2) == 0)
                {
                    await tree.InsertAsFirstChildAsync(Node(id), parent, cancellationToken);
                }
                else
                {
                    await tree.InsertAsLastChildAsync(Node(id), parent, cancellationToken);
                }
            }
        }
        else
        {
            var source = nodes[random.Next(nodes.Count)];
            var targets = nodes
                .Where(x => x.NodeId != source.NodeId
                    && !(x.TreeId == source.TreeId && x.Start > source.Start && x.End < source.End))
                .ToArray();

            if (targets.Length == 0)
            {
                await tree.DetachAsTreeAsync(source.NodeId, Guid.NewGuid(), cancellationToken);
            }
            else
            {
                var target = targets[random.Next(targets.Length)];
                switch ((step - 12) % 5)
                {
                    case 0:
                        if (source.Parent != null
                            && target.Parent != null
                            && source.TreeId == target.TreeId)
                        {
                            await tree.MoveBeforeAsync(source.NodeId, target.NodeId, cancellationToken);
                        }
                        else
                        {
                            await tree.MoveToAsync(source.NodeId, target.NodeId, cancellationToken);
                        }

                        break;
                    case 1:
                        if (source.Parent != null
                            && target.Parent != null
                            && source.TreeId == target.TreeId)
                        {
                            await tree.MoveAfterAsync(source.NodeId, target.NodeId, cancellationToken);
                        }
                        else
                        {
                            await tree.MoveToAsync(source.NodeId, target.NodeId, cancellationToken);
                        }

                        break;
                    case 2:
                        var firstChild = nodes
                            .Where(node => node.TreeId == target.TreeId && node.Parent == target.NodeId)
                            .OrderBy(node => node.Position)
                            .FirstOrDefault();

                        if (firstChild != null
                            && source.Parent != null
                            && source.TreeId == firstChild.TreeId
                            && firstChild.NodeId != source.NodeId)
                        {
                            await tree.MoveBeforeAsync(source.NodeId, firstChild.NodeId, cancellationToken);
                        }
                        else
                        {
                            await tree.MoveToAsync(source.NodeId, target.NodeId, cancellationToken);
                        }

                        break;
                    case 3:
                        await tree.MoveToAsync(source.NodeId, target.NodeId, cancellationToken);
                        break;
                    default:
                        await tree.DetachAsTreeAsync(source.NodeId, Guid.NewGuid(), cancellationToken);
                        break;
                }
            }
        }
    }
}
