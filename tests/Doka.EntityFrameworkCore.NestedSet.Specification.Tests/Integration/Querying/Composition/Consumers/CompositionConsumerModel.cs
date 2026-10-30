using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;
using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Adapts actual sample models to the already persisted capacity nodes without copying a hierarchy.</summary>
/// <remarks>
/// WHY: The consumers intentionally use different CLR keys. Reversible fixture conversions keep their real
/// builders under test while structural reads still seek the original integer primary key. These read-only
/// views qualify query scale, not application migrations, period-table indexes, or writes through a view.
/// </remarks>
internal static class CompositionConsumerModel
{
    /// <summary>Names the read-only signal projection over the shared structural table.</summary>
    internal const string SignalNodes = "CCL_Nodes";

    /// <summary>Names the exact-edge validity projection used only by capacity reads.</summary>
    internal const string SignalPeriods = "CCL_Periods";

    /// <summary>Names the read-only group projection over those same structural rows.</summary>
    internal const string GroupNodes = "CCG_Nodes";

    /// <summary>Gets the one authorized logical tenant, stored as the original scope integer.</summary>
    internal static readonly Guid Tenant = Identity(1);

    /// <summary>Gets the nonempty logical tree identity swapped with the fixture's physical empty GUID.</summary>
    internal static readonly Guid Tree = Identity(2);

    /// <summary>Gets the inclusive first day for both measurement and projected validity data.</summary>
    internal static readonly DateOnly FirstDay = new(2042, 3, 10);

    private static readonly CompositionConsumerIdentityConverter s_identity = new();

    private static readonly CompositionConsumerTreeConverter s_tree = new();

    /// <summary>Creates one allocation-free, reversible fixture identity in the documented integer range.</summary>
    internal static Guid Identity(int value)
        // WHY: Unbound catalog sentinels retain Guid.Empty <-> zero; real fixture keys use the
        // sample's independent synthetic namespace, including keys produced by its catalog seed.
        => value == 0 ? Guid.Empty : SignalApp.SignalReportDataset.Id(value);

    /// <summary>Rejects accidental arbitrary GUIDs instead of silently collapsing distinct application keys.</summary>
    internal static int Integer(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        var integer = BinaryPrimitives.ReadInt32LittleEndian(bytes);

        if (value != Identity(integer))
        {
            throw new ArgumentException(
                "Consumer capacity identities must use the reversible fixture range.", nameof(value));
        }

        return integer;
    }

    /// <summary>Swaps the two tree identities bijectively, retaining every other identity unchanged.</summary>
    internal static Guid SwapTree(Guid value) => value == Guid.Empty ? Tree : value == Tree ? Guid.Empty : value;

    /// <summary>Applies consistent physical types to catalog unions, foreign keys, and node joins.</summary>
    internal static void Configure(ModelBuilder model, bool signal)
    {
        foreach (var entity in model.Model.GetEntityTypes().ToArray())
        {
            var table = entity.GetTableName();

            if (table is not null)
            {
                entity.SetTableName((signal ? "CCL_" : "CCG_") + table);
            }

            foreach (var property in entity.GetDeclaredProperties())
            {
                if (property.ClrType != typeof(Guid) && property.ClrType != typeof(Guid?))
                {
                    continue;
                }

                var tree = property.Name is "TreeId" or "NestedSetTreeId";
                property.SetValueConverter(tree ? s_tree : s_identity);
            }
        }

        if (signal)
        {
            model.Entity<SignalApp.SignalNode>().ToTable(SignalNodes);
            model.Entity<SignalApp.LinkWindow>().ToTable(SignalPeriods);
        }
        else
        {
            model.Entity<GroupApp.UserGroup>().ToTable(GroupNodes);
            model.Entity<GroupApp.UserGroup>().Property(row => row.Id).ValueGeneratedNever();
            model.Entity<GroupApp.User>().Property(row => row.Id).ValueGeneratedNever();
        }
    }

    /// <summary>Creates only concrete sample payload tables; no structural rows are inserted or copied.</summary>
    internal static async Task CreatePayloadAsync(DbContext context, bool signal)
    {
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var excluded = signal ? new[] { SignalNodes, SignalPeriods } : new[] { GroupNodes };
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model)
            .Where(operation => operation switch
            {
                CreateTableOperation table => !excluded.Contains(table.Name, StringComparer.Ordinal),
                CreateIndexOperation index => !excluded.Contains(index.Table, StringComparer.Ordinal),
                _ => true,
            }).ToArray();

        foreach (var table in operations.OfType<CreateTableOperation>())
        {
            // WHY: SQL databases cannot reference a view with a foreign key. Normal payload-to-payload FKs remain;
            // the existing sample model tests separately qualify real node-table FKs and migration constraints.
            table.ForeignKeys.RemoveAll(key => excluded.Contains(key.PrincipalTable, StringComparer.Ordinal));
        }

        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(operations,
            context.GetService<IDesignTimeModel>().Model);

        foreach (var command in commands)
        {
            await context.Database.ExecuteSqlRawAsync(command.CommandText, CancellationToken.None);
        }
    }

    /// <summary>Exposes exact persisted coordinates with explicit, constant capacity-only business payload.</summary>
    internal static async Task CreateViewsAsync(TreeContext source, bool signal)
    {
        var entity = source.Model.FindEntityType(typeof(TreeNode))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = source.GetService<ISqlGenerationHelper>();
        var provider = source.Database.ProviderName;
        var boolean = provider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "TRUE"
            : provider == "Microsoft.EntityFrameworkCore.SqlServer" ? "CAST(1 AS bit)" : "1";

        var view = signal ? SignalNodes : GroupNodes;
        var projections = new[]
        {
            Column(nameof(TreeNode.NodeId), "Id"), Column(nameof(TreeNode.Tree), "TenantId"),
            Column(nameof(TreeNode.TreeId), signal ? SignalApp.SignalStorage.TreeId : "TreeId"),
            Column(nameof(TreeNode.Parent), signal ? SignalApp.SignalStorage.ParentId : "ParentId"),
            Column(nameof(TreeNode.Start), signal ? SignalApp.SignalStorage.Left : "Left"),
            Column(nameof(TreeNode.End), signal ? SignalApp.SignalStorage.Right : "Right"),
            Column(nameof(TreeNode.Depth), signal ? SignalApp.SignalStorage.Depth : "Depth"),
            Column(nameof(TreeNode.Position), signal ? SignalApp.SignalStorage.Position : "Position"),
            $"1 AS {sql.DelimitIdentifier("TypeId")}",
            $"'Capacity node' AS {sql.DelimitIdentifier("Name")}",
            $"{boolean} AS {sql.DelimitIdentifier(signal ? "IsVisible" : "Visible")}",
        };

        var table = sql.DelimitIdentifier(store.Name, store.Schema);
        var statement = $"CREATE VIEW {sql.DelimitIdentifier(view, store.Schema)} AS SELECT "
            + string.Join(", ", projections) + $" FROM {table}";

        await source.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);

        if (signal)
        {
            // WHY: A logical period per exact stored edge reuses the deep fixture instead of copying 100,000
            // edge rows. Child key 2 closes after the first day; expected pruning is derived from adjacency.
            var literal = "'" + FirstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'";
            var day = provider == "Microsoft.EntityFrameworkCore.Sqlite" ? literal : $"CAST({literal} AS date)";
            var child = entity.FindProperty(nameof(TreeNode.NodeId))!.GetColumnName(store)!;
            var end = $"CASE WHEN {sql.DelimitIdentifier(child)} = 2 THEN {day} ELSE NULL END";
            var periods = new[]
            {
                Column(nameof(TreeNode.Tree), "TenantId"), Column(nameof(TreeNode.Parent), "ParentId"),
                Column(nameof(TreeNode.NodeId), "ChildId"), $"{day} AS {sql.DelimitIdentifier("FromDate")}",
                $"{end} AS {sql.DelimitIdentifier("ToDate")}",
            };

            var parent = entity.FindProperty(nameof(TreeNode.Parent))!.GetColumnName(store)!;
            var periodStatement = $"CREATE VIEW {sql.DelimitIdentifier(SignalPeriods, store.Schema)} AS SELECT "
                + string.Join(", ", periods) + $" FROM {table} WHERE {sql.DelimitIdentifier(parent)} IS NOT NULL";

            await source.Database.ExecuteSqlRawAsync(periodStatement, CancellationToken.None);
        }

        string Column(string property, string alias) => sql.DelimitIdentifier(
            entity.FindProperty(property)!.GetColumnName(store)!) + " AS " + sql.DelimitIdentifier(alias);
    }
}

/// <summary>Runs the actual signal builders against shared capacity views and bounded payload tables.</summary>
internal sealed class CompositionCapacitySignalLabContext(DbContextOptions<CompositionCapacitySignalLabContext> options)
    : SignalApp.SignalLabContext(options)
{
    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // WHY: The real catalog UNION also projects unbound Guid.Empty literals. Their default scalar
        // mapping must match the adapted integer property mapping on every provider, including constants.
        configurationBuilder.DefaultTypeMapping<Guid>().HasConversion<CompositionConsumerIdentityConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsNpgsql())
        {
            modelBuilder.HasDefaultSchema("hierarchies");
        }

        base.OnModelCreating(modelBuilder);
        CompositionConsumerModel.Configure(modelBuilder, true);
    }
}

/// <summary>Shares the fixture's reversible scalar mapping between properties and unbound catalog literals.</summary>
internal sealed class CompositionConsumerIdentityConverter : ValueConverter<Guid, int>
{
    /// <summary>Creates EF's parameterless converter for the restricted fixture identity domain.</summary>
    public CompositionConsumerIdentityConverter()
        : base(value => CompositionConsumerModel.Integer(value), value => CompositionConsumerModel.Identity(value)) { }
}

/// <summary>Keeps converted tree identities consistent between properties and unbound status-union literals.</summary>
internal sealed class CompositionConsumerTreeConverter : ValueConverter<Guid, Guid>
{
    /// <summary>Creates the bijective swap used by this fixture's logical and physical tree identities.</summary>
    public CompositionConsumerTreeConverter()
        : base(value => CompositionConsumerModel.SwapTree(value), value => CompositionConsumerModel.SwapTree(value)) { }
}

/// <summary>Runs the actual supervisor builders over the same persisted capacity hierarchy.</summary>
internal sealed class CompositionCapacityGroupContext(DbContextOptions<CompositionCapacityGroupContext> options)
    : GroupApp.UserGroupContext(options)
{
    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // WHY: The real status UNION's unbound StartTreeId literals must retain the same reversible tree
        // mapping as its entity-property branches. Tenant/role properties retain their explicit integer codecs.
        configurationBuilder.DefaultTypeMapping<Guid>().HasConversion<CompositionConsumerTreeConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsNpgsql())
        {
            modelBuilder.HasDefaultSchema("hierarchies");
        }

        base.OnModelCreating(modelBuilder);
        CompositionConsumerModel.Configure(modelBuilder, false);
    }
}
