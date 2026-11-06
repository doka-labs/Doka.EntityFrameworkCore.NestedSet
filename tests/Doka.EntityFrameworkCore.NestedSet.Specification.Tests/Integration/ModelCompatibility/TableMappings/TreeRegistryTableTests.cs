using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks explicitly named tree registries against actual DDL, runtime writes and migrations.</summary>
[Collection("Model compatibility")]
public abstract class TreeRegistryTableTests : ProviderTest
{
    private const string NamedTable = "Reporting Hierarchy-Trees";

    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected TreeRegistryTableTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Quotes a name that needs delimiters and leaves the neighboring default registry untouched.</summary>
    [DatabaseIndependent]
    [Fact]
    public void ScriptCreatesTheConfiguredRegistryNextToTheDefaultOne()
    {
        // Arrange
        using var context = new NamedRegistryContext(ModelCompatibilityDatabase.Options<NamedRegistryContext>(Engine));

        var sql = context.GetService<ISqlGenerationHelper>();

        // Act
        var script = context.Database.GenerateCreateScript();

        // Assert
        Assert.Contains(sql.DelimitIdentifier(NamedTable), script, StringComparison.Ordinal);
        Assert.Contains("DokaNestedSetTrees_", script, StringComparison.Ordinal);
    }

    /// <summary>Reaches only the configured table for every structural and administrative operation.</summary>
    [Fact]
    public async Task EveryRegistryOperationUsesTheConfiguredTable()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<NamedRegistryContext>(
            Engine,
            static options => new NamedRegistryContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context.NestedSet<ReportingNode>();
        var other = context.NestedSet<OrganizationNode>();
        var retired = Guid.NewGuid();
        var active = Guid.NewGuid();
        var mapping = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(ReportingNode))!);

        // Act
        await hierarchy.InsertRootAsync(
            new ReportingNode
            {
                Id = 1,
                Name = "Root",
            },
            active,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ReportingNode
            {
                Id = 2,
                Name = "Child",
            },
            1L,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ReportingNode
            {
                Id = 3,
                Name = "Sibling",
            },
            1L,
            CancellationToken.None);

        await hierarchy.MoveToAsync(3L, 2L, CancellationToken.None);
        await hierarchy.InsertRootAsync(
            new ReportingNode
            {
                Id = 4,
                Name = "Old",
            },
            retired,
            CancellationToken.None);

        await hierarchy.DeleteTreeAsync(retired, CancellationToken.None);
        await other.InsertRootAsync(
            new OrganizationNode
            {
                Id = 1,
                Name = "Same id",
            },
            active,
            CancellationToken.None);

        var beforePurge = await ReadLifecyclesAsync(
            context,
            mapping.Registry.Name,
            cancellationToken: TestContext.Current.CancellationToken);

        await hierarchy.PurgeTreeIdAsync(retired, CancellationToken.None);
        var afterPurge = await ReadLifecyclesAsync(
            context,
            mapping.Registry.Name,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(NamedTable, mapping.Store.Name);
        Assert.Equal(NestedSetTreeRegistryMetadata.Tombstoned, beforePurge[retired]);
        Assert.Equal(NestedSetTreeRegistryMetadata.Active, beforePurge[active]);
        Assert.Equal([active], afterPurge.Keys);
        Assert.Equal(
            1,
            await CountRowsAsync(
                context,
                NamedTable,
                mapping.Registry.GetSchema(),
                cancellationToken: TestContext.Current.CancellationToken));

        var nodes = await hierarchy
            .InTree(active)
            .Nodes
            .Select(node => node.Id)
            .OrderBy(id => id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(new long[] { 1, 2, 3 }, nodes);
    }

    /// <summary>Renames forward and back, keeping active, revised and tombstoned identities each way.</summary>
    [Fact]
    public async Task NamingAnExistingRegistryMigratesAsDataPreservingRename()
    {
        // Arrange
        await using var before = await _fixture.CreateContextAsync<RegistryMigrationBeforeContext>(
            Engine,
            static options => new RegistryMigrationBeforeContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        await using var after = new RegistryMigrationAfterContext(
            ModelCompatibilityDatabase.Options<RegistryMigrationAfterContext>(
                Engine,
                before.Database.GetConnectionString()!));

        var active = Guid.NewGuid();
        var retired = Guid.NewGuid();
        var hierarchy = before.NestedSet<ReportingNode>();
        await hierarchy.InsertRootAsync(
            new ReportingNode
            {
                Id = 1,
                Name = "Root",
            },
            active,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ReportingNode
            {
                Id = 2,
                Name = "Child",
            },
            1L,
            CancellationToken.None);

        await hierarchy.InsertRootAsync(
            new ReportingNode
            {
                Id = 3,
                Name = "Old",
            },
            retired,
            CancellationToken.None);

        await hierarchy.DeleteTreeAsync(retired, CancellationToken.None);
        var registry = NestedSetTreeRegistryMapping.For(before.Model.FindEntityType(typeof(ReportingNode))!)
            .Registry;

        var rowsBefore = await ReadRowsAsync(
            before,
            registry.Name,
            cancellationToken: TestContext.Current.CancellationToken);

        var differ = before.GetService<IMigrationsModelDiffer>();
        var source = before
            .GetService<IDesignTimeModel>()
            .Model
            .GetRelationalModel();

        var target = after
            .GetService<IDesignTimeModel>()
            .Model
            .GetRelationalModel();

        // Act
        var forward = differ.GetDifferences(source, target);
        var backward = differ.GetDifferences(target, source);
        await ExecuteAsync(
            before,
            forward,
            after.GetService<IDesignTimeModel>()
                .Model,
            cancellationToken: TestContext.Current.CancellationToken);

        var rowsAfter = await ReadRowsAsync(
            after,
            registry.Name,
            cancellationToken: TestContext.Current.CancellationToken);

        var reuse = await Record.ExceptionAsync(() => after
            .NestedSet<ReportingNode>()
            .InsertRootAsync(
                new ReportingNode
                {
                    Id = 4,
                    Name = "Reuse",
                },
                retired,
                CancellationToken.None));

        await after
            .NestedSet<ReportingNode>()
            .InsertChildAsync(
                new ReportingNode
                {
                    Id = 5,
                    Name = "Later",
                },
                1L,
                CancellationToken.None);

        var rowsBeforeDown = await ReadRowsAsync(
            after,
            registry.Name,
            cancellationToken: TestContext.Current.CancellationToken);

        await ExecuteAsync(
            after,
            backward,
            before.GetService<IDesignTimeModel>()
                .Model,
            cancellationToken: TestContext.Current.CancellationToken);

        var rowsAfterDown = await ReadRowsAsync(
            before,
            registry.Name,
            cancellationToken: TestContext.Current.CancellationToken);

        var reuseAfterDown = await Record.ExceptionAsync(() => hierarchy.InsertRootAsync(
            new ReportingNode
            {
                Id = 6,
                Name = "Reuse",
            },
            retired,
            CancellationToken.None));

        await hierarchy.InsertChildAsync(
            new ReportingNode
            {
                Id = 7,
                Name = "Back",
            },
            1L,
            CancellationToken.None);

        var nodes = await hierarchy
            .InTree(active)
            .Nodes
            .Select(node => node.Id)
            .OrderBy(id => id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Contains(forward, operation => operation is RenameTableOperation);
        Assert.Contains(backward, operation => operation is RenameTableOperation);
        Assert.DoesNotContain(forward, operation => operation is CreateTableOperation or DropTableOperation);
        Assert.DoesNotContain(backward, operation => operation is CreateTableOperation or DropTableOperation);
        Assert.Equal(rowsBefore, rowsAfter);
        Assert.Contains(rowsBefore, row => row.Revision > 0);
        Assert.IsAssignableFrom<NestedSetException>(reuse);
        Assert.Equal(rowsBeforeDown, rowsAfterDown);
        Assert.IsAssignableFrom<NestedSetException>(reuseAfterDown);
        Assert.Equal(new long[] { 1, 2, 5, 7 }, nodes);
    }

    /// <summary>Applies operations through EF's ordinary provider generator.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task ExecuteAsync(
        DbContext context,
        IReadOnlyList<MigrationOperation> operations,
        IModel model,
        CancellationToken cancellationToken
    )
    {
        var connection = context.GetService<IRelationalConnection>();
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, model);

        foreach (var command in commands)
        {
            await command.ExecuteNonQueryAsync(connection, cancellationToken: cancellationToken);
        }
    }

    /// <summary>Reads identities and lifecycles through the mapped registry entity.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<Dictionary<Guid, byte>> ReadLifecyclesAsync(
        DbContext context,
        string registry,
        CancellationToken cancellationToken
    ) => (await ReadRowsAsync(
        context,
        registry,
        cancellationToken: cancellationToken)).ToDictionary(row => row.TreeId, row => row.Lifecycle);

    /// <summary>Reads each complete registry row in a stable order.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<RegistryRow[]> ReadRowsAsync(
        DbContext context,
        string registry,
        CancellationToken cancellationToken
    )
    {
        var rows = await context
            .Set<NestedSetTreeRegistry>(registry)
            .AsNoTracking()
            .Select(row => new RegistryRow(
                EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(cancellationToken);

        return rows
            .OrderBy(row => row.TreeId)
            .ToArray();
    }

    /// <summary>Proves the physical table exists under its delimited name, independent of the mapping.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<int> CountRowsAsync(
        DbContext context,
        string table,
        string? schema,
        CancellationToken cancellationToken
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM " + sql.DelimitIdentifier(table, schema);
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var count = await command.ExecuteScalarAsync(cancellationToken);

            return Convert.ToInt32(count, CultureInfo.InvariantCulture);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Compares registry rows by value across the rename.</summary>
    private sealed record RegistryRow(
        Guid TreeId,
        long Revision,
        byte Lifecycle
    );
}
