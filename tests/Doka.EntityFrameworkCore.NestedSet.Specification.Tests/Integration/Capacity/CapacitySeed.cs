namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Creates real persisted capacity fixtures without allocating one CLR entity per stored row.</summary>
internal static class CapacitySeed
{
    private const int NativeBatchSize = 100_000;

    /// <summary>Registers the tree through the public API before inserting bounded native adjacency batches.</summary>
    internal static async Task SeedAsync(
        TreeContext context,
        int count,
        bool deep = false,
        bool corrupt = false
    )
    {
        await context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        context.ChangeTracker.Clear();

        await context
            .Set<TreeNode>()
            .Where(node => node.NodeId == 1)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(node => node.Start, 1L)
                    .SetProperty(node => node.End, corrupt ? 2L : (long)count * 2)
                    .SetProperty(node => node.Depth, corrupt ? 9 : 0),
                CancellationToken.None);

        var entity = context.Model.FindEntityType(typeof(TreeNode))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var helper = context.GetService<ISqlGenerationHelper>();
        var table = helper.DelimitIdentifier(store.Name, store.Schema);
        var names = new[]
        {
            nameof(TreeNode.NodeId), nameof(TreeNode.TreeId), nameof(TreeNode.Tree), nameof(TreeNode.Parent),
            nameof(TreeNode.Start), nameof(TreeNode.End), nameof(TreeNode.Depth), nameof(TreeNode.Position),
        };
        var columns = string.Join(", ", names.Select(name =>
            helper.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(store)!)));
        var offset = helper.GenerateParameterName("offset");
        var limit = helper.GenerateParameterName("limit");
        var total = helper.GenerateParameterName("total");
        var tree = helper.GenerateParameterName("tree");
        var scope = helper.GenerateParameterName("scope");
        var parent = deep ? "(numbers.id - 1)" : "1";
        var left = corrupt ? "1" : deep ? "numbers.id" : "((2 * numbers.id) - 2)";
        var right = corrupt ? "2" : deep ? $"((2 * {total}) - numbers.id + 1)" : "((2 * numbers.id) - 1)";
        var depth = corrupt ? "9" : deep ? "(numbers.id - 1)" : "1";
        var position = deep ? "0" : "(numbers.id - 2)";
        var sql = $"INSERT INTO {table} ({columns}) "
            + $"SELECT numbers.id, {tree}, {scope}, {parent}, {left}, {right}, {depth}, {position} "
            + $"FROM ({FixtureRowNumbers.SelectSql(offset)}) numbers "
            + $"WHERE numbers.id <= {limit}";

        if (deep)
        {
            // WHY: Immediate self-reference checks need the preceding parent before each deeper child.
            sql += " ORDER BY numbers.id";
        }

        for (var first = 2; first <= count; first += NativeBatchSize)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            var parameters = new object[]
            {
                Parameter(nameof(TreeNode.NodeId), "offset", first),
                Parameter(nameof(TreeNode.NodeId), "limit", Math.Min(count, first + NativeBatchSize - 1)),
                Parameter(nameof(TreeNode.End), "total", (long)count),
                Parameter(nameof(TreeNode.TreeId), "tree", Guid.Empty),
                Parameter(nameof(TreeNode.Tree), "scope", 1),
            };

            // WHY: The mapped relational type supplies Guid conversion, including Doka's binary MySQL storage.
            await context.Database.ExecuteSqlRawAsync(sql, parameters, CancellationToken.None);

            DbParameter Parameter(
                string property,
                string name,
                object value
            ) => entity.FindProperty(property)!
                .GetRelationalTypeMapping()
                .CreateParameter(command, name, value, nullable: false);
        }
    }

    /// <summary>Counts missing, unexpected, and structurally mismatched rows in bounded key ranges.</summary>
    internal static async Task<long> MismatchesAsync(
        TreeContext context,
        int count,
        bool deep = false,
        bool corrupt = false
    )
    {
        var nodes = context
            .Set<TreeNode>()
            .AsNoTracking();

        var mismatches = await nodes.LongCountAsync(
            node => node.NodeId < 1 || node.NodeId > count, CancellationToken.None);

        // WHY: One coordinate aggregate over ten million rows can exceed the ordinary command timeout before
        // returning any result. Primary-key ranges bound that wait without retaining entities or raising timeouts.
        for (var first = 1L; first <= count; first += NativeBatchSize)
        {
            var firstKey = (int)first;
            var lastKey = (int)Math.Min(count, first + NativeBatchSize - 1);
            var range = nodes.Where(node => node.NodeId >= firstKey && node.NodeId <= lastKey);

            // WHY: Correct coordinates on surviving rows do not prove that every expected key still exists.
            mismatches += lastKey - firstKey + 1L - await range.LongCountAsync(CancellationToken.None);

            // WHY: Explicit root/child branches preserve null-parent checks without repeated SQL CASE compensation.
            mismatches += await range.LongCountAsync(
                node => node.Tree != 1
                    || node.TreeId != Guid.Empty
                    || (node.NodeId == 1 && node.Parent != null)
                    || (node.NodeId != 1 && (node.Parent == null || node.Parent != (deep ? node.NodeId - 1 : 1)))
                    || node.Start != (corrupt ? 1L : deep ? node.NodeId
                        : node.NodeId == 1 ? 1L : (2L * node.NodeId) - 2)
                    || node.End != (corrupt ? 2L : deep ? (2L * count) - node.NodeId + 1
                        : node.NodeId == 1 ? 2L * count : (2L * node.NodeId) - 1)
                    || node.Depth != (corrupt ? 9 : deep ? node.NodeId - 1 : node.NodeId == 1 ? 0 : 1)
                    || node.Position != (deep || node.NodeId == 1 ? 0L : node.NodeId - 2L),
                CancellationToken.None);
        }

        return mismatches;
    }

    /// <summary>Reads the complete mutable registry state independently of any operation context.</summary>
    internal static Task<CapacityRegistryState> RegistryAsync(
        TreeContext context
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(TreeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy).Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Where(row => EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope) == 1
                && EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == Guid.Empty)
            .Select(row => new CapacityRegistryState(
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .SingleAsync(CancellationToken.None);
    }

    /// <summary>Counts all registry rows to prove that a failed new-tree reservation was rolled back.</summary>
    internal static Task<int> RegistryCountAsync(
        TreeContext context
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(TreeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy).Registry;

        return context.Set<NestedSetTreeRegistry>(registry.Name).CountAsync(CancellationToken.None);
    }
}

/// <summary>Retains only the registry values that an atomic operation can mutate.</summary>
internal sealed record CapacityRegistryState(
    long Revision,
    byte Lifecycle
);
