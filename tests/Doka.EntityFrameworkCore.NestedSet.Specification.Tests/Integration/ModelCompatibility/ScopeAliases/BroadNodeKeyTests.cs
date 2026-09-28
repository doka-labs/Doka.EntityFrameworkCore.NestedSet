namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that converted node and parent keys retain their stored identities.</summary>
[Collection("Model compatibility")]
public abstract class BroadNodeKeyTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares provider databases with the other mapping-compatibility tests.</summary>
    protected BroadNodeKeyTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Compares node and parent keys by stored value despite broad domain equality.</summary>
    [Fact]
    public void ProviderComparerSeparatesKeyAliases()
    {
        // Arrange
        using var context = new BroadNodeKeyContext(ModelCompatibilityDatabase.Options<BroadNodeKeyContext>(Engine));

        var entity = context.Model.FindEntityType(typeof(BroadNodeKeyNode))!;
        var key = entity.FindProperty(nameof(BroadNodeKeyNode.Id))!;
        var parent = entity.FindProperty(nameof(BroadNodeKeyNode.ParentId))!;
        var first = new BroadNodeKey("A");
        var second = new BroadNodeKey("a");

        // Act
        var keyEqual = NestedSetProviderComparer.Matches(key, first, second);
        var parentEqual = NestedSetProviderComparer.Matches(parent, first, second);

        // Assert
        Assert.True(first.Equals(second));
        Assert.False(keyEqual);
        Assert.False(parentEqual);
    }

    /// <summary>A callback cannot redirect a single child to a database-distinct parent key.</summary>
    [Fact]
    public async Task SingleInsertRejectsCallbackParentAliasAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadNodeKeyContext>(
            Engine,
            static options => new BroadNodeKeyContext(options));

        var hierarchy = context.NestedSet<BroadNodeKeyNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(
            new BroadNodeKeyNode { Id = new BroadNodeKey("A") },
            treeId,
            CancellationToken.None);

        var input = new BroadNodeKeyNode { Id = new BroadNodeKey("B") };
        context.SavingChanges += (_, _) => input.ParentId = new BroadNodeKey("a");

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertChildAsync(
            input,
            new BroadNodeKey("A"),
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Null(input.ParentId);
        Assert.Equal(
            1,
            await context
                .Set<BroadNodeKeyNode>()
                .CountAsync(node => node.TreeId == treeId, CancellationToken.None));
    }

    /// <summary>A callback cannot redirect a bulk child to a database-distinct parent key.</summary>
    [Fact]
    public async Task BulkInsertRejectsCallbackParentAliasAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadNodeKeyContext>(
            Engine,
            static options => new BroadNodeKeyContext(options));

        var root = new BroadNodeKeyNode { Id = new BroadNodeKey("P") };
        var child = new BroadNodeKeyNode { Id = new BroadNodeKey("Q") };
        var treeId = Guid.NewGuid();
        var branch = new NestedSetBranch<BroadNodeKeyNode>(root, [new NestedSetBranch<BroadNodeKeyNode>(child)]);

        context.SavingChanges += (_, _) =>
        {
            if (child.ParentId?.Value == "P")
            {
                child.ParentId = new BroadNodeKey("p");
            }
        };

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadNodeKeyNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<BroadNodeKeyNode, Guid>(treeId, branch)],
                CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
        Assert.Null(child.ParentId);
        Assert.Equal(
            0,
            await context
                .Set<BroadNodeKeyNode>()
                .CountAsync(node => node.TreeId == treeId, CancellationToken.None));
    }

    /// <summary>A database-distinct replacement key cannot satisfy a bulk import's row membership.</summary>
    [Fact]
    public async Task BulkRefreshRejectsStoredKeyAliasAsync()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe("BroadNodeKeyNodes");
        await using var context = await _fixture.CreateContextAsync<BroadNodeKeyContext>(
            Engine,
            static options => new BroadNodeKeyContext(options),
            probe);

        var input = new BroadNodeKeyNode { Id = new BroadNodeKey("C") };
        var treeId = Guid.NewGuid();
        var sql = ReplacementSql(context);
        probe.BeforeRefresh = (command, token) => ReplaceKeyAsync(command, sql, "C", "c", token);
        context.SavedChanges += (_, _) => probe.Inserted = true;

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadNodeKeyNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<BroadNodeKeyNode, Guid>(treeId, new NestedSetBranch<BroadNodeKeyNode>(input))],
                CancellationToken.None));

        probe.Inserted = false;

        // Assert
        Assert.IsType<DbUpdateConcurrencyException>(error);
        Assert.Single(probe.Commands);
        Assert.Equal("C", input.Id.Value);
        Assert.Equal(
            0,
            await context
                .Set<BroadNodeKeyNode>()
                .CountAsync(node => node.TreeId == treeId, CancellationToken.None));
    }

    /// <summary>A changed input key cannot be accepted by broad domain equality after bulk refresh.</summary>
    [Fact]
    public async Task BulkRefreshRejectsInputKeyAliasAsync()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe("BroadNodeKeyNodes");
        await using var context = await _fixture.CreateContextAsync<BroadNodeKeyContext>(
            Engine,
            static options => new BroadNodeKeyContext(options),
            probe);

        var input = new BroadNodeKeyNode { Id = new BroadNodeKey("D") };
        var treeId = Guid.NewGuid();
        probe.BeforeRefresh = (_, _) =>
        {
            input.Id = new BroadNodeKey("d");

            return Task.CompletedTask;
        };

        context.SavedChanges += (_, _) => probe.Inserted = true;

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadNodeKeyNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<BroadNodeKeyNode, Guid>(treeId, new NestedSetBranch<BroadNodeKeyNode>(input))],
                CancellationToken.None));

        probe.Inserted = false;

        // Assert
        Assert.IsType<DbUpdateConcurrencyException>(error);
        Assert.Single(probe.Commands);
        Assert.Equal("D", input.Id.Value);
        Assert.Equal(
            0,
            await context
                .Set<BroadNodeKeyNode>()
                .CountAsync(node => node.TreeId == treeId, CancellationToken.None));
    }

    /// <summary>A sorted refresh rejects a stored alias even when database and EF equality accept it.</summary>
    [Fact]
    public async Task SortedBulkRefreshRejectsStoredKeyAliasAsync()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe("OrderedBroadNodeKeyNodes");
        await using var context = await _fixture.CreateContextAsync<OrderedBroadNodeKeyContext>(
            Engine,
            static options => new OrderedBroadNodeKeyContext(options),
            probe);

        var input = new BroadNodeKeyNode
        {
            Id = new BroadNodeKey("E"),
            Name = "Root",
        };

        var treeId = Guid.NewGuid();
        var sql = ReplacementSql(context);
        probe.BeforeRefresh = (command, token) => ReplaceKeyAsync(command, sql, "E", "e", token);
        context.SavedChanges += (_, _) => probe.Inserted = true;

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadNodeKeyNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<BroadNodeKeyNode, Guid>(treeId, new NestedSetBranch<BroadNodeKeyNode>(input))],
                CancellationToken.None));

        probe.Inserted = false;

        // Assert
        Assert.Equal(
            "The imported refresh contains an unexpected node.",
            Assert.IsType<DbUpdateConcurrencyException>(error).Message);
        Assert.Single(probe.Commands);
        Assert.Equal("E", input.Id.Value);
        Assert.Equal(
            0,
            await context
                .Set<BroadNodeKeyNode>()
                .CountAsync(node => node.TreeId == treeId, CancellationToken.None));
    }

    /// <summary>A sorted refresh rejects a callback's input alias after reading the unchanged row.</summary>
    [Fact]
    public async Task SortedBulkRefreshRejectsInputKeyAliasAsync()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe("OrderedBroadNodeKeyNodes");
        await using var context = await _fixture.CreateContextAsync<OrderedBroadNodeKeyContext>(
            Engine,
            static options => new OrderedBroadNodeKeyContext(options),
            probe);

        var input = new BroadNodeKeyNode
        {
            Id = new BroadNodeKey("F"),
            Name = "Root",
        };

        var treeId = Guid.NewGuid();
        probe.BeforeRefresh = (_, _) =>
        {
            input.Id = new BroadNodeKey("f");

            return Task.CompletedTask;
        };

        context.SavedChanges += (_, _) => probe.Inserted = true;

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadNodeKeyNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<BroadNodeKeyNode, Guid>(treeId, new NestedSetBranch<BroadNodeKeyNode>(input))],
                CancellationToken.None));

        probe.Inserted = false;

        // Assert
        Assert.Equal(
            "An imported input changed its identity during refresh.",
            Assert.IsType<DbUpdateConcurrencyException>(error).Message);
        Assert.Single(probe.Commands);
        Assert.Equal("F", input.Id.Value);
        Assert.Equal(
            0,
            await context
                .Set<BroadNodeKeyNode>()
                .CountAsync(node => node.TreeId == treeId, CancellationToken.None));
    }

    /// <summary>Builds a provider-delimited statement to change the persisted node identity.</summary>
    private static string ReplacementSql(
        DbContext context
    )
    {
        var entity = context.Model.FindEntityType(typeof(BroadNodeKeyNode))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

        var sql = context.GetService<ISqlGenerationHelper>();
        var key = sql.DelimitIdentifier(
            entity.FindPrimaryKey()!
                .Properties[0]
                .GetColumnName(table)!);

        return $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {key} = @replacement WHERE {key} = @key";
    }

    /// <summary>Replaces a stored key inside the import transaction before its final reader executes.</summary>
    private static async Task ReplaceKeyAsync(
        DbCommand source,
        string sql,
        string key,
        string replacement,
        CancellationToken cancellationToken
    )
    {
        await using var command = source.Connection!.CreateCommand();
        command.Transaction = source.Transaction;
        command.CommandText = sql;
        var original = command.CreateParameter();
        original.ParameterName = "key";
        original.Value = key;
        command.Parameters.Add(original);
        var changed = command.CreateParameter();
        changed.ParameterName = "replacement";
        changed.Value = replacement;
        command.Parameters.Add(changed);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
