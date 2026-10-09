namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects principal-key equality across custom and built-in text converters.</summary>
[Collection("Model compatibility")]
public abstract class ConvertedTextCollationTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares the provider's existing owner of independent compatibility tables.</summary>
    /// <param name="fixture">The fixture bound to this suite's exact engine.</param>
    protected ConvertedTextCollationTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Combines every converter shape with each query, mutation, maintenance, and failure probe.</summary>
    /// <returns>Provider-neutral cases inherited by every supported engine.</returns>
    public static IEnumerable<TheoryDataRow<int, string>> Cases()
    {
        foreach (var model in Enumerable.Range(0, 4))
        {
            foreach (var operation in new[]
                     {
                         "query",
                         "projection",
                         "delete",
                         "move",
                         "maintenance",
                         "managed",
                         "missing",
                         "cycle",
                         "rollback",
                     })
            {
                yield return new TheoryDataRow<int, string>(model, operation);
            }
        }
    }

    /// <summary>Case aliases use the principal's collation even when the parent's text column is binary.</summary>
    /// <param name="model">The custom reference, custom value, explicit built-in, or implicit conversion shape.</param>
    /// <param name="operation">The public behavior or atomic failure boundary to verify.</param>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ConvertedKeysUsePrincipalCollation(
        int model,
        string operation
    )
    {
        switch (model)
        {
            case 0:
                await RunAsync<ReferenceTextCollationContext, ReferenceTextKey, ReferenceTextKey?>(
                    options => new ReferenceTextCollationContext(options),
                    value => new ReferenceTextKey(value),
                    value => new ReferenceTextKey(value),
                    operation);
                break;
            case 1:
                await RunAsync<ValueTextCollationContext, ValueTextKey, ValueTextKey?>(
                    options => new ValueTextCollationContext(options),
                    value => new ValueTextKey(value),
                    value => new ValueTextKey(value),
                    operation);
                break;
            case 2:
                await RunAsync<EnumTextCollationContext, EnumTextKey, EnumTextKey?>(
                    options => new EnumTextCollationContext(options),
                    value => Enum.Parse<EnumTextKey>(value, true),
                    value => Enum.Parse<EnumTextKey>(value, true),
                    operation);
                break;
            case 3:
                await RunAsync<ImplicitEnumTextCollationContext, EnumTextKey, EnumTextKey?>(
                    options => new ImplicitEnumTextCollationContext(options),
                    value => Enum.Parse<EnumTextKey>(value, true),
                    value => Enum.Parse<EnumTextKey>(value, true),
                    operation);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(model));
        }
    }

    /// <summary>Registry DDL and supplied runtime models retain the same implicit conversion and collation.</summary>
    [DatabaseIndependent]
    [Fact]
    public void ImplicitIdentityCaptureMatchesRegistryAndSuppliedModel()
    {
        // Arrange
        var connection = Engine == "Sqlite"
            ? "Data Source=:memory:"
            : "Server=localhost;Database=model_compatibility;User ID=unused;Password=unused";

        var options = ModelCompatibilityDatabase.Options<ImplicitRegistryTextCollationContext>(Engine, connection);
        using var source = new ImplicitRegistryTextCollationContext(options);
        var model = source.Model;
        var designModel = source.GetService<IDesignTimeModel>().Model;
        var node = model.FindEntityType(typeof(ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>))!;
        var designNode = designModel.FindEntityType(node.Name)!;
        var store = StoreObjectIdentifier.Table(designNode.GetTableName()!, designNode.GetSchema());
        var registryName = (string)node.FindAnnotation(NestedSetAnnotationNames.TreeRegistryEntity)!.Value!;
        var registry = designModel.FindEntityType(registryName)!;
        var provider = source.GetService<IDatabaseProvider>().Name;
        var typeMappings = source.GetService<ITypeMappingSource>();

        // Act
        using var supplied = new ImplicitRegistryTextCollationContext(
            new DbContextOptionsBuilder<ImplicitRegistryTextCollationContext>(options).UseModel(model).Options);

        var suppliedNode = supplied.Model.FindEntityType(node.Name)!;
        var parentSql = supplied
            .NestedSet<ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>>()
            .ForScope(EnumTextKey.Root)
            .ParentOf(EnumTextKey.Child)
            .ToQueryString();

        // Assert
        Assert.Same(model, supplied.Model);
        foreach (var name in new[] { "Id", "ParentId", "Scope", "NativeTree" })
        {
            var expected = NestedSetCollations.ResolveEffective(
                designNode,
                designNode.FindProperty(name)!,
                store,
                provider,
                typeMappings);
            var captured = NestedSetCollations.Resolve(source, node.FindProperty(name)!, node);
            Assert.Equal(expected, captured);
            Assert.Equal(
                expected,
                NestedSetCollations.Resolve(supplied, suppliedNode.FindProperty(name)!, suppliedNode));

            if (name is "Scope" or "NativeTree")
            {
                var property = registry.FindProperty(name == "Scope" ? "Scope" : "TreeId")!;
                Assert.Equal(expected, property.GetCollation());
                Assert.Equal(
                    node.FindProperty(name)!.GetTypeMapping()
                        .Converter!.ProviderClrType,
                    property.GetTypeMapping()
                        .Converter!.ProviderClrType);
            }
        }

        var providerType = node.FindProperty("Id")!.GetTypeMapping()
            .Converter!.ProviderClrType;
        if (providerType == typeof(string))
        {
            Assert.Contains(
                NestedSetCollations.Resolve(source, node.FindProperty("Id")!, node)!,
                parentSql,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(typeof(int), providerType);
            Assert.True(source.Database.IsSqlite() || provider == NestedSetProviderCapabilities.MySqlProviderName);
            Assert.DoesNotContain(" COLLATE ", parentSql, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Required nullable keys keep their converter through canonical parent joins and repair.</summary>
    [Fact]
    public async Task RequiredNullableConvertedKeySupportsAliasMaintenance()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<NullableEnumTextCollationContext>(
            Engine,
            options => new NullableEnumTextCollationContext(options));

        await context
            .Set<ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        var hierarchy = context.NestedSet<ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertForestAsync(
            [
                new NestedSetTreeImport<ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>, Guid>(
                    treeId,
                    new NestedSetBranch<ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>>(
                        new ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>
                        {
                            Id = EnumTextKey.Root,
                            Name = "Root",
                        },
                        [
                            new NestedSetBranch<ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>>(
                                new ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>
                                {
                                    Id = EnumTextKey.Child,
                                    Name = "Child",
                                }),
                        ])),
            ],
            CancellationToken.None);

        await LowerParentsAsync<EnumTextKey?, EnumTextKey?>(context);
        context.ChangeTracker.Clear();

        // Act
        await hierarchy
            .InTree(treeId)
            .RebuildAsync(CancellationToken.None);

        // Assert
        var report = await hierarchy
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        Assert.True(report.IsValid);
        var stored = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);
        Assert.Equal(2, stored.Length);
        Assert.Equal(
            EnumTextKey.Root,
            Assert.Single(stored, node => node.Id == EnumTextKey.Child)
                .ParentId);
        var property = context.Model.FindEntityType(typeof(ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>))!
            .FindProperty(nameof(ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>.Id))!;
        Assert.Equal(typeof(EnumTextKey?), property.ClrType);
        Assert.False(property.IsNullable);
        Assert.Equal(
            typeof(string),
            property.GetTypeMapping()
                .Converter!.ProviderClrType);
    }

    /// <summary>Seeds one aliased adjacency tree and executes one independent acceptance probe.</summary>
    private async Task RunAsync<TContext, TKey, TParent>(
        Func<DbContextOptions<TContext>, TContext> create,
        Func<string, TKey> key,
        Func<string, TParent> parent,
        string operation
    )
        where TContext : DbContext
        where TKey : notnull
    {
        await using var context = await _fixture.CreateContextAsync(Engine, create);
        await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        var hierarchy = context.NestedSet<ConvertedTextCollationNode<TKey, TParent>>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertForestAsync(
            [
                new NestedSetTreeImport<ConvertedTextCollationNode<TKey, TParent>, Guid>(
                    treeId,
                    new NestedSetBranch<ConvertedTextCollationNode<TKey, TParent>>(
                        new ConvertedTextCollationNode<TKey, TParent>
                        {
                            Id = key("ROOT"),
                            Name = "Root",
                        },
                        [
                            new NestedSetBranch<ConvertedTextCollationNode<TKey, TParent>>(
                                new ConvertedTextCollationNode<TKey, TParent>
                                {
                                    Id = key("CHILD"),
                                    Name = "A",
                                },
                                [
                                    new NestedSetBranch<ConvertedTextCollationNode<TKey, TParent>>(
                                        new ConvertedTextCollationNode<TKey, TParent>
                                        {
                                            Id = key("LEAF"),
                                            Name = "Leaf",
                                        }),
                                ]),
                            new NestedSetBranch<ConvertedTextCollationNode<TKey, TParent>>(
                                new ConvertedTextCollationNode<TKey, TParent>
                                {
                                    Id = key("OTHER"),
                                    Name = "B",
                                }),
                        ])),
            ],
            CancellationToken.None);

        await LowerParentsAsync<TKey, TParent>(context);
        context.ChangeTracker.Clear();

        switch (operation)
        {
            case "query":
                await QueryAsync(context, hierarchy, key, treeId);
                break;
            case "projection":
                await ProjectAsync<TKey, TParent>(context, key, treeId);
                break;
            case "delete":
                await DeleteAsync(hierarchy, key, treeId);
                break;
            case "move":
                await MoveAsync(hierarchy, key, treeId);
                break;
            case "maintenance":
                await MaintainAsync(context, hierarchy, key, treeId);
                break;
            case "managed":
                await SaveAsync(context, hierarchy, key, parent, treeId);
                break;
            case "missing":
            case "cycle":
                await RejectAsync(context, hierarchy, key, treeId, operation == "missing");
                break;
            case "rollback":
                await RollbackAsync(context, hierarchy, key, parent, treeId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>Stores lowercase provider aliases, including values normalized on enum materialization.</summary>
    private static Task<int> LowerParentsAsync<TKey, TParent>(
        DbContext context
    )
    {
        var entity = context.Model.FindEntityType(typeof(ConvertedTextCollationNode<TKey, TParent>))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(store.Name, store.Schema);
        var parent = sql.DelimitIdentifier(entity.FindProperty("ParentId")!.GetColumnName(store)!);

        // WHY: Model enum aliases materialize to the same value. Writing the provider representation directly
        // proves the SQL join uses principal equality instead of accidentally testing canonical CLR spellings.
        var command = $"UPDATE {table} SET {parent} = LOWER({parent}) WHERE {parent} IS NOT NULL";

        return context.Database.ExecuteSqlRawAsync(command, CancellationToken.None);
    }

    /// <summary>Verifies symmetric indexed parent navigation with mapped converter parameters.</summary>
    private static async Task QueryAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId
    )
        where TKey : notnull
    {
        // Arrange
        var parents = hierarchy.ParentOf(key("cHiLd"));
        var children = hierarchy.ChildrenOf(key("rOoT"));
        var entity = context.Model.FindEntityType(typeof(ConvertedTextCollationNode<TKey, TParent>))!;
        var property = entity.FindProperty("Id")!;
        var collation = NestedSetCollations.Resolve(context, property, entity);

        // Act
        var parentRows = await parents.ToArrayAsync(CancellationToken.None);
        var childRows = await children.ToArrayAsync(CancellationToken.None);
        var leafParents = await hierarchy
            .ParentOf(key("lEaF"))
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(key("ROOT"), Assert.Single(parentRows).Id);
        Assert.Equal(new[] { key("CHILD"), key("OTHER") }, childRows.Select(node => node.Id));
        Assert.Equal(key("CHILD"), Assert.Single(leafParents).Id);

        if (property.GetTypeMapping().Converter!.ProviderClrType == typeof(string))
        {
            Assert.NotNull(collation);
            Assert.Contains(collation, parents.ToQueryString(), StringComparison.Ordinal);
            Assert.Contains(collation, children.ToQueryString(), StringComparison.Ordinal);
        }
        else
        {
            // WHY: SQLite and Doka select an integral enum converter even for a declared text column. Collation must
            // follow the effective conversion rather than inferring text identity from the column type name.
            Assert.Equal(typeof(int), property.GetTypeMapping().Converter!.ProviderClrType);
            Assert.True(context.Database.IsSqlite()
                || context.GetService<IDatabaseProvider>().Name == NestedSetProviderCapabilities.MySqlProviderName);
            Assert.Null(collation);
            Assert.DoesNotContain(" COLLATE ", parents.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        }

        await AssertValidAsync(hierarchy, treeId);
    }

    /// <summary>Structural reads materialize nullable storage once without selecting application payload.</summary>
    private static async Task ProjectAsync<TKey, TParent>(
        DbContext context,
        Func<string, TKey> key,
        Guid treeId
    )
        where TKey : notnull
    {
        // Arrange
        var entity = context.Model.FindEntityType(typeof(ConvertedTextCollationNode<TKey, TParent>))!;
        var store = new NestedSetStore<ConvertedTextCollationNode<TKey, TParent>, TKey, Guid, NestedSetNoScope>(
            context,
            entity,
            default,
            treeId);

        var sql = store.Structure.ToQueryString();

        // Act
        var stored = await store.Structure.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(4, stored.Length);
        Assert.False(Assert.Single(stored, node => node.Key.Equals(key("ROOT"))).Parent.HasValue);
        Assert.True(Assert.Single(stored, node => node.Key.Equals(key("LEAF"))).Parent.HasValue);
        Assert.DoesNotContain("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Name", sql, StringComparison.Ordinal);
        Assert.Contains("ParentId", sql, StringComparison.Ordinal);
    }

    /// <summary>Deletion promotes aliased children and keeps the ordered sibling group dense.</summary>
    private static async Task DeleteAsync<TKey, TParent>(
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId
    )
        where TKey : notnull
    {
        // Arrange
        var source = key("cHiLd");

        // Act
        await hierarchy.DeleteAsync(source, CancellationToken.None);

        // Assert
        var children = await hierarchy
            .ChildrenOf(key("rOoT"))
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(new[] { key("OTHER"), key("LEAF") }, children.Select(node => node.Id));
        Assert.Equal(new long[] { 0, 1 }, children.Select(node => node.Position));
        Assert.Equal(
            key("ROOT"),
            (await hierarchy
                .ParentOf(key("lEaF"))
                .SingleAsync(CancellationToken.None)).Id);
        await AssertValidAsync(hierarchy, treeId);
    }

    /// <summary>Managed alias changes preserve descendants and refresh unchanged tracked destination rows.</summary>
    private static async Task SaveAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Func<string, TParent> parent,
        Guid treeId
    )
        where TKey : notnull
    {
        // Arrange
        var tracked = await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .ToArrayAsync(CancellationToken.None);

        var child = Assert.Single(tracked, node => node.Id.Equals(key("CHILD")));
        child.ParentId = parent("oThEr");
        child.Name = "Z";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal(
            key("OTHER"),
            (await hierarchy
                .ParentOf(key("cHiLd"))
                .SingleAsync(CancellationToken.None)).Id);
        Assert.Equal(
            key("CHILD"),
            (await hierarchy
                .ParentOf(key("lEaF"))
                .SingleAsync(CancellationToken.None)).Id);
        var stored = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);
        Assert.All(
            tracked,
            node =>
            {
                var row = Assert.Single(stored, candidate => candidate.Id.Equals(node.Id));
                Assert.Equal(
                    (row.Left, row.Right, row.Depth, row.Position),
                    (node.Left, node.Right, node.Depth, node.Position));
                Assert.Equal(
                    EntityState.Unchanged,
                    context.Entry(node)
                        .State);
            });

        await AssertValidAsync(hierarchy, treeId);
    }

    /// <summary>Movement resolves aliased source and target groups using principal equality.</summary>
    private static async Task MoveAsync<TKey, TParent>(
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId
    )
        where TKey : notnull
    {
        // Arrange
        var source = key("lEaF");
        var target = key("oThEr");

        // Act
        await hierarchy.MoveToAsync(source, target, CancellationToken.None);

        // Assert
        Assert.Empty(
            await hierarchy
                .ChildrenOf(key("cHiLd"))
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            key("LEAF"),
            (await hierarchy
                .ChildrenOf(key("oThEr"))
                .SingleAsync(CancellationToken.None)).Id);
        Assert.Equal(
            key("OTHER"),
            (await hierarchy
                .ParentOf(key("lEaF"))
                .SingleAsync(CancellationToken.None)).Id);
        await AssertValidAsync(hierarchy, treeId);
    }

    /// <summary>Repair and rule ordering resolve canonical parent links without losing nullable converters.</summary>
    private static async Task MaintainAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId
    )
        where TKey : notnull
    {
        // Arrange
        await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .Where(node => node.Id.Equals(key("CHILD")))
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Name, "Z"), CancellationToken.None);

        // Act
        await hierarchy
            .InTree(treeId)
            .RebuildAsync(CancellationToken.None);

        // Assert
        var children = await hierarchy
            .ChildrenOf(key("rOoT"))
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(new[] { key("OTHER"), key("CHILD") }, children.Select(node => node.Id));
        Assert.Equal(
            key("CHILD"),
            (await hierarchy
                .ParentOf(key("lEaF"))
                .SingleAsync(CancellationToken.None)).Id);
        await AssertValidAsync(hierarchy, treeId);
    }

    /// <summary>Missing and cyclic alias requests preserve the entire persisted tree.</summary>
    private static async Task RejectAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId,
        bool missing
    )
        where TKey : notnull
    {
        // Arrange
        var before = await SnapshotAsync<TKey, TParent>(context);

        // Act
        var failure = await Assert.ThrowsAsync<NestedSetException>(() => missing
            ? hierarchy.InsertChildAsync(
                new ConvertedTextCollationNode<TKey, TParent>
                {
                    Id = key("MISSING"),
                    Name = "Missing",
                },
                key("mIsSiNg"),
                CancellationToken.None)
            : hierarchy.MoveToAsync(key("rOoT"), key("lEaF"), CancellationToken.None));

        // Assert
        Assert.Equal(missing ? NestedSetErrorCode.NodeNotFound : NestedSetErrorCode.CycleDetected, failure.Code);
        Assert.Equal(before, await SnapshotAsync<TKey, TParent>(context));
        await AssertValidAsync(hierarchy, treeId);
    }

    /// <summary>A late callback failure restores rows and retains the caller's pending tracked change.</summary>
    private static async Task RollbackAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Func<string, TParent> parent,
        Guid treeId
    )
        where TKey : notnull
    {
        // Arrange
        var before = await SnapshotAsync<TKey, TParent>(context);
        var child = await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .SingleAsync(node => node.Id.Equals(key("CHILD")), CancellationToken.None);

        child.ParentId = parent("oThEr");
        child.Name = "Z";
        var callbackRan = false;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveNestedSetChangesAsync(
            true,
            async token =>
            {
                await context.SaveChangesAsync(false, token);
                callbackRan = true;

                throw new InvalidOperationException("Converted collation rollback probe.");
            },
            CancellationToken.None));

        // Assert
        Assert.True(callbackRan);
        Assert.Equal("Converted collation rollback probe.", failure.Message);
        Assert.Equal(before, await SnapshotAsync<TKey, TParent>(context));
        Assert.Equal(parent("oThEr"), child.ParentId);
        Assert.Equal("Z", child.Name);
        Assert.Equal(EntityState.Modified, context.Entry(child).State);
        await AssertValidAsync(hierarchy, treeId);
    }

    /// <summary>Observes every persisted structural and payload value independently of the change tracker.</summary>
    private static async Task<(TKey, TParent, Guid, long, long, int, long, string)[]> SnapshotAsync<TKey, TParent>(
        DbContext context
    )
    {
        var stored = await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        return stored
            .Select(node => (node.Id, node.ParentId, node.TreeId, node.Left, node.Right, node.Depth, node.Position,
                node.Name))
            .ToArray();
    }

    /// <summary>Checks adjacency, bounds, positions, and configured ordering after the acceptance probe.</summary>
    private static async Task AssertValidAsync<TKey, TParent>(
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Guid treeId
    )
    {
        var report = await hierarchy
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        Assert.True(report.IsValid);
    }
}
