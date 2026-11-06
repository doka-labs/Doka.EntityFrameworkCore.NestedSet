namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Pre-cancellation rejects before either provider selection or the owned preparation callback.</summary>
    [Fact]
    public async Task CanceledSaveFixturePreparationDoesNotInvokeCallback()
    {
        // Arrange
        await using var fixture = new SaveSemanticsFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var invoked = false;

        // Act
        var failure = await Record.ExceptionAsync(() => fixture.PrepareAsync(
            "Unsupported",
            (_, _, _) =>
            {
                invoked = true;

                return Task.CompletedTask;
            },
            cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.False(invoked);
    }

    /// <summary>Canceled preparation releases its database, preserves token identity and permits a fresh retry.</summary>
    [Fact]
    public async Task SaveFixturePreparationForwardsCancellationAndRecovers()
    {
        // Arrange
        await using var fixture = new SaveSemanticsFixture();
        using var cancellation = new CancellationTokenSource();
        var tokens = new List<CancellationToken>();
        var initializations = new List<bool>();
        Func<TestDatabase, bool, CancellationToken, Task> prepare = async (_, firstUse, token) =>
        {
            tokens.Add(token);
            initializations.Add(firstUse);

            if (tokens.Count == 1)
            {
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            }
        };

        // Act
        var failure = await Record.ExceptionAsync(() => fixture.PrepareAsync(Engine, prepare, cancellation.Token));
        var database = await fixture.PrepareAsync(Engine, prepare, CancellationToken.None);
        await using var context = database.CreateContext();

        // WHY: EF composes the scalar query over a case-sensitive Value column on PostgreSQL.
        var available = await context
            .Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(cancellation.Token, tokens[0]);
        Assert.Equal(CancellationToken.None, tokens[1]);
        Assert.All(initializations, firstUse => Assert.True(firstUse));
        Assert.Equal(1, available);
    }

    /// <summary>A canceled options clone rejects before reading fixture state and retains the original token.</summary>
    [Fact]
    public async Task CanceledOrderingContextCreationDoesNotReadFixtureState()
    {
        // Arrange
        await using var fixture = new OrderingFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var failure = await Record.ExceptionAsync(() => fixture.CreateContextAsync(Engine, cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }
}
