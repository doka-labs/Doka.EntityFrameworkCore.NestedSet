namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares test-free ordering arrangements and exact finalized cache-context identity.</summary>
internal static class OrderingRefreshTestSupport
{
    /// <summary>Bounds fixture write work per command without changing provider command timeouts.</summary>
    internal const int SeedBatchSize = 10_000;

    /// <summary>Identifies native setup writes independently of the measured hierarchy operations.</summary>
    internal const string SeedCommandTag = "/* NestedSet ordering fixture seed */";

    /// <summary>
    ///     Retains the fixture's strict model and provider options inside the experiment's owned cache graph.
    /// </summary>
    /// <param name="template">The exact finalized ordering model and provider options.</param>
    /// <param name="services">The independently owned cache and provider service graph.</param>
    /// <param name="interceptors">The observers installed only for this context.</param>
    /// <returns>A strict context retaining the original model and shared experimental cache.</returns>
    internal static StrictOrderingContext CreateCacheContext(
        OrderingContext template,
        IServiceProvider services,
        params IInterceptor[] interceptors
    )
    {
        var extensions = template
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings()
            .UseModel(template.Model)
            .UseInternalServiceProvider(services)
            .AddInterceptors(interceptors)
            .Options;

        // WHY: Warm and measured contexts must keep the same finalized model even if the fixture cache evicts it.

        return new StrictOrderingContext(options);
    }

    /// <summary>
    ///     Seeds valid ordered structure directly so setup does not measure repeated hierarchy mutations.
    /// </summary>
    /// <param name="fixture">The independently owned ordering database.</param>
    /// <param name="engine">The concrete suite's immutable provider engine.</param>
    /// <param name="nodes">The measured child cardinality excluding its enclosing root.</param>
    /// <param name="interceptors">Observers for verifying setup command bounds and failure rollback.</param>
    /// <returns>A task completing after atomic root registration and bounded native child inserts.</returns>
    internal static async Task SeedAsync(
        OrderingFixture fixture,
        string engine,
        int nodes,
        params IInterceptor[] interceptors
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodes);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await fixture.ResetAsync(engine);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings()
            .UseModel(setup.Model)
            .AddInterceptors(interceptors)
            .Options;

        // WHY: An untracked enclosing root preserves every measured child and the original refresh cardinalities.
        await using var seed = new DbContext(options);
        var treeId = Guid.NewGuid();
        var root = new OrderingNode
        {
            Id = 0,
            Scope = 1,
            TreeId = treeId,
            Name = "Root",
            Left = 1,
            Right = (nodes + 1L) * 2,
        };

        await NestedSetTransaction.ExecuteAsync(
            seed,
            async token =>
            {
                // WHY: Only the root needs EF tracking to reserve the tree through the established fixture guard.
                // Tracking the whole forest adds entities, snapshots and thousands of EF batches before refresh.
                await seed.AddAsync(root, token);
                await seed.SavePrecomputedHierarchyAsync(token);
                seed.ChangeTracker.Clear();
                await InsertChildrenAsync(seed, engine, nodes, root, token);
            },
            cancellationToken);
    }

    /// <summary>
    ///     Writes precomputed fixture children without materialization or per-row EF modification commands.
    /// </summary>
    private static async Task InsertChildrenAsync(
        DbContext context,
        string engine,
        int nodes,
        OrderingNode root,
        CancellationToken cancellationToken
    )
    {
        var entity = context.Model.FindEntityType(typeof(OrderingNode))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var helper = context.GetService<ISqlGenerationHelper>();
        var table = helper.DelimitIdentifier(store.Name, store.Schema);
        var names = new[]
        {
            nameof(OrderingNode.Id), nameof(OrderingNode.Scope), nameof(OrderingNode.TreeId),
            nameof(OrderingNode.ParentId), nameof(OrderingNode.Name), nameof(OrderingNode.Priority),
            nameof(OrderingNode.Payload), nameof(OrderingNode.Category), nameof(OrderingNode.Left),
            nameof(OrderingNode.Right), nameof(OrderingNode.Depth), nameof(OrderingNode.Position),
        };

        var columns = string.Join(", ", names.Select(name =>
            helper.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(store)!)));

        var offset = helper.GenerateParameterName("offset");
        var limit = helper.GenerateParameterName("limit");
        var tree = helper.GenerateParameterName("tree");
        var scope = helper.GenerateParameterName("scope");
        var prefix = helper.GenerateParameterName("prefix");
        var payload = helper.GenerateParameterName("payload");
        var category = helper.GenerateParameterName("category");
        var name = ChildNameSql(engine, prefix);
        var coordinate = engine is "MySql" or "MariaDb"
            ? "CAST(numbers.id AS SIGNED)"
            : "CAST(numbers.id AS bigint)";

        var sql = $"{SeedCommandTag}\nINSERT INTO {table} ({columns}) "
            + $"SELECT numbers.id, {scope}, {tree}, 0, {name}, 0, {payload}, {category}, "
            + $"(2 * {coordinate}), (2 * {coordinate}) + 1, 1, numbers.id - 1 "
            + $"FROM ({FixtureRowNumbers.SelectSql(offset)}) numbers WHERE numbers.id <= {limit}";

        // WHY: The root, registry and all batches share the outer transaction; a late fixture failure leaves no
        // partially populated tree. Batch size bounds each command's work while retaining every requested row.
        for (long first = 1; first <= nodes; first += SeedBatchSize)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            var parameters = new object[]
            {
                Parameter(nameof(OrderingNode.Id), "offset", checked((int)first)),
                Parameter(nameof(OrderingNode.Id), "limit", checked((int)Math.Min(nodes, first + SeedBatchSize - 1))),
                Parameter(nameof(OrderingNode.TreeId), "tree", root.TreeId),
                Parameter(nameof(OrderingNode.Scope), "scope", root.Scope),
                Parameter(nameof(OrderingNode.Name), "prefix", "node-"),
                Parameter(nameof(OrderingNode.Payload), "payload", root.Payload),
                Parameter(nameof(OrderingNode.Category), "category", root.Category),
            };

            // WHY: The actual property mappings preserve enum conversion and Doka's binary Guid representation.
            await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);

            DbParameter Parameter(
                string property,
                string parameterName,
                object value
            ) => entity.FindProperty(property)!
                .GetRelationalTypeMapping()
                .CreateParameter(command, parameterName, value, nullable: false);
        }
    }

    /// <summary>Retains invariant D8 child names using each provider's native string operations.</summary>
    private static string ChildNameSql(
        string engine,
        string prefix
    )
    {
        // WHY: Padding belongs in the fixture SELECT so setup never allocates one CLR name per stored child.
        // D8 is a minimum width; large IDs must retain all digits instead of being truncated by RIGHT or LPAD.

        return engine switch
        {
            "SqlServer" => $"{prefix} + CASE WHEN numbers.id < 100000000 "
                + "THEN RIGHT('00000000' + CAST(numbers.id AS varchar(11)), 8) "
                + "ELSE CAST(numbers.id AS varchar(11)) END",
            "PostgreSql" => $"{prefix} || CASE WHEN numbers.id < 100000000 "
                + "THEN LPAD(CAST(numbers.id AS text), 8, '0') ELSE CAST(numbers.id AS text) END",
            "MySql" or "MariaDb" => $"CONCAT({prefix}, CASE WHEN numbers.id < 100000000 "
                + "THEN LPAD(CAST(numbers.id AS CHAR), 8, '0') ELSE CAST(numbers.id AS CHAR) END)",
            "Sqlite" => $"{prefix} || printf('%08d', numbers.id)",
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };
    }
}
