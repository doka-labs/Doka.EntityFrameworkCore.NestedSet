namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies cached dispatch retains named metadata, caller context and immutable scope bindings.</summary>
public abstract partial class MutationBindingIsolationTests : ProviderTest
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
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Two shared CLR mappings retain different key roles across interleaved contexts and scopes.</summary>
    /// <returns>A task that completes after verifying physical rows and registry lifecycle isolation.</returns>
    [Fact]
    public async Task InsertRootAsync_InterleavedNamedBindings_KeepRowsContextsScopesAndRegistriesSeparate()
    {
        // Arrange
        await using var first = await _fixture.CreateContextAsync<IsolationContext>(
            Engine,
            static options => new IsolationContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        // WHY: This scenario requires identical metadata across distinct contexts. Supply that model explicitly
        // instead of relying on EF's bounded model cache while the full suite builds other models in parallel.
        var sharedOptions = new DbContextOptionsBuilder<IsolationContext>(
                (DbContextOptions<IsolationContext>)first.GetService<IDbContextOptions>()).UseModel(first.Model)
            .Options;

        await using var second = new IsolationContext(sharedOptions);

        await ResetModelAsync(first, cancellationToken: TestContext.Current.CancellationToken);
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
            await AlphaRegistriesAsync(first, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            new[]
            {
                ("one", "shared", NestedSetTreeRegistryMetadata.Active),
                ("two", "shared", NestedSetTreeRegistryMetadata.Tombstoned),
            },
            await StringRegistriesAsync(first, Beta, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
    }

    /// <summary>Equal shared entity names and CLR types cannot reuse another model's closed identity roles.</summary>
    /// <returns>A task that completes after verifying both specialized mutation bindings and registries.</returns>
    [Fact]
    public async Task InsertRootAsync_SameEntityNameAcrossDifferentModels_KeepsTypedMutationBindingsSeparate()
    {
        // Arrange
        await using var first = await _fixture.CreateContextAsync<IsolationContext>(
            Engine,
            static options => new IsolationContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        await using var alternate = await _fixture.CreateContextAsync<AlternateContext>(
            Engine,
            static options => new AlternateContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        await ResetModelAsync(first, cancellationToken: TestContext.Current.CancellationToken);
        await ResetModelAsync(alternate, cancellationToken: TestContext.Current.CancellationToken);
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

        Assert.Equal(
            new[] { (1, s_tree, NestedSetTreeRegistryMetadata.Active) },
            await AlphaRegistriesAsync(
                first,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            new[]
            {
                ("one", "detached", NestedSetTreeRegistryMetadata.Active),
                ("one", "shared", NestedSetTreeRegistryMetadata.Active),
            },
            await StringRegistriesAsync(alternate, Alpha, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(alternate.ChangeTracker.Entries());
    }
}
