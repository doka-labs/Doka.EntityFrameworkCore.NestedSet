namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>
/// Verifies bounded SQL Server scope identities are rejected before issuing database commands.
/// </summary>
public sealed class ScopeValidationTests : ProviderTest,
    IClassFixture<ProviderFixture<OrderingLockFixture, SqlServerEngine>>
{
    private readonly OrderingLockFixture _fixture;

    /// <summary>Creates cases using typed tree registries and an isolated class-owned database.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    public ScopeValidationTests(
        ProviderFixture<OrderingLockFixture, SqlServerEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Verifies a bounded Scope cannot be truncated into another persisted tree identity.</summary>
    [Fact]
    public async Task OversizedStringScopeIsRejectedBeforeSql()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var key = new string('a', 64);
        var probe = new OrderingLockProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, probe);
        var mapping = Mapping.NestedSetMapping<OrderingLockNode<string, FirstLockHierarchy>, int, string>.For(
            context,
            context.Model.FindEntityType(typeof(OrderingLockNode<string, FirstLockHierarchy>))!);

        // Act
        var error = Assert.Throws<ArgumentException>(() => new Execution.NestedSetTreeLockRequest<Guid, string>(
            mapping.EntityType,
            key + "x",
            Guid.Parse("195d92b7-70a3-47e8-81fb-fb7945277005"),
            Execution.NestedSetTreeLockMode.Existing));

        // Assert
        Assert.Equal("Scope", error.ParamName);
        Assert.Equal("Scope exceeds the mapped maximum length of 64. (Parameter 'Scope')", error.Message);
        Assert.Empty(probe.Commands);
    }
}
