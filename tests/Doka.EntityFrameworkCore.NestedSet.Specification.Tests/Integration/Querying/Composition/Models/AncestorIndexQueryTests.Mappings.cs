namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    // WHY: The collection fixture retains tables across cases. Globally disjoint unscoped keys
    // prevent one mapping probe from changing the rows another case will later query.
    private static int s_mappingIdentity = 500_000;

    /// <summary>Opted-in mapped variants execute the indexed source strategy without CLR property or equality assumptions.</summary>
    /// <param name="shape">The finalized EF mapping whose exact alias must be retained.</param>
    /// <param name="includeSelf">Whether equal-bound leaf owners participate.</param>
    [Theory]
    [InlineData("shadow", false)]
    [InlineData("shadow", true)]
    [InlineData("converted", false)]
    [InlineData("converted", true)]
    [InlineData("binary", false)]
    [InlineData("binary", true)]
    [InlineData("alternate", false)]
    [InlineData("alternate", true)]
    [InlineData("fields", false)]
    [InlineData("fields", true)]
    [InlineData("named", false)]
    [InlineData("named", true)]
    [InlineData("tph", false)]
    [InlineData("tph", true)]
    [InlineData("tpc", false)]
    [InlineData("tpc", true)]
    [InlineData("table-split", false)]
    [InlineData("table-split", true)]
    [InlineData("entity-split", false)]
    [InlineData("entity-split", true)]
    public async Task IndexedAncestorsExecuteAgainstExactMappedRoles(string shape, bool includeSelf)
    {
        // Arrange
        var first = Interlocked.Add(ref s_mappingIdentity, 10);
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexMappedContext>(Engine,
            static options => new AncestorIndexMappedContext(options), probe);

        var query = shape switch
        {
            "shadow" => await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
                static _ => new CompositionShadowNode()),
            "converted" => await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
                static _ => new CompositionConvertedNode()),
            "binary" => await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
                static _ => new BinaryNode()),
            "alternate" => await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
                depth => new CompositeKeyNode { RowId = first + 100 + depth }),
            "fields" => await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
                static _ => new FieldNode()),
            "named" => await SeedNamedQueriesAsync(context, tree, scope, first, includeSelf),
            "tph" => await SeedMappedQueryAsync<InheritanceNode>(context, tree, scope, first, includeSelf,
                static depth => depth == 1 ? new TphMetricNode() : new TphFolderNode()),
            "tpc" => await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
                static _ => new TpcFolderNode()),
            "table-split" => await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
                static _ => new TableSplitNode()),
            "entity-split" => await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
                static _ => new EntitySplitNode()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        int[] expected = includeSelf ? [0, 1, 2] : [0, 1];
        probe.Reset();

        // Act
        var actual = await query.OrderBy(depth => depth).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>TPT rehydration keeps the actual base owner and separate derived payload aliases.</summary>
    /// <param name="includeSelf">Whether leaf owners participate.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedAncestorsExecuteAgainstTptBaseOwner(bool includeSelf)
    {
        // Arrange
        var first = Interlocked.Add(ref s_mappingIdentity, 10);
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexTptQueryContext>(Engine,
            static options => new AncestorIndexTptQueryContext(options), probe);

        var query = await SeedMappedQueryAsync<InheritanceNode>(context, tree, scope, first, includeSelf,
            static depth => depth == 1 ? new TptMetricNode() : new TptFolderNode());

        int[] expected = includeSelf ? [0, 1, 2] : [0, 1];
        probe.Reset();

        // Act
        var actual = await query.OrderBy(depth => depth).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Seeds both named maps with colliding identities so exact mapping provenance is observable.</summary>
    private static async Task<IQueryable<int>> SeedNamedQueriesAsync(
        DbContext context,
        Guid tree,
        Guid scope,
        int first,
        bool includeSelf
    )
    {
        await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
            static _ => new Dictionary<string, object?>(), AncestorIndexMappedContext.Second);

        // WHY: Equal keys, TreeId and coordinates challenge exact named mapping identity. Different
        // stored depths make accidentally reading the other physical table observable in the result.
        await context.Set<Dictionary<string, object?>>(AncestorIndexMappedContext.Second)
            .Where(node => EF.Property<Guid>(node, NestedSetPropertyRoles.TreeId) == tree)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                node => EF.Property<int>(node, NestedSetPropertyRoles.Depth),
                node => EF.Property<int>(node, NestedSetPropertyRoles.Depth) + 10), CancellationToken.None);

        return await SeedMappedQueryAsync(context, tree, scope, first, includeSelf,
            static _ => new Dictionary<string, object?>(), AncestorIndexMappedContext.First);
    }

    /// <summary>Assigns exact finalized mapped roles, including shadow and converter-backed values, before tracking.</summary>
    private static async Task<IQueryable<int>> SeedMappedQueryAsync<TEntity>(
        DbContext context,
        Guid tree,
        Guid scope,
        int first,
        bool includeSelf,
        Func<int, TEntity> create,
        string? name = null
    )
        where TEntity : class
    {
        var entityType = name is null ? context.Model.FindEntityType(typeof(TEntity))! : context.Model.FindEntityType(name)!;
        var descriptor = NestedSetModelMapping.For(context.Model).Descriptor(entityType);
        var set = name is null ? context.Set<TEntity>() : context.Set<TEntity>(name);

        for (var depth = 0; depth < 3; depth++)
        {
            var entity = create(depth);

            var entry = name is null ? context.Entry(entity) : set.Entry(entity);
            var key = MappedKey(descriptor.NodeKey.ClrType, first, depth);
            var parent = depth == 0 ? null : MappedKey(descriptor.NodeKey.ClrType, first, depth - 1);

            entry.Property(descriptor.NodeKey.Name).CurrentValue = key;
            entry.Property(descriptor.TreeId.Name).CurrentValue = tree;
            entry.Property(descriptor.Parent.Name).CurrentValue = parent;
            entry.Property(descriptor.Left.Name).CurrentValue = depth + 1L;
            entry.Property(descriptor.Right.Name).CurrentValue = 6L - depth;
            entry.Property(descriptor.Depth.Name).CurrentValue = depth;
            entry.Property(descriptor.Position.Name).CurrentValue = 0L;

            if (entityType.FindProperty("Name") is { ClrType: var nameType } && nameType == typeof(string))
            {
                entry.Property("Name").CurrentValue = $"mapped-{depth}";
            }

            if (descriptor.Scope is { } scopeProperty)
            {
                entry.Property(scopeProperty.Name).CurrentValue = scopeProperty.ClrType == typeof(Guid) ? scope : first;
            }

            await set.AddAsync(entity, CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var hierarchy = name is null ? context.NestedSet<TEntity>() : context.NestedSet<TEntity>(name);

        var depthName = descriptor.Depth.Name;
        var bound = descriptor.Scope is { } configuredScope
            ? configuredScope.ClrType == typeof(Guid)
                ? hierarchy.ForScope(scope).InTree(tree)
                : hierarchy.ForScope(first).InTree(tree)
            : hierarchy.InTree(tree);

        return includeSelf
            ? bound.SelectAncestorsOrSelf(node => EF.Property<int>(node, depthName) == 2,
                (source, owner, distance) => EF.Property<int>(owner, depthName))
            : bound.SelectAncestors(node => EF.Property<int>(node, depthName) == 2,
                (source, owner, distance) => EF.Property<int>(owner, depthName));
    }

    /// <summary>Creates independent but database-equal parent/principal arrays and an actual converted key zero.</summary>
    private static object MappedKey(Type type, int first, int depth)
        => type == typeof(CompositionNodeKey) ? new CompositionNodeKey(depth)
            : type == typeof(byte[]) ? BitConverter.GetBytes(first + depth) : first + depth;
}
