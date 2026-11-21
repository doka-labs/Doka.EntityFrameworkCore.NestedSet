namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Executes the three existing public insertion forms using navigation-mapped inputs.</summary>
    private static async Task InsertPreparedNavigationShapeAsync(
        DbContext context,
        string shape,
        NavigationNode[] inputs,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<NavigationNode>()
            .ForScope(inputs[0].Scope);

        switch (shape)
        {
            case "single":
                await tree.InsertChildAsync(inputs[0], 100, cancellationToken);
                break;
            case "subtree":
                await tree.InsertSubtreeAsync(
                    new NestedSetBranch<NavigationNode>(inputs[0], [new NestedSetBranch<NavigationNode>(inputs[1])]),
                    100,
                    cancellationToken);

                break;
            case "forest":
                await tree.InsertForestAsync(
                    [
                        new NestedSetTreeImport<NavigationNode, Guid>(
                            s_manualFirstImportTree,
                            new NestedSetBranch<NavigationNode>(
                                inputs[0],
                                [new NestedSetBranch<NavigationNode>(inputs[1])])),
                        new NestedSetTreeImport<NavigationNode, Guid>(
                            s_manualSecondImportTree,
                            new NestedSetBranch<NavigationNode>(
                                inputs[2],
                                [new NestedSetBranch<NavigationNode>(inputs[3])])),
                    ],
                    cancellationToken);

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }
}
