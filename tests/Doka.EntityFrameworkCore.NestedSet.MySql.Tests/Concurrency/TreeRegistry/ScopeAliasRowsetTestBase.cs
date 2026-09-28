namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Verifies Doka scope alias rowsets using an isolated fixture.</summary>
public abstract class ScopeAliasRowsetTestBase : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the exact provider fixture owning this suite's database.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    protected ScopeAliasRowsetTestBase(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Both rowset forms preserve database collation when detecting duplicate scoped TreeIds.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(256)]
    public async Task RowsetRejectsDatabaseEqualScopeAliases(int count)
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var entityType = context.Model.FindEntityType(typeof(TextNode))!;
        var requests = Enumerable.Range(0, count - 2)
            .Select(_ => new NestedSetTreeLockRequest<Guid, string>(entityType, "other", Guid.NewGuid(),
                NestedSetTreeLockMode.New))
            .ToList();

        var duplicateTreeId = Guid.NewGuid();
        requests.Add(new NestedSetTreeLockRequest<Guid, string>(entityType, "ALPHA", duplicateTreeId,
            NestedSetTreeLockMode.New));
        requests.Add(new NestedSetTreeLockRequest<Guid, string>(entityType, "alpha", duplicateTreeId,
            NestedSetTreeLockMode.New));

        // Act
        var error = await Record.ExceptionAsync(() => NestedSetTreeLocks.RequireDistinctAsync(
            context, requests, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
    }
}
