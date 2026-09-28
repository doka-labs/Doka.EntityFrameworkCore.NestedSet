namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class BulkIntervalRefreshTests
{
    /// <summary>A broad application comparer cannot authorize a database-distinct replacement key.</summary>
    [Fact]
    public async Task ComparerEqualReplacementKeyFailsExactMembership()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe(nameof(BulkStageTextNode));
        await using var context = await _generated.ResetAsync(Engine, probe);
        var input = new BulkStageTextNode
        {
            Id = "C",
            Scope = "Original",
            Left = 71,
            Right = 72,
        };

        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope("S");

        var sql = ReplacementSql<BulkStageTextNode>(context);
        probe.BeforeRefresh = (command, token) => ReplaceKeyAsync(command, sql, "C", "c", token);
        context.SavedChanges += (_, _) => probe.Inserted = true;

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkStageTextNode, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<BulkStageTextNode>(input)),
            ],
            CancellationToken.None));

        probe.Inserted = false;

        // Assert
        Assert.IsType<DbUpdateConcurrencyException>(error);
        Assert.Single(probe.Commands);
        Assert.Equal(("C", "Original", 71, 72), (input.Id, input.Scope, input.Left, input.Right));
        Assert.Empty(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Reader callbacks cannot mutate the separately captured expected binary identity.</summary>
    [Fact]
    public async Task MutableInputKeyCannotAuthorizeReplacementRow()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe(nameof(BulkStageBinaryParent));
        await using var context = await _generated.ResetAsync(Engine, probe);
        var input = new BulkStageBinaryParent
        {
            Id = [1],
            Scope = 17,
            Left = 71,
            Right = 72,
        };

        var tree = context
            .NestedSet<BulkStageBinaryParent>()
            .ForScope(1);

        var sql = ReplacementSql<BulkStageBinaryParent>(context);
        byte[] originalKey = [1];
        byte[] replacementKey = [2];
        probe.BeforeRefresh = async (command, token) =>
        {
            await ReplaceKeyAsync(command, sql, originalKey, replacementKey, token);
            input.Id[0] = 2;
        };

        context.SavedChanges += (_, _) => probe.Inserted = true;

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkStageBinaryParent, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<BulkStageBinaryParent>(input)),
            ],
            CancellationToken.None));

        probe.Inserted = false;

        // Assert
        Assert.IsType<DbUpdateConcurrencyException>(error);
        Assert.Single(probe.Commands);
        Assert.Equal<byte>([1], input.Id);
        Assert.Equal((17, 71, 72), (input.Scope, input.Left, input.Right));
        Assert.Empty(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A reader callback cannot leave a successful input with an identity different from its row.</summary>
    [Fact]
    public async Task MutableInputKeyWithoutDatabaseChangeFailsAndRestoresInput()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe(nameof(BulkStageBinaryParent));
        await using var context = await _generated.ResetAsync(Engine, probe);
        var input = new BulkStageBinaryParent
        {
            Id = [1],
            Scope = 17,
            Left = 71,
            Right = 72,
        };

        var tree = context
            .NestedSet<BulkStageBinaryParent>()
            .ForScope(1);

        probe.BeforeRefresh = (_, _) =>
        {
            input.Id[0] = 2;

            return Task.CompletedTask;
        };

        context.SavedChanges += (_, _) => probe.Inserted = true;

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkStageBinaryParent, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<BulkStageBinaryParent>(input)),
            ],
            CancellationToken.None));

        probe.Inserted = false;

        // Assert
        _output.WriteLine($"{Engine}: callback-only key mutation; error={error?.GetType().Name ?? "none"}");
        Assert.NotNull(error);
        Assert.Single(probe.Commands);
        Assert.Equal<byte>([1], input.Id);
        Assert.Equal((17, 71, 72), (input.Scope, input.Left, input.Right));
        Assert.Empty(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Builds a replacement statement against the fixture's mapped primary key and table.</summary>
    private static string ReplacementSql<TEntity>(
        DbContext context
    )
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

        var sql = context.GetService<ISqlGenerationHelper>();
        var key = sql.DelimitIdentifier(
            entity.FindPrimaryKey()!
                .Properties[0]
                .GetColumnName(table)!);

        return $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {key} = @replacement WHERE {key} = @key";
    }

    /// <summary>Changes one persisted identity inside the import transaction before its reader executes.</summary>
    private static async Task ReplaceKeyAsync(
        DbCommand source,
        string sql,
        object key,
        object replacement,
        CancellationToken cancellationToken
    )
    {
        await using var command = source.Connection!.CreateCommand();
        command.Transaction = source.Transaction;
        command.CommandText = sql;
        var oldKey = command.CreateParameter();
        oldKey.ParameterName = "key";
        oldKey.Value = key;
        command.Parameters.Add(oldKey);
        var newKey = command.CreateParameter();
        newKey.ParameterName = "replacement";
        newKey.Value = replacement;
        command.Parameters.Add(newKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
