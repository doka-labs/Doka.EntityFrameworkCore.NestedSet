namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies cached dispatch retains named metadata, caller context and immutable scope bindings.</summary>
public abstract class MutationBindingIsolationTests : ProviderTest
{
    private const string Alpha = "MutationBindingAlpha";
    private const string Beta = "MutationBindingBeta";
    private const string Id = "Id";
    private const string Scope = "Scope";
    private const string Tree = "TreeId";
    private const string Parent = "ParentId";
    private const string Left = "Left";
    private const string Right = "Right";
    private const string Depth = "Depth";
    private const string Position = "Position";
    private static readonly Guid s_tree = Guid.Parse("44000000-0000-0000-0000-000000000001");
    private static readonly Guid s_detached = Guid.Parse("44000000-0000-0000-0000-000000000002");
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses existing provider ownership while this model receives its own physical tables.</summary>
    /// <param name="fixture">The owner of model-compatibility databases.</param>
    protected MutationBindingIsolationTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture) => _fixture = fixture.Value;

    /// <summary>Two shared CLR mappings retain different key roles across interleaved contexts and scopes.</summary>
    /// <returns>A task that completes after verifying physical rows and registry lifecycle isolation.</returns>
    [Fact]
    public async Task InterleavedNamedBindingsKeepRowsContextsScopesAndRegistriesSeparate()
    {
        // Arrange
        await using var first = await _fixture.CreateContextAsync<IsolationContext>(
            Engine,
            static options => new IsolationContext(options));

        // WHY: This scenario requires identical metadata across distinct contexts. Supply that model explicitly
        // instead of relying on EF's bounded model cache while the full suite builds other models in parallel.
        var sharedOptions = new DbContextOptionsBuilder<IsolationContext>(
                (DbContextOptions<IsolationContext>)first.GetService<IDbContextOptions>()).UseModel(first.Model)
            .Options;

        await using var second = new IsolationContext(sharedOptions);

        await ResetModelAsync(first);
        var alphaOne = first
            .NestedSet<Dictionary<string, object>>(Alpha)
            .ForScope(1);

        var alphaTwo = second
            .NestedSet<Dictionary<string, object>>(Alpha)
            .ForScope(2);

        var betaOne = second
            .NestedSet<Dictionary<string, object>>(Beta)
            .ForScope("one");

        var betaTwo = first
            .NestedSet<Dictionary<string, object>>(Beta)
            .ForScope("two");

        // Act
        await alphaOne.InsertRootAsync(Bag(1, 0, Guid.Empty), s_tree, CancellationToken.None);
        await betaOne.InsertRootAsync(Bag(GuidKey(1), "", ""), "shared", CancellationToken.None);
        await alphaTwo.InsertRootAsync(Bag(10, 0, Guid.Empty), s_tree, CancellationToken.None);
        await betaTwo.InsertRootAsync(Bag(GuidKey(10), "", ""), "shared", CancellationToken.None);
        await alphaOne.InsertChildAsync(Bag(2, 0, Guid.Empty), 1, CancellationToken.None);
        await betaOne.InsertChildAsync(Bag(GuidKey(2), "", ""), GuidKey(1), CancellationToken.None);
        await alphaOne.InsertChildAsync(Bag(3, 0, Guid.Empty), 2, CancellationToken.None);
        await betaTwo.InsertChildAsync(Bag(GuidKey(11), "", ""), GuidKey(10), CancellationToken.None);
        await alphaTwo.InsertChildAsync(Bag(11, 0, Guid.Empty), 10, CancellationToken.None);
        await betaOne.InsertChildAsync(Bag(GuidKey(3), "", ""), GuidKey(1), CancellationToken.None);
        await alphaOne.DeleteSubtreeAsync(2, CancellationToken.None);
        await betaOne.MoveToAsync(GuidKey(3), GuidKey(2), CancellationToken.None);
        await alphaTwo.DetachAsTreeAsync(11, s_detached, CancellationToken.None);
        await betaTwo.DeleteTreeAsync("shared", CancellationToken.None);

        // Assert
        Assert.Same(first.Model.FindEntityType(Alpha), second.Model.FindEntityType(Alpha));
        Assert.Same(first.Model.FindEntityType(Beta), second.Model.FindEntityType(Beta));
        Assert.NotSame(first.Model.FindEntityType(Alpha), first.Model.FindEntityType(Beta));
        Assert.Equal(
            (1, 1, s_tree, null, 1L, 2L, 0, 0L),
            AlphaRow(
                await alphaOne
                    .InTree(s_tree)
                    .Nodes
                    .SingleAsync(CancellationToken.None)));
        Assert.Equal(
            (10, 2, s_tree, null, 1L, 2L, 0, 0L),
            AlphaRow(
                await alphaTwo
                    .InTree(s_tree)
                    .Nodes
                    .SingleAsync(CancellationToken.None)));
        Assert.Equal(
            (11, 2, s_detached, null, 1L, 2L, 0, 0L),
            AlphaRow(
                await alphaTwo
                    .InTree(s_detached)
                    .Nodes
                    .SingleAsync(CancellationToken.None)));
        Assert.Collection(
            await betaOne
                .InTree("shared")
                .Nodes
                .ToArrayAsync(CancellationToken.None),
            root => Assert.Equal((GuidKey(1), "one", "shared", null, 1L, 6L, 0, 0L), BetaRow(root)),
            child => Assert.Equal((GuidKey(2), "one", "shared", (Guid?)GuidKey(1), 2L, 5L, 1, 0L), BetaRow(child)),
            grandchild => Assert.Equal(
                (GuidKey(3), "one", "shared", (Guid?)GuidKey(2), 3L, 4L, 2, 0L),
                BetaRow(grandchild)));
        Assert.False(
            await betaTwo
                .InTree("shared")
                .Nodes
                .AnyAsync(CancellationToken.None));
        Assert.Equal(
            3,
            await first
                .Set<Dictionary<string, object>>(Alpha)
                .CountAsync(CancellationToken.None));
        Assert.Equal(
            3,
            await first
                .Set<Dictionary<string, object>>(Beta)
                .CountAsync(CancellationToken.None));
        Assert.Equal(
            new[]
                {
                    (1, s_tree, NestedSetTreeRegistryMetadata.Active),
                    (2, s_tree, NestedSetTreeRegistryMetadata.Active),
                    (2, s_detached, NestedSetTreeRegistryMetadata.Active),
                }
                .OrderBy(row => row.Item1)
                .ThenBy(row => row.Item2),
            await AlphaRegistriesAsync(first));
        Assert.Equal(
            new[]
            {
                ("one", "shared", NestedSetTreeRegistryMetadata.Active),
                ("two", "shared", NestedSetTreeRegistryMetadata.Tombstoned),
            },
            await StringRegistriesAsync(first, Beta));
        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
    }

    /// <summary>Equal shared entity names and CLR types cannot reuse another model's closed identity roles.</summary>
    /// <returns>A task that completes after verifying both specialized mutation bindings and registries.</returns>
    [Fact]
    public async Task SameEntityNameAcrossDifferentModelsKeepsTypedMutationBindingsSeparate()
    {
        // Arrange
        await using var first = await _fixture.CreateContextAsync<IsolationContext>(
            Engine,
            static options => new IsolationContext(options));

        await using var alternate = await _fixture.CreateContextAsync<AlternateContext>(
            Engine,
            static options => new AlternateContext(options));

        await ResetModelAsync(first);
        await ResetModelAsync(alternate);
        var integerKeys = first
            .NestedSet<Dictionary<string, object>>(Alpha)
            .ForScope(1);

        var guidKeys = alternate
            .NestedSet<Dictionary<string, object>>(Alpha)
            .ForScope("one");

        // Act
        await integerKeys.InsertRootAsync(Bag(1, 0, Guid.Empty), s_tree, CancellationToken.None);
        await guidKeys.InsertRootAsync(Bag(GuidKey(1), "", ""), "shared", CancellationToken.None);
        await integerKeys.InsertChildAsync(Bag(2, 0, Guid.Empty), 1, CancellationToken.None);
        await guidKeys.InsertChildAsync(Bag(GuidKey(2), "", ""), GuidKey(1), CancellationToken.None);
        await integerKeys.DeleteSubtreeAsync(2, CancellationToken.None);
        await guidKeys.DetachAsTreeAsync(GuidKey(2), "detached", CancellationToken.None);

        // Assert
        var firstEntity = first.Model.FindEntityType(Alpha)!;
        var alternateEntity = alternate.Model.FindEntityType(Alpha)!;
        Assert.NotSame(first.Model, alternate.Model);
        Assert.Equal(firstEntity.Name, alternateEntity.Name);
        Assert.Equal(firstEntity.ClrType, alternateEntity.ClrType);
        Assert.NotEqual(firstEntity.FindProperty(Id)!.ClrType, alternateEntity.FindProperty(Id)!.ClrType);
        Assert.NotEqual(firstEntity.GetTableName(), alternateEntity.GetTableName());
        Assert.Equal(
            (1, 1, s_tree, null, 1L, 2L, 0, 0L),
            AlphaRow(
                await integerKeys
                    .InTree(s_tree)
                    .Nodes
                    .SingleAsync(CancellationToken.None)));
        Assert.Equal(
            (GuidKey(1), "one", "shared", null, 1L, 2L, 0, 0L),
            BetaRow(
                await guidKeys
                    .InTree("shared")
                    .Nodes
                    .SingleAsync(CancellationToken.None)));
        Assert.Equal(
            (GuidKey(2), "one", "detached", null, 1L, 2L, 0, 0L),
            BetaRow(
                await guidKeys
                    .InTree("detached")
                    .Nodes
                    .SingleAsync(CancellationToken.None)));
        Assert.Equal(new[] { (1, s_tree, NestedSetTreeRegistryMetadata.Active) }, await AlphaRegistriesAsync(first));
        Assert.Equal(
            new[]
            {
                ("one", "detached", NestedSetTreeRegistryMetadata.Active),
                ("one", "shared", NestedSetTreeRegistryMetadata.Active),
            },
            await StringRegistriesAsync(alternate, Alpha));
        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(alternate.ChangeTracker.Entries());
    }

    /// <summary>Completely resets only the property-bag hierarchy tables owned by the selected test model.</summary>
    private static async Task ResetModelAsync(
        DbContext context
    )
    {
        foreach (var entity in context
                     .Model
                     .GetEntityTypes()
                     .Where(entity => entity.ClrType == typeof(Dictionary<string, object>)))
        {
            var rows = context.Set<Dictionary<string, object>>(entity.Name);

            if (entity.FindProperty(Parent)!.ClrType == typeof(int?))
            {
                await rows.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => EF.Property<int?>(row, Parent), (int?)null),
                    CancellationToken.None);
            }
            else
            {
                await rows.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => EF.Property<Guid?>(row, Parent), (Guid?)null),
                    CancellationToken.None);
            }

            await rows.ExecuteDeleteAsync(CancellationToken.None);
        }

        await context.ClearNestedSetTreeRegistriesAsync(CancellationToken.None);
    }

    /// <summary>Initializes explicit property-bag roles without relying on missing-indexer defaults.</summary>
    private static Dictionary<string, object> Bag(
        object id,
        object scope,
        object tree
    ) => new()
    {
        [Id] = id,
        [Scope] = scope,
        [Tree] = tree,
        [Parent] = null!,
        [Left] = 0L,
        [Right] = 0L,
        [Depth] = 0,
        [Position] = 0L,
    };

    /// <summary>Creates deterministic Guid node keys distinct from the integer mapping's native keys.</summary>
    private static Guid GuidKey(
        int ordinal
    ) => Guid.Parse($"45000000-0000-0000-0000-{ordinal:D12}");

    /// <summary>Reads the integer/Guid/int mapping through its own property-bag role types.</summary>
    private static (int Id, int Scope, Guid Tree, int? Parent, long Left, long Right, int Depth, long Position)
        AlphaRow(
            Dictionary<string, object> row
        ) => ((int)row[Id], (int)row[Scope], (Guid)row[Tree], (int?)row[Parent], (long)row[Left], (long)row[Right],
        (int)row[Depth], (long)row[Position]);

    /// <summary>Reads the Guid/string/string mapping through its independent property-bag role types.</summary>
    private static (Guid Id, string Scope, string Tree, Guid? Parent, long Left, long Right, int Depth, long Position)
        BetaRow(
            Dictionary<string, object> row
        ) => ((Guid)row[Id], (string)row[Scope], (string)row[Tree], (Guid?)row[Parent], (long)row[Left],
        (long)row[Right],
        (int)row[Depth], (long)row[Position]);

    /// <summary>Reads the registry owned solely by the integer-key named hierarchy.</summary>
    private static Task<(int Scope, Guid Tree, byte Lifecycle)[]> AlphaRegistriesAsync(
        IsolationContext context
    )
    {
        var registry = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(Alpha)!).Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .OrderBy(row => EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope))
            .ThenBy(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId))
            .Select(row => new ValueTuple<int, Guid, byte>(
                EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope),
                EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Reads the independent registry owned solely by the Guid-key named hierarchy.</summary>
    private static Task<(string Scope, string Tree, byte Lifecycle)[]> StringRegistriesAsync(
        DbContext context,
        string entityTypeName
    )
    {
        var registry = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(entityTypeName)!).Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .OrderBy(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope))
            .ThenBy(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.TreeId))
            .Select(row => new ValueTuple<string, string, byte>(
                EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope),
                EF.Property<string>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Configures shared coordinates while each exact model retains its own scalar identity roles.</summary>
    private static void ConfigureCoordinates(
        EntityTypeBuilder<Dictionary<string, object>> node
    )
    {
        node.IndexerProperty<long>(Left);
        node.IndexerProperty<long>(Right);
        node.IndexerProperty<int>(Depth);
        node.IndexerProperty<long>(Position);
        node.HasKey(Id);
        node.HasNestedSet(builder => builder
            .HasNodeKey(Id)
            .HasTreeId(Tree)
            .HasScope(Scope)
            .HasParent(Parent)
            .HasBounds(Left, Right)
            .HasDepth(Depth)
            .HasPosition(Position));
    }

    /// <summary>Maps one shared CLR type twice with intentionally different generic identity roles.</summary>
    private sealed class IsolationContext(DbContextOptions<IsolationContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            // WHY: Distinct closed key/tree/scope roles expose cache keys based only on CLR type or caller
            // context. Both named mappings must share immutable invokers only with their exact EF metadata.
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                Alpha,
                node =>
                {
                    node.ToTable("MutationBindingAlphaNodes");
                    node
                        .IndexerProperty<int>(Id)
                        .ValueGeneratedNever();
                    node.IndexerProperty<int>(Scope);
                    node.IndexerProperty<Guid>(Tree);
                    node.IndexerProperty<int?>(Parent);
                    ConfigureCoordinates(node);
                });

            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                Beta,
                node =>
                {
                    node.ToTable("MutationBindingBetaNodes");
                    node
                        .IndexerProperty<Guid>(Id)
                        .ValueGeneratedNever();
                    node
                        .IndexerProperty<string>(Scope)
                        .HasMaxLength(40)
                        .IsRequired();
                    node
                        .IndexerProperty<string>(Tree)
                        .HasMaxLength(40)
                        .IsRequired();
                    node.IndexerProperty<Guid?>(Parent);
                    ConfigureCoordinates(node);
                });
        }
    }

    /// <summary>Reuses one shared CLR type and entity name under a different model and specialization.</summary>
    private sealed class AlternateContext(DbContextOptions<AlternateContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            // WHY: A name or CLR-type cache key would select Alpha's integer specialization from the first
            // model. Exact IEntityType ownership must instead preserve this model's Guid/string/string roles.
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                Alpha,
                node =>
                {
                    node.ToTable("MutationBindingAlternateAlphaNodes");
                    node
                        .IndexerProperty<Guid>(Id)
                        .ValueGeneratedNever();
                    node
                        .IndexerProperty<string>(Scope)
                        .HasMaxLength(40)
                        .IsRequired();
                    node
                        .IndexerProperty<string>(Tree)
                        .HasMaxLength(40)
                        .IsRequired();
                    node.IndexerProperty<Guid?>(Parent);
                    ConfigureCoordinates(node);
                });
        }
    }
}
