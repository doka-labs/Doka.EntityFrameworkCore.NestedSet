namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Runs the selected public insertion API without touching the caller's detection policy.</summary>
    private static async Task InsertManualShapeAsync(
        DbContext context,
        string shape,
        OrderingNode[] inputs,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        switch (shape)
        {
            case "single":
                await tree.InsertChildAsync(inputs[0], 100, cancellationToken);
                break;
            case "subtree":
                await tree.InsertSubtreeAsync(
                    new NestedSetBranch<OrderingNode>(inputs[0], [new NestedSetBranch<OrderingNode>(inputs[1])]),
                    100,
                    cancellationToken);

                break;
            case "forest":
                await tree.InsertForestAsync(
                    [
                        new NestedSetTreeImport<OrderingNode, Guid>(
                            s_manualFirstImportTree,
                            new NestedSetBranch<OrderingNode>(
                                inputs[0],
                                [new NestedSetBranch<OrderingNode>(inputs[1])])),
                        new NestedSetTreeImport<OrderingNode, Guid>(
                            s_manualSecondImportTree,
                            new NestedSetBranch<OrderingNode>(
                                inputs[2],
                                [new NestedSetBranch<OrderingNode>(inputs[3])])),
                    ],
                    cancellationToken);

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }
}
