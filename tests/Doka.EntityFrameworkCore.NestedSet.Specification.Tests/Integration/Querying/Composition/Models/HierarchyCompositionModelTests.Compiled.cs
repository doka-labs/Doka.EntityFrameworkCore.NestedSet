namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>
    /// Generated runtime models preserve principal collation across differently collated parent aliases.
    /// </summary>
    /// <param name="operation">The individual public query contract or rejection case.</param>
    [Theory]
    [InlineData(HierarchyCompositionOperation.Ancestors)]
    [InlineData(HierarchyCompositionOperation.AncestorsOrSelf)]
    [InlineData(HierarchyCompositionOperation.Subtrees)]
    [InlineData(HierarchyCompositionOperation.AllowedPath)]
    [InlineData(HierarchyCompositionOperation.AnyAncestor)]
    [InlineData(HierarchyCompositionOperation.NearestAncestor)]
    [InlineData(HierarchyCompositionOperation.MissingNearestOwner)]
    [InlineData(HierarchyCompositionOperation.NearestOwnerRejectsValue)]
    [InlineData(HierarchyCompositionOperation.BlockedPath)]
    public async Task GeneratedModelUsesCapturedPrincipalCollationForParentAliases(
        HierarchyCompositionOperation operation
    )
    {
        // Arrange
        var scope = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var treeId = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var design = await _fixture.CreateContextAsync<CompiledTreeContext>(
            Engine,
            static options => new CompiledTreeContext(options, false),
            probe,
            TestMaterializationObserver.Instance);

        var ids = new[] { scope + "-ROOT", scope + "-MIDDLE", scope + "-LEAF" };

        await design.AddAsync(new CompiledScope { Id = scope }, CancellationToken.None);
        await design.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new CompiledFolder
        {
            Id = ids[depth],
            Scope = scope,
            ParentId = depth == 0 ? null : ids[depth - 1].ToLowerInvariant(),
            TreeId = treeId,
            Left = depth + 1,
            Right = 6 - depth,
            Depth = depth,
            Name = $"Node {depth}",
        }), CancellationToken.None);

        await design.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var model = CompiledCompositionModel(Engine);
        var extensions = design.GetService<IDbContextOptions>().Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder<CompiledTreeContext>(
                new DbContextOptions<CompiledTreeContext>(extensions))
            .UseModel(model)
            .Options;

        // WHY: The guarded constructor throws on OnModelCreating; a passing query must consume generated metadata.
        await using var context = new CompiledTreeContext(options, true);
        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        probe.Reset();
        var hierarchy = context.NestedSet<CompiledFolder>().ForScope(scope.ToUpperInvariant()).InTree(treeId);

        // Act
        var actual = await QueryDepths(hierarchy, operation)
            .OrderBy(depth => depth)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertDepths(operation, actual, probe, context, materialized);
        Assert.Same(model, context.Model);
    }

    /// <summary>
    /// Selects existing checked-in generator output for the actual engine without regenerating fixtures.
    /// </summary>
    private static IModel CompiledCompositionModel(
        string engine
    ) => engine switch
    {
        "Sqlite" => CompiledModels.CompiledTreeContextModel.Instance,
        "MySql" => CompiledModels.MySql.CompiledMySqlContextModel.Instance,
        "MariaDb" => CompiledModels.MariaDb.CompiledMariaDbContextModel.Instance,
        "PostgreSql" => CompiledModels.PostgreSql.CompiledPostgreSqlContextModel.Instance,
        "SqlServer" => CompiledModels.SqlServer.CompiledSqlServerContextModel.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
}
