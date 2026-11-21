namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Mutates only raw complex CLR values at the requested callback boundary.</summary>
    private static void ChangeComplexCallbackRow(
        ComplexCallbackRow row,
        string mutation
    )
    {
        switch (mutation)
        {
            case "remove":
                row.Items!.RemoveAt(row.Items.Count - 1);
                break;
            case "nested-clear":
                row.Items![0].Parts!.Clear();
                break;
            case "list-null":
                row.Items = null;
                break;
            case "reference-null":
                row.Address = null;
                break;
            case "replace":
                row.Items = [NewComplexCallbackItem("replacement")];
                break;
            case "nested-null":
                row.Items![0].Parts = null;
                break;
            case "address-nested-clear":
                row.Address!.Tags!.Clear();
                break;
            case "edit":
                row.Items![0].Text = "callback item";
                break;
            case "add":
                row.Items!.Add(NewComplexCallbackItem("callback added"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    /// <summary>Advances only the independent application token to provoke genuine optimistic concurrency.</summary>
    private static async Task ChangeComplexCallbackVersionAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await using var external = await CreateComplexCallbackContextAsync(
            database,
            cancellationToken: cancellationToken);

        var sql = external.GetService<ISqlGenerationHelper>();
        var update = $"UPDATE {sql.DelimitIdentifier("ComplexCallbackRows")} "
            + $"SET {sql.DelimitIdentifier("Version")} = {{0}} WHERE {sql.DelimitIdentifier("Id")} = {{1}}";

        await external.Database.ExecuteSqlRawAsync(update, [8, 1], cancellationToken);
    }

    /// <summary>Executes each managed insertion form without registering hierarchy inputs before preflight.</summary>
    private static Task InsertComplexCallbackInputsAsync(
        DbContext context,
        string shape,
        NavigationNode[] inputs,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<NavigationNode>()
            .ForScope(1);

        return shape switch
        {
            "single" => tree.InsertChildAsync(inputs[0], 1, cancellationToken),
            "subtree" => tree.InsertSubtreeAsync(
                PreparedOwnerBranch(inputs, 0, inputs.Length),
                1,
                cancellationToken),
            "forest" => tree.InsertForestAsync(
                [
                    new NestedSetTreeImport<NavigationNode, Guid>(
                        s_manualFirstImportTree,
                        PreparedOwnerBranch(inputs, 0, inputs.Length / 2)),
                    new NestedSetTreeImport<NavigationNode, Guid>(
                        s_manualSecondImportTree,
                        PreparedOwnerBranch(inputs, inputs.Length / 2, inputs.Length / 2)),
                ],
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }
}
