namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies assigned default scalar keys remain present parents rather than root sentinels.</summary>
public abstract partial class TypedParentContractTests : ProviderTest
{
    private static readonly Guid s_source = Guid.Parse("41000000-0000-0000-0000-000000000001");
    private static readonly Guid s_target = Guid.Parse("41000000-0000-0000-0000-000000000002");
    private static readonly Guid s_child = Guid.Parse("42000000-0000-0000-0000-000000000001");
    private static readonly Guid s_other = Guid.Parse("42000000-0000-0000-0000-000000000002");
    private readonly ModelCompatibilityDatabase _convertedFixture;
    private readonly RelationalFixture _fixture;

    /// <summary>Creates assigned-key contracts on the existing relational fixture.</summary>
    /// <param name="fixture">The owner of the reusable provider databases.</param>
    /// <param name="convertedFixture">The existing database owner for the private converted-key model.</param>
    protected TypedParentContractTests(
        IProviderFixture<RelationalFixture> fixture,
        IProviderFixture<ModelCompatibilityDatabase> convertedFixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _convertedFixture = convertedFixture.Value;
    }

    /// <summary>Provides assigned and converted default-key representations for the owning provider fixture.</summary>
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        yield return new TheoryDataRow<string>("Int");
        yield return new TheoryDataRow<string>("Guid");
        yield return new TheoryDataRow<string>("OperatorFree");
    }

    /// <summary>Insertion and parent queries preserve a present parent whose assigned key is the default.</summary>
    /// <param name="keyType">The assigned scalar key representation.</param>
    /// <returns>A task that completes after verifying root absence and child parent presence separately.</returns>
    [Theory]
    [MemberData(nameof(Cases))]
    public Task InsertAsync_InsertUnderDefaultKey_PreservesParentPresence(
        string keyType
    ) => keyType switch
    {
        "Int" => InsertAsync(
            Engine,
            1,
            IntegerNode,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "Guid" => InsertAsync(
            Engine,
            s_child,
            GuidNodeFor,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "OperatorFree" => InsertAsync(
            Engine,
            new OperatorFreeKey(1),
            ConvertedNode,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(keyType)),
    };

    /// <summary>A move to a default-key root retains a present typed parent and the destination tree.</summary>
    /// <param name="keyType">The assigned scalar key representation.</param>
    /// <returns>A task that completes after verifying both source and destination remain valid.</returns>
    [Theory]
    [MemberData(nameof(Cases))]
    public Task ReparentAsync_ReparentToDefaultKey_PreservesParentPresence(
        string keyType
    ) => keyType switch
    {
        "Int" => ReparentAsync(
            Engine,
            1,
            2,
            IntegerNode,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "Guid" => ReparentAsync(
            Engine,
            s_child,
            s_other,
            GuidNodeFor,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "OperatorFree" => ReparentAsync(
            Engine,
            new OperatorFreeKey(1),
            new OperatorFreeKey(2),
            ConvertedNode,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(keyType)),
    };

    /// <summary>Detaching beneath a default-key root changes parent presence to genuine absence.</summary>
    /// <param name="keyType">The assigned scalar key representation.</param>
    /// <returns>A task that completes after verifying the original and detached roots have no parent.</returns>
    [Theory]
    [MemberData(nameof(Cases))]
    public Task DetachAsync_DetachFromDefaultKey_CreatesAbsentParent(
        string keyType
    ) => keyType switch
    {
        "Int" => DetachAsync(
            Engine,
            1,
            2,
            IntegerNode,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "Guid" => DetachAsync(
            Engine,
            s_child,
            s_other,
            GuidNodeFor,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "OperatorFree" => DetachAsync(
            Engine,
            new OperatorFreeKey(1),
            new OperatorFreeKey(2),
            ConvertedNode,
            node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(keyType)),
    };

    /// <summary>Creates an assigned integer key, including zero, on the existing restrictive-FK model.</summary>
    private static ConstrainedNode IntegerNode(
        int key
    ) => new() { Id = key };

    /// <summary>Creates an assigned Guid key, including Guid.Empty, on the existing Guid model.</summary>
    private static GuidNode GuidNodeFor(
        Guid key
    ) => new() { Id = key };

    /// <summary>Creates the operator-free converted model while reusing the existing provider database owner.</summary>
    private static OperatorFreeNode ConvertedNode(
        OperatorFreeKey key
    ) => new() { Id = key };

    /// <summary>Resets only the model used by this scalar-key representation.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task<DbContext> CreateContextAsync<TEntity>(
        string engine,
        CancellationToken cancellationToken
    )
    {
        if (typeof(TEntity) != typeof(OperatorFreeNode))
        {
            var database = await _fixture.ResetAsync(engine, cancellationToken: cancellationToken);

            return database.CreateContext();
        }

        var context = await _convertedFixture.CreateContextAsync<OperatorFreeContext>(
            engine,
            static options => new OperatorFreeContext(options),
            cancellationToken: cancellationToken);

        await context
            .Set<OperatorFreeNode>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (OperatorFreeKey?)null),
                cancellationToken);

        await context
            .Set<OperatorFreeNode>()
            .ExecuteDeleteAsync(cancellationToken);

        await context.ClearNestedSetTreeRegistriesAsync(cancellationToken);

        return context;
    }
}
