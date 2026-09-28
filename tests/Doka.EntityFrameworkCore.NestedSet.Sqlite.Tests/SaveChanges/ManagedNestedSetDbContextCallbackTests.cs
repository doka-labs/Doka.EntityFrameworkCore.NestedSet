namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Checks post-base tracker inspection through the optional nested-set context base class.</summary>
public sealed class ManagedNestedSetDbContextCallbackTests : ProviderTest,
    IClassFixture<ProviderFixture<BulkGeneratedFixture, SqliteEngine>>
{
    private readonly BulkGeneratedFixture _fixture;

    /// <summary>Uses the generated-key fixture's real SQLite mapping and save override.</summary>
    /// <param name="fixture">The SQLite fixture owning the generated-key database resource.</param>
    public ManagedNestedSetDbContextCallbackTests(
        ProviderFixture<BulkGeneratedFixture, SqliteEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>An override may inspect accepted entries after its nested-set base save returns.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostBaseTrackerInspectionDoesNotRejectManagedInsert(
        bool bulk
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var observed = 0;
        context.AfterBaseSave = current => observed = current
            .ChangeTracker
            .Entries<BulkManualNode>()
            .Count();

        var root = new BulkManualNode();
        var hierarchy = context
            .NestedSet<BulkManualNode>()
            .ForScope(1);

        // Act
        if (bulk)
        {
            await hierarchy.InsertForestAsync(
                [new NestedSetTreeImport<BulkManualNode, Guid>(Guid.Empty, new NestedSetBranch<BulkManualNode>(root)),],
                CancellationToken.None);
        }
        else
        {
            await hierarchy.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        }

        // Assert
        Assert.Equal(1, observed);
        Assert.Equal(
            1,
            await context
                .Set<BulkManualNode>()
                .CountAsync(CancellationToken.None));
    }
}
