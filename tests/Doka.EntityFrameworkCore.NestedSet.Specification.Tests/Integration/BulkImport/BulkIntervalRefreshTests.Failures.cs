namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class BulkIntervalRefreshTests
{
    /// <summary>Combines all supported engines with distinct physical row-membership failures.</summary>
    public static TheoryData<string> InvalidRowCases
    {
        get
        {
            var cases = new TheoryData<string>();

            foreach (var failure in new[] { "missing", "extra", "escaped", "replacement" })
            {
                cases.Add(failure);
            }

            return cases;
        }
    }

    /// <summary>Every expected key must occur once in the interval, even when row count is unchanged.</summary>
    [Theory]
    [MemberData(nameof(InvalidRowCases))]
    public async Task UnexpectedPhysicalRowsFailAndRestoreTheEntireImport(
        string failure
    )
    {
        // Arrange
        var database = await _assigned.ResetAsync(Engine);
        var probe = new BulkIntervalRefreshProbe(nameof(TreeNode));
        await using var context = database.CreateContext(probe);
        await SeedAssignedAsync(context);
        var nodes = AssignedNodes();
        var mutation = MutationSql(context, failure);
        probe.BeforeRefresh = (command, token) => MutateAsync(command, mutation, nodes[^1].NodeId, token);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var branch = new NestedSetBranch<TreeNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<TreeNode>(node))
                .ToArray());

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync(branch, 1, CancellationToken.None));
        probe.Inserted = false;

        // Assert
        Assert.IsType<DbUpdateConcurrencyException>(error);
        Assert.NotEmpty(probe.Commands);
        Assert.All(
            nodes,
            node => Assert.Equal((17, 71, 72, "Retained"), (node.Tree, node.Start, node.End, node.Payload)));
        Assert.Equal(
            2,
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .CountAsync(CancellationToken.None));
        Assert.Equal(
            4,
            await context
                .Set<TreeNode>()
                .CountAsync(CancellationToken.None));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A refresh failure rolls back only the import savepoint and retains the caller's earlier work.</summary>
    [Fact]
    public async Task CallerTransactionRetainsEarlierWorkWhenRefreshFindsAnUnexpectedRow()
    {
        // Arrange
        var database = await _assigned.ResetAsync(Engine);
        var probe = new BulkIntervalRefreshProbe(nameof(TreeNode));
        await using var context = database.CreateContext(probe);
        await SeedAssignedAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync(
            context.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            CancellationToken.None);

        await context.AddAsync(
            new UnrelatedRow
            {
                Id = 1,
                Value = "Before import",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var mutation = MutationSql(context, "extra");
        probe.BeforeRefresh = (command, token) => MutateAsync(command, mutation, 140, token);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var nodes = AssignedNodes();
        var branch = new NestedSetBranch<TreeNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<TreeNode>(node))
                .ToArray());

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync(branch, 1, CancellationToken.None));
        probe.Inserted = false;

        // Assert
        Assert.IsType<DbUpdateConcurrencyException>(error);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(
            "Before import",
            await context
                .Set<UnrelatedRow>()
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(
            2,
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .CountAsync(CancellationToken.None));
        Assert.All(nodes, node => Assert.Equal((17, 71, 72), (node.Tree, node.Start, node.End)));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Cancellation after all writes restores generated identities, defaults and complex values.</summary>
    [Fact]
    public async Task CanceledGeneratedRefreshRestoresEveryInput()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var probe = new BulkIntervalRefreshProbe(nameof(BulkIntervalNode))
        {
            BeforeRefresh = async (_, _) =>
            {
                await cancellation.CancelAsync();
                cancellation.Token.ThrowIfCancellationRequested();
            },
        };

        await using var context = await _generated.ResetAsync(Engine, probe);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<BulkIntervalNode>()
            .ForScope(1);

        var nodes = Enumerable
            .Range(0, NodeCount)
            .Select(_ => new BulkIntervalNode
            {
                Scope = 17,
                Left = 71,
                Right = 72,
                Payload = "Retained",
            })
            .ToArray();

        var branch = new NestedSetBranch<BulkIntervalNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<BulkIntervalNode>(node))
                .ToArray());

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkIntervalNode, Guid>(Guid.Empty, branch),],
            cancellation.Token));

        probe.Inserted = false;

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Single(probe.Commands);
        Assert.All(
            nodes,
            node =>
            {
                Assert.Equal(
                    (0, 17, 71, 72, 0, 0),
                    (node.Id, node.Scope, node.Left, node.Right, node.Version, node.Details.Revision));
                Assert.Null(node.Name);
                Assert.Null(node.Details.Label);
                Assert.Equal("Retained", node.Payload);
            });
        Assert.Empty(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Creates deliberate in-transaction corruption using the fixture's actual mapped identifiers.</summary>
    private static string MutationSql(
        TreeContext context,
        string failure
    )
    {
        var entity = context.Model.FindEntityType(typeof(TreeNode))!;
        var table = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
            entity.GetTableName()!,
            entity.GetSchema());

        var sql = context.GetService<Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper>();
        var name = sql.DelimitIdentifier(table.Name, table.Schema);

        return failure switch
        {
            "missing" => $"DELETE FROM {name} WHERE {Column(nameof(TreeNode.NodeId))} = @key",
            "replacement" =>
                $"UPDATE {name} SET {Column(nameof(TreeNode.NodeId))} = 999999 "
                + $"WHERE {Column(nameof(TreeNode.NodeId))} = @key",
            "escaped" =>
                $"UPDATE {name} SET {Column(nameof(TreeNode.Start))} = 999998, "
                + $"{Column(nameof(TreeNode.End))} = 999999 WHERE {Column(nameof(TreeNode.NodeId))} = @key",
            "extra" => $"INSERT INTO {name} ({Column(nameof(TreeNode.NodeId))}, {Column(nameof(TreeNode.Tree))}, "
                + $"{Column(nameof(TreeNode.TreeId))}, {Column(nameof(TreeNode.Start))}, "
                + $"{Column(nameof(TreeNode.End))}, {Column(nameof(TreeNode.Depth))}, "
                + $"{Column(nameof(TreeNode.Position))}) SELECT @key + 1000, "
                + $"{Column(nameof(TreeNode.Tree))}, {Column(nameof(TreeNode.TreeId))}, 4, 5, 1, 1 FROM {name} "
                + $"WHERE {Column(nameof(TreeNode.NodeId))} = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
    }

    /// <summary>Changes a physical row in the operation's transaction without altering tracked inputs.</summary>
    private static async Task MutateAsync(
        DbCommand source,
        string sql,
        int key,
        CancellationToken cancellationToken
    )
    {
        await using var command = source.Connection!.CreateCommand();
        command.Transaction = source.Transaction;
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
