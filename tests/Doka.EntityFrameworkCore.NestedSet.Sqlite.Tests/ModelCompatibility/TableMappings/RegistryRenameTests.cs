using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Guards registry persistence when an application entity changes its CLR identity.</summary>
public sealed class RegistryRenameTests : ProviderTest, IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses immutable SQLite ownership while each test owns its local resources.</summary>
    /// <param name="fixture">The provider fixture identifying this suite's SQLite engine.</param>
    public RegistryRenameTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>Equivalent physical mappings retain one registry table across a CLR type rename.</summary>
    [Fact]
    public void ClrRenamePreservesPhysicalRegistryName()
    {
        // Arrange
        var beforeOptions = new DbContextOptionsBuilder<RegistryRenameContext<PreviousFolder>>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        var afterOptions = new DbContextOptionsBuilder<RegistryRenameContext<RenamedFolder>>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        using var before = new RegistryRenameContext<PreviousFolder>(beforeOptions);
        using var after = new RegistryRenameContext<RenamedFolder>(afterOptions);

        // Act
        var originalRegistry = before
            .GetService<IDesignTimeModel>()
            .Model
            .GetEntityTypes()
            .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

        var renamedRegistry = after
            .GetService<IDesignTimeModel>()
            .Model
            .GetEntityTypes()
            .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

        // Assert
        Assert.NotEqual(originalRegistry.Name, renamedRegistry.Name);
        Assert.Equal(originalRegistry.GetTableName(), renamedRegistry.GetTableName());
        Assert.Equal(originalRegistry.GetSchema(), renamedRegistry.GetSchema());
    }

    /// <summary>Changing the CLR entity keeps both active and tombstoned registry rows readable.</summary>
    [Fact]
    public async Task ClrRenameRetainsRegistryLifecycleRows()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var beforeOptions = new DbContextOptionsBuilder<RegistryRenameContext<PreviousFolder>>()
            .ConfigureTestWarnings()
            .UseSqlite(connection)
            .UseNestedSets()
            .Options;

        var afterOptions = new DbContextOptionsBuilder<RegistryRenameContext<RenamedFolder>>()
            .ConfigureTestWarnings()
            .UseSqlite(connection)
            .UseNestedSets()
            .Options;

        var activeTree = Guid.NewGuid();
        var retiredTree = Guid.NewGuid();

        await using (var before = new RegistryRenameContext<PreviousFolder>(beforeOptions))
        {
            await before.Database.EnsureCreatedAsync(CancellationToken.None);
            var registry = before
                .Model
                .GetEntityTypes()
                .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

            AddLifecycleRows(before, registry.Name, activeTree, retiredTree);
            await before.SaveChangesAsync(CancellationToken.None);
        }

        // Act
        await using var after = new RegistryRenameContext<RenamedFolder>(afterOptions);
        var renamedRegistry = after
            .Model
            .GetEntityTypes()
            .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

        var rows = await after
            .Set<NestedSetTreeRegistry>(renamedRegistry.Name)
            .Select(row => new
            {
                TreeId = EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                Lifecycle = EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle),
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row.TreeId == activeTree && row.Lifecycle == NestedSetTreeRegistryMetadata.Active);
        Assert.Contains(
            rows,
            row => row.TreeId == retiredTree && row.Lifecycle == NestedSetTreeRegistryMetadata.Tombstoned);
    }

    /// <summary>An EF table rename preserves active rows and tombstones from the earlier hash rule.</summary>
    [Fact]
    public async Task LegacyRegistryNameMigratesWithoutLosingLifecycleRows()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var beforeOptions = new DbContextOptionsBuilder<RegistryRenameContext<PreviousFolder>>()
            .ConfigureTestWarnings()
            .UseSqlite(connection)
            .UseNestedSets()
            .Options;

        var afterOptions = new DbContextOptionsBuilder<RegistryRenameContext<RenamedFolder>>()
            .ConfigureTestWarnings()
            .UseSqlite(connection)
            .UseNestedSets()
            .Options;

        var activeTree = Guid.NewGuid();
        var retiredTree = Guid.NewGuid();
        string oldName;
        string newName;

        await using (var before = new RegistryRenameContext<PreviousFolder>(beforeOptions))
        {
            await before.Database.EnsureCreatedAsync(CancellationToken.None);
            var registry = before
                .Model
                .GetEntityTypes()
                .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

            var hierarchy = before.Model.FindEntityType(typeof(PreviousFolder))!;
            newName = registry.GetTableName()!;
            var oldIdentity = hierarchy.Name + "\0\0Documents";
            oldName = "DokaNestedSetTrees_"
                + Convert
                    .ToHexString(
                        SHA256.HashData(
                            Encoding.UTF8.GetBytes(oldIdentity)))[..16];

            AddLifecycleRows(before, registry.Name, activeTree, retiredTree);
            await before.SaveChangesAsync(CancellationToken.None);
            var sql = before.GetService<ISqlGenerationHelper>();

            // WHY: Recreate an existing database made with the original CLR-name hash before applying the
            // documented data-preserving RenameTable upgrade.
            await using var renameToLegacy = before
                .Database
                .GetDbConnection()
                .CreateCommand();

            renameToLegacy.CommandText = "ALTER TABLE "
                + sql.DelimitIdentifier(newName)
                + " RENAME TO "
                + sql.DelimitIdentifier(oldName);

            await renameToLegacy.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // Act
        await using var after = new RegistryRenameContext<RenamedFolder>(afterOptions);
        var migration = new MigrationBuilder(after.Database.ProviderName!);

        migration.RenameTable(name: oldName, newName: newName);
        var commands = after
            .GetService<IMigrationsSqlGenerator>()
            .Generate(migration.Operations, after.Model);

        var connectionService = after.GetService<IRelationalConnection>();

        foreach (var command in commands)
        {
            await command.ExecuteNonQueryAsync(connectionService, cancellationToken: CancellationToken.None);
        }

        var renamedRegistry = after
            .Model
            .GetEntityTypes()
            .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

        var rows = await after
            .Set<NestedSetTreeRegistry>(renamedRegistry.Name)
            .Select(row => new
            {
                TreeId = EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                Lifecycle = EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle),
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.NotEqual(oldName, newName);
        Assert.Equal(newName, renamedRegistry.GetTableName());
        Assert.Contains(rows, row => row.TreeId == activeTree && row.Lifecycle == NestedSetTreeRegistryMetadata.Active);
        Assert.Contains(
            rows,
            row => row.TreeId == retiredTree && row.Lifecycle == NestedSetTreeRegistryMetadata.Tombstoned);
    }

    /// <summary>Creates representative active and reserved registry identities without domain payload.</summary>
    private static void AddLifecycleRows(
        DbContext context,
        string registryName,
        Guid activeTree,
        Guid retiredTree
    )
    {
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
    }

    /// <summary>Maps two independently named CLR types to the same physical hierarchy table.</summary>
    public sealed class RegistryRenameContext<TNode>(DbContextOptions<RegistryRenameContext<TNode>> options)
        : DbContext(options)
        where TNode : RegistryRenameNode
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<TNode>()
                .ToTable("Documents")
                .HasKey(node => node.Id);
            modelBuilder
                .Entity<TNode>()
                .HasNestedSet(builder => builder
                    .HasNodeKey(node => node.Id)
                    .HasTreeId(node => node.TreeId)
                    .HasParent(node => node.ParentId)
                    .HasBounds(node => node.Left, node => node.Right)
                    .HasDepth(node => node.Depth)
                    .HasPosition(node => node.Position));
        }
    }

    /// <summary>Provides identical physical properties for both CLR model versions.</summary>
    public abstract class RegistryRenameNode
    {
        /// <summary>Gets or sets the node identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the number of ancestors.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Represents the original CLR entity name.</summary>
    public sealed class PreviousFolder : RegistryRenameNode;

    /// <summary>Represents the renamed CLR entity with identical physical columns.</summary>
    public sealed class RenamedFolder : RegistryRenameNode;
}
