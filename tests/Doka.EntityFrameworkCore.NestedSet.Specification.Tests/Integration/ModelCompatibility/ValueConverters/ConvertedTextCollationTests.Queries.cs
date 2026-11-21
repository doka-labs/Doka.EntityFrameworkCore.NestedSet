namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ConvertedTextCollationTests
{
    /// <summary>Combines every converter shape with each query, mutation, maintenance, and failure probe.</summary>
    /// <returns>Provider-neutral cases inherited by every supported engine.</returns>
    public static IEnumerable<TheoryDataRow<int, string>> Cases()
    {
        // WHY: Yield each independent shape/operation case lazily so discovery does not retain a case matrix.
        // ReSharper disable once LoopCanBeConvertedToQuery
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

    /// <summary>Seeds one aliased adjacency tree and executes one independent acceptance probe.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task RunAsync<TContext, TKey, TParent>(
        Func<DbContextOptions<TContext>, TContext> create,
        Func<string, TKey> key,
        Func<string, TParent> parent,
        string operation,
        CancellationToken cancellationToken
    )
        where TContext : DbContext
        where TKey : notnull
    {
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            create,
            cancellationToken: cancellationToken);

        await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .ExecuteDeleteAsync(cancellationToken);

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
            cancellationToken);

        await LowerParentsAsync<TKey, TParent>(context, cancellationToken: cancellationToken);
        context.ChangeTracker.Clear();

        switch (operation)
        {
            case "query":
                await QueryAsync(context, hierarchy, key, treeId, cancellationToken: cancellationToken);
                break;
            case "projection":
                await ProjectAsync<TKey, TParent>(context, key, treeId, cancellationToken: cancellationToken);
                break;
            case "delete":
                await DeleteAsync(hierarchy, key, treeId, cancellationToken: cancellationToken);
                break;
            case "move":
                await MoveAsync(hierarchy, key, treeId, cancellationToken: cancellationToken);
                break;
            case "maintenance":
                await MaintainAsync(context, hierarchy, key, treeId, cancellationToken: cancellationToken);
                break;
            case "managed":
                await SaveAsync(context, hierarchy, key, parent, treeId, cancellationToken: cancellationToken);
                break;
            case "missing":
            case "cycle":
                await RejectAsync(
                    context,
                    hierarchy,
                    key,
                    treeId,
                    operation == "missing",
                    cancellationToken: cancellationToken);

                break;
            case "rollback":
                await RollbackAsync(context, hierarchy, key, parent, treeId, cancellationToken: cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>Stores lowercase provider aliases, including values normalized on enum materialization.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<int> LowerParentsAsync<TKey, TParent>(
        DbContext context,
        CancellationToken cancellationToken
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

        return context.Database.ExecuteSqlRawAsync(command, cancellationToken);
    }

    /// <summary>Verifies symmetric indexed parent navigation with mapped converter parameters.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task QueryAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId,
        CancellationToken cancellationToken
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
        var parentRows = await parents.ToArrayAsync(cancellationToken);
        var childRows = await children.ToArrayAsync(cancellationToken);
        var leafParents = await hierarchy
            .ParentOf(key("lEaF"))
            .ToArrayAsync(cancellationToken);

        // Assert
        Assert.Equal(
            key("ROOT"),
            Assert.Single(parentRows)
                .Id);

        Assert.Equal(new[] { key("CHILD"), key("OTHER") }, childRows.Select(node => node.Id));
        Assert.Equal(
            key("CHILD"),
            Assert.Single(leafParents)
                .Id);

        if (property.GetTypeMapping()
                .Converter!.ProviderClrType
            == typeof(string))
        {
            Assert.NotNull(collation);
            Assert.Contains(collation, parents.ToQueryString(), StringComparison.Ordinal);
            Assert.Contains(collation, children.ToQueryString(), StringComparison.Ordinal);
        }
        else
        {
            // WHY: SQLite and Doka select an integral enum converter even for a declared text column. Collation must
            // follow the effective conversion rather than inferring text identity from the column type name.
            Assert.Equal(
                typeof(int),
                property.GetTypeMapping()
                    .Converter!.ProviderClrType);

            Assert.True(
                context.Database.IsSqlite()
                || context.GetService<IDatabaseProvider>()
                    .Name
                == DatabaseTestTargets.MySqlProviderName);

            Assert.Null(collation);
            Assert.DoesNotContain(" COLLATE ", parents.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        }

        await AssertValidAsync(hierarchy, treeId, cancellationToken: cancellationToken);
    }

    /// <summary>Structural reads materialize nullable storage once without selecting application payload.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task ProjectAsync<TKey, TParent>(
        DbContext context,
        Func<string, TKey> key,
        Guid treeId,
        CancellationToken cancellationToken
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
        var stored = await store.Structure.ToArrayAsync(cancellationToken);

        // Assert
        Assert.Equal(4, stored.Length);
        Assert.False(
            Assert.Single(stored, node => node.Key.Equals(key("ROOT")))
                .Parent.HasValue);

        Assert.True(
            Assert.Single(stored, node => node.Key.Equals(key("LEAF")))
                .Parent.HasValue);

        Assert.DoesNotContain("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Name", sql, StringComparison.Ordinal);
        Assert.Contains("ParentId", sql, StringComparison.Ordinal);
    }
}
