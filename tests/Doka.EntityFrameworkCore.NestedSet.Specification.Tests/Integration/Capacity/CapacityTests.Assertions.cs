namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class CapacityTests
{
    /// <summary>Verifies every input with aggregate mismatches without allocating another full snapshot.</summary>
    private static int CountInputMismatches(
        NestedSetBranch<TreeNode> branch,
        bool restored
    )
    {
        var count = branch.Children.Count + 1;
        var mismatches = Matches(branch.Entity, 1)
            ? 0
            : 1;

        for (var index = 0; index < branch.Children.Count; index++)
        {
            if (!Matches(branch.Children[index].Entity, index + 2))
            {
                mismatches++;
            }
        }

        return mismatches;

        bool Matches(
            TreeNode node,
            int key
        ) => node.NodeId == key
            && node.Payload == "caller-owned capacity payload"
            && node.TreeId
            == (restored
                ? s_inputTreeId
                : Guid.Empty)
            && node.Tree
            == (restored
                ? 7
                : 1)
            && node.Start
            == (restored
                ? 17
                : key == 1
                    ? 1L
                    : 2L * key - 2)
            && node.End
            == (restored
                ? 18
                : key == 1
                    ? 2L * count
                    : 2L * key - 1)
            && node.Depth
            == (restored
                ? 9
                : key == 1
                    ? 0
                    : 1)
            && node.Position
            == (restored
                ? 11
                : key == 1
                    ? 0L
                    : key - 2L)
            && node.Parent
            == (restored
                ? 999
                : key == 1
                    ? null
                    : 1);
    }

    /// <summary>Checks the exact persisted result through a fresh context and public Full validation.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task AssertCanonicalAsync(
        TestDatabase database,
        int count,
        bool deep,
        CancellationToken cancellationToken
    )
    {
        await using var verification = CreateCapacityContext(database);
        Assert.Equal(
            count,
            await verification
                .Set<TreeNode>()
                .LongCountAsync(cancellationToken));

        Assert.Equal(
            0,
            await CapacitySeed.MismatchesAsync(
                verification,
                count,
                cancellationToken: cancellationToken,
                deep: deep,
                corrupt: false));

        var report = await verification
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

        Assert.True(report.IsValid);
        Assert.Equal(count, report.NodeCount);
        Assert.Empty(report.Issues);
    }

    /// <summary>Checks the exact injected failure class independently of the rollback assertions.</summary>
    private void AssertFailure(
        Exception? error,
        bool cancel
    )
    {
        if (error is AggregateException)
        {
            // WHY: The exact-type assertion otherwise hides the operation and cleanup errors needed to diagnose
            // failure.
            _output.WriteLine(error.ToString());
        }

        if (cancel)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        }
        else
        {
            Assert.IsType<InjectedCommandException>(error);
        }
    }

}
