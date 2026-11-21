using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public sealed partial class HierarchyCompositionWorkTests
{
    /// <summary>Prepares the same independent node and payload fixture for both capability contracts.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="database">The isolated database used by the scenario.</param>
    private async Task PreparePairsAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, Nodes, cancellationToken: cancellationToken, deep: false, corrupt: false);
        await SeedPayloadAsync(setup, cancellationToken: cancellationToken);
        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(
            setup,
            Engine,
            cancellationToken: cancellationToken);

        await QueryPlanTestSupport.RefreshStatisticsAsync<UnrelatedRow>(
            setup,
            Engine,
            cancellationToken: cancellationToken);
    }

    /// <summary>Copies the fixture's existing node keys into payload with no second tracked population.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static async Task SeedPayloadAsync(
        TreeContext context,
        CancellationToken cancellationToken
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var source = context.Model.FindEntityType(typeof(TreeNode))!;
        var target = context.Model.FindEntityType(typeof(UnrelatedRow))!;
        var sourceStore = StoreObjectIdentifier.Table(source.GetTableName()!, source.GetSchema());
        var targetStore = StoreObjectIdentifier.Table(target.GetTableName()!, target.GetSchema());
        var sourceKey =
            sql.DelimitIdentifier(source.FindProperty(nameof(TreeNode.NodeId))!.GetColumnName(sourceStore)!);

        var targetKey =
            sql.DelimitIdentifier(target.FindProperty(nameof(UnrelatedRow.Id))!.GetColumnName(targetStore)!);

        var value = sql.DelimitIdentifier(target.FindProperty(nameof(UnrelatedRow.Value))!.GetColumnName(targetStore)!);

        var statement = $"INSERT INTO {sql.DelimitIdentifier(targetStore.Name, targetStore.Schema)} "
            + $"({targetKey}, {value}) SELECT {sourceKey}, 'fact' "
            + $"FROM {sql.DelimitIdentifier(sourceStore.Name, sourceStore.Schema)} "
            + $"WHERE {sourceKey} <= {Nodes.ToString(CultureInfo.InvariantCulture)}";

        // WHY: Only fixed fixture cardinality is interpolated; native mapping supplies every delimited identifier.
        await context.Database.ExecuteSqlRawAsync(statement, cancellationToken);
    }
}
