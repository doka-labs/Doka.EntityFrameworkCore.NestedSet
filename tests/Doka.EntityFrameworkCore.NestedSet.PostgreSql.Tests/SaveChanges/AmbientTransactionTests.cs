using System.Transactions;
using TransactionIsolationLevel = System.Transactions.IsolationLevel;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Verifies PostgreSQL ambient save ownership using an isolated fixture.</summary>
public sealed class AmbientTransactionTests : ProviderTest,
    IClassFixture<ProviderFixture<OrderingFixture, PostgreSqlEngine>>
{
    private readonly OrderingFixture _fixture;

    /// <summary>Uses the exact provider fixture owning this suite's database.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    public AmbientTransactionTests(
        ProviderFixture<OrderingFixture, PostgreSqlEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    ///     Allows the PostgreSQL provider to enlist ordinary saves in the application's ambient transaction.
    /// </summary>
    [Fact]
    public async Task UnrelatedSavePreservesAmbientTransactionOwnership()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var probe = new SaveBoundaryProbe();
        var options = SaveBoundaryTestSupport.Options(setup, probe).Options;
        var saved = 0;

        // Act
        using (var transaction = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions
            {
                IsolationLevel = TransactionIsolationLevel.ReadCommitted,
            },
            TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var context = new SaveBoundaryContext(options);
            await context.AddAsync(new OrderingMarker { Id = 1, Value = "ambient" }, CancellationToken.None);
            saved = await context.SaveChangesAsync(CancellationToken.None);
        }

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal(0, probe.LockCommands);
        Assert.Empty(await setup.Set<OrderingMarker>().ToArrayAsync(CancellationToken.None));
    }
}
