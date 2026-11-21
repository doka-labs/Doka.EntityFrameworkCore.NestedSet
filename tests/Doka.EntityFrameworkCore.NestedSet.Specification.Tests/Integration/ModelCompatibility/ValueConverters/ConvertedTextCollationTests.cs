namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects principal-key equality across custom and built-in text converters.</summary>
[Collection("Model compatibility")]
public abstract partial class ConvertedTextCollationTests : ProviderTest
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

    /// <summary>Case aliases use the principal's collation even when the parent's text column is binary.</summary>
    /// <param name="model">The custom reference, custom value, explicit built-in, or implicit conversion shape.</param>
    /// <param name="operation">The public behavior or atomic failure boundary to verify.</param>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RunAsync_ConvertedKeys_UsePrincipalCollation(
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
                    operation,
                    cancellationToken: TestContext.Current.CancellationToken);

                break;
            case 1:
                await RunAsync<ValueTextCollationContext, ValueTextKey, ValueTextKey?>(
                    options => new ValueTextCollationContext(options),
                    value => new ValueTextKey(value),
                    value => new ValueTextKey(value),
                    operation,
                    cancellationToken: TestContext.Current.CancellationToken);

                break;
            case 2:
                await RunAsync<EnumTextCollationContext, EnumTextKey, EnumTextKey?>(
                    options => new EnumTextCollationContext(options),
                    value => Enum.Parse<EnumTextKey>(value, true),
                    value => Enum.Parse<EnumTextKey>(value, true),
                    operation,
                    cancellationToken: TestContext.Current.CancellationToken);

                break;
            case 3:
                await RunAsync<ImplicitEnumTextCollationContext, EnumTextKey, EnumTextKey?>(
                    options => new ImplicitEnumTextCollationContext(options),
                    value => Enum.Parse<EnumTextKey>(value, true),
                    value => Enum.Parse<EnumTextKey>(value, true),
                    operation,
                    cancellationToken: TestContext.Current.CancellationToken);

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(model));
        }
    }

    /// <summary>Registry DDL and supplied runtime models retain the same implicit conversion and collation.</summary>
    [DatabaseIndependent]
    [Fact]
    public void UseModel_ImplicitIdentityCapture_MatchesRegistryAndSuppliedModel()
    {
        // Arrange
        var connection = Engine == "Sqlite"
            ? "Data Source=:memory:"
            : "Server=localhost;Database=model_compatibility;User ID=unused;Password=unused";

        var options = ModelCompatibilityDatabase.Options<ImplicitRegistryTextCollationContext>(Engine, connection);
        using var source = new ImplicitRegistryTextCollationContext(options);
        var model = source.Model;
        var designModel = source.GetService<IDesignTimeModel>()
            .Model;

        var node = model.FindEntityType(typeof(ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>))!;
        var designNode = designModel.FindEntityType(node.Name)!;
        var store = StoreObjectIdentifier.Table(designNode.GetTableName()!, designNode.GetSchema());
        var registryName = (string)node.FindAnnotation(NestedSetAnnotationNames.TreeRegistryEntity)!.Value!;
        var registry = designModel.FindEntityType(registryName)!;
        var provider = NestedSetProviderCapabilities.Resolve(source);
        var typeMappings = source.GetService<ITypeMappingSource>();

        // Act
        using var supplied = new ImplicitRegistryTextCollationContext(
            new DbContextOptionsBuilder<ImplicitRegistryTextCollationContext>(options).UseModel(model)
                .Options);

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
                var property = registry.FindProperty(
                    name == "Scope"
                        ? "Scope"
                        : "TreeId")!;

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
            Assert.True(source.Database.IsSqlite() || provider.ProviderName == DatabaseTestTargets.MySqlProviderName);
            Assert.DoesNotContain(" COLLATE ", parentSql, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Required nullable keys keep their converter through canonical parent joins and repair.</summary>
    [Fact]
    public async Task RebuildAsync_RequiredNullableConvertedKey_SupportsAliasMaintenance()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<NullableEnumTextCollationContext>(
            Engine,
            options => new NullableEnumTextCollationContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

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

        await LowerParentsAsync<EnumTextKey?, EnumTextKey?>(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

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

    /// <summary>Observes every persisted structural and payload value independently of the change tracker.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<(TKey, TParent, Guid, long, long, int, long, string)[]> SnapshotAsync<TKey, TParent>(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        var stored = await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(cancellationToken);

        return stored
            .Select(node => (node.Id, node.ParentId, node.TreeId, node.Left, node.Right, node.Depth, node.Position,
                node.Name))
            .ToArray();
    }

    /// <summary>Checks adjacency, bounds, positions, and configured ordering after the acceptance probe.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task AssertValidAsync<TKey, TParent>(
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var report = await hierarchy
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

        Assert.True(report.IsValid);
    }
}
