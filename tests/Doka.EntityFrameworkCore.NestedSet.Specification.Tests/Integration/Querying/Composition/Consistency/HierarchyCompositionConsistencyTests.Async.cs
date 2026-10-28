namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionConsistencyTests
{
    /// <summary>
    ///     One asynchronous terminal executes the entire mixed query without synchronous command fallback.
    /// </summary>
    /// <param name="aggregate">Whether the terminal is a scalar sum instead of a buffered projection.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneAsyncTerminalExecutesOneMixedQuery(
        bool aggregate
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var query = BuildMixedQuery(context, scope);

        // Act
        var value = aggregate
            ? await query.SumAsync(row => row.Amount, CancellationToken.None)
            : (await query.ToArrayAsync(CancellationToken.None)).Sum(row => row.Amount);

        // Assert
        Assert.Equal(30, value);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>A pre-canceled terminal cannot turn an interrupted query into an empty successful result.</summary>
    /// <param name="aggregate">Whether cancellation guards the scalar or buffered terminal.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeExecutionDoesNotProduceAResult(
        bool aggregate
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var query = BuildMixedQuery(context, scope);
        var completed = false;

        // Act
        var error = await Record.ExceptionAsync(async () =>
        {
            if (aggregate)
            {
                await query.SumAsync(row => row.Amount, cancellation.Token);
            }
            else
            {
                await query.ToArrayAsync(cancellation.Token);
            }

            completed = true;
        });

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.False(completed);
        // WHY: EF may call the provider interceptor before the driver checks an already-canceled token.
        // Cancellation promises propagation and no successful result, not zero command dispatch attempts.
        Assert.InRange(probe.CommandCount, 0, 1);

        if (probe.CommandCount != 0)
        {
            // WHY: SqlClient can cancel its returned task without preserving the token on its exception.
            // The actual command boundary establishes forwarding independently of driver exception metadata.
            Assert.Equal(cancellation.Token, probe.ReaderCancellationToken);
        }

        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Cancellation during an actual ReadAsync propagates the caller token without partial success.</summary>
    /// <param name="aggregate">Whether the interrupted reader belongs to an aggregate or buffered terminal.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringReaderDoesNotReturnPartialSuccess(
        bool aggregate
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var setup = await CreateContextAsync(scope, probe);
        using var cancellation = new CancellationTokenSource();
        var observedToken = CancellationToken.None;
        var boundary = new CompositionReadBoundaryProbe(async token =>
        {
            observedToken = token;
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
        }) { Armed = true };

        await using var context = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options),
            probe,
            boundary);

        var query = BuildMixedQuery(context, scope);
        var completed = false;

        // Act
        var error = await Record.ExceptionAsync(async () =>
        {
            if (aggregate)
            {
                await query.SumAsync(row => row.Amount, cancellation.Token);
            }
            else
            {
                await query.ToArrayAsync(cancellation.Token);
            }

            completed = true;
        });

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(cancellation.Token, observedToken);
        Assert.Equal(cancellation.Token, boundary.CommandToken);
        Assert.False(completed);
        Assert.Equal(1, boundary.FirstRows);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
