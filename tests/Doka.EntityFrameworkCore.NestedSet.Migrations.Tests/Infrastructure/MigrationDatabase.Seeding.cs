namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class MigrationDatabase
{
    /// <summary>Stores two hierarchy rows for the requested historical model.</summary>
    /// <param name="stage">The schema shape that currently exists in the database.</param>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>A task that completes after the seed rows are saved.</returns>
    public async Task SeedAsync(
        MigrationStage stage,
        CancellationToken cancellationToken
    )
    {
        if (stage != MigrationStage.Baseline)
        {
            await InsertNodeDirectlyAsync(
                new MigrationNode
                {
                    NodeId = 41,
                    Scope = 7,
                    TreeId = 11,
                    Left = 1,
                    Right = 4,
                    Payload = "parent payload",
                    Category = 2,
                },
                cancellationToken: cancellationToken);

            await InsertNodeDirectlyAsync(
                new MigrationNode
                {
                    NodeId = 42,
                    Scope = 7,
                    TreeId = 11,
                    ParentId = 41,
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                    Payload = "child payload",
                    Category = 1,
                },
                cancellationToken: cancellationToken);

            return;
        }

        await using var context = CreateContext(stage);
        await context
            .Set<BaselineMigrationNode>(MigrationContext.EntityTypeName)
            .AddRangeAsync(
                [
                    new BaselineMigrationNode
                    {
                        NodeId = 41,
                        Scope = 7,
                        TreeId = 11,
                        Left = 1,
                        Right = 4,
                        Payload = "parent payload",
                        Category = 2,
                    },
                    new BaselineMigrationNode
                    {
                        NodeId = 42,
                        Scope = 7,
                        TreeId = 11,
                        ParentId = 41,
                        Left = 2,
                        Right = 3,
                        Depth = 1,
                        Payload = "child payload",
                        Category = 1,
                    },
                ],
                cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts one current row directly so database constraints can be qualified independently of save logic.
    /// </summary>
    /// <param name="node">The complete physical row to insert.</param>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>A task that completes with the affected-row count.</returns>
    public async Task<int> InsertNodeDirectlyAsync(
        MigrationNode node,
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext();
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(MigrationContext.TableName, Schema);
        var columns = new[]
        {
            "entry_id",
            "tree_scope",
            "tree_id",
            "parent_entry",
            "left_bound",
            "right_bound",
            "entry_depth",
            "sibling_position",
            "entry_payload",
            "entry_category",
        };

        var command = "INSERT INTO "
            + table
            + " ("
            + string.Join(", ", columns.Select(sql.DelimitIdentifier))
            + ") VALUES ("
            + string.Join(
                ", ",
                Enumerable
                    .Range(0, columns.Length)
                    .Select(index => "{" + index + "}"))
            + ")";

        var parent = context
            .Database
            .GetDbConnection()
            .CreateCommand()
            .CreateParameter();

        parent.ParameterName = "@parent";
        parent.DbType = DbType.Int32;
        parent.Value = node.ParentId is null
            ? DBNull.Value
            : node.ParentId.Value;

        return await context.Database.ExecuteSqlRawAsync(
            command,
            [
                node.NodeId,
                node.Scope,
                node.TreeId,
                parent,
                node.Left,
                node.Right,
                node.Depth,
                node.Position,
                node.Payload,
                node.Category,
            ],
            cancellationToken);
    }
}
