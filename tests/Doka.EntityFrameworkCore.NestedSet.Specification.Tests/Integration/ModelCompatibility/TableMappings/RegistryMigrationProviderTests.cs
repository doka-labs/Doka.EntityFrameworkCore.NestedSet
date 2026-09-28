using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks data-preserving legacy registry renames on every supported relational provider.</summary>
public abstract class RegistryMigrationProviderTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Uses independently reset provider databases for each migration case.</summary>
    protected RegistryMigrationProviderTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>EF table renames retain active and tombstoned tree identities.</summary>
    [Fact]
    public async Task LegacyRegistryRenamePreservesLifecycleRows()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.Model.FindEntityType(typeof(OrderingNode))!;
        var registryName = (string)hierarchy.FindAnnotation(NestedSetAnnotationNames.TreeRegistryEntity)!.Value!;

        var registry = context.Model.FindEntityType(registryName)!;
        var newName = registry.GetTableName()!;
        var schema = registry.GetSchema();
        var legacyIdentity = hierarchy.Name + "\0" + schema + "\0" + hierarchy.GetTableName();
        var oldName = "DokaNestedSetTrees_"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyIdentity)))[..16];

        var activeTree = Guid.NewGuid();
        var retiredTree = Guid.NewGuid();

        foreach (var (treeId, lifecycle) in new[]
                 {
                     (activeTree, NestedSetTreeRegistryMetadata.Active),
                     (retiredTree, NestedSetTreeRegistryMetadata.Tombstoned),
                 })
        {
            var row = new NestedSetTreeRegistry();
            context
                .Set<NestedSetTreeRegistry>(registryName)
                .Add(row);

            context
                .Entry(row)
                .Property<int>(NestedSetTreeRegistryMetadata.Scope)
                .CurrentValue = 1;

            context
                .Entry(row)
                .Property<Guid>(NestedSetTreeRegistryMetadata.TreeId)
                .CurrentValue = treeId;

            context
                .Entry(row)
                .Property<long>(NestedSetTreeRegistryMetadata.Revision)
                .CurrentValue = 1;

            context
                .Entry(row)
                .Property<byte>(NestedSetTreeRegistryMetadata.Lifecycle)
                .CurrentValue = lifecycle;
        }

        await context.SaveChangesAsync(CancellationToken.None);
        await RenameAsync(context, newName, oldName, schema);

        // Act
        await RenameAsync(context, oldName, newName, schema);
        var rows = await context
            .Set<NestedSetTreeRegistry>(registryName)
            .Select(row => new
            {
                TreeId = EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                Lifecycle = EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle),
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.NotEqual(oldName, newName);
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row.TreeId == activeTree && row.Lifecycle == NestedSetTreeRegistryMetadata.Active);
        Assert.Contains(
            rows,
            row => row.TreeId == retiredTree && row.Lifecycle == NestedSetTreeRegistryMetadata.Tombstoned);
    }

    /// <summary>Executes the same EF RenameTable operation an application migration uses.</summary>
    private static async Task RenameAsync(
        DbContext context,
        string from,
        string to,
        string? schema
    )
    {
        var migration = new MigrationBuilder(context.Database.ProviderName!);

        migration.RenameTable(name: from, schema: schema, newName: to, newSchema: schema);
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(migration.Operations, context.Model);

        var connection = context.GetService<IRelationalConnection>();

        foreach (var command in commands)
        {
            await command.ExecuteNonQueryAsync(connection, cancellationToken: CancellationToken.None);
        }
    }
}
