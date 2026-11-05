using System.Runtime.CompilerServices;
using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects save guard lifetimes independently of a later save replacing them.</summary>
public sealed class SaveValidationLifetimeTests
{
    // WHY: Subsequent saves overwrite the guard and can hide a retained closure. Inspect the existing typed
    // context state directly so success, failure, cancellation and pool reuse must release that exact guard.
    private static readonly ConditionalWeakTable<DbContext, NestedSetSaveChanges.SaveState> s_states =
        (ConditionalWeakTable<DbContext, NestedSetSaveChanges.SaveState>)typeof(NestedSetSaveChanges)
            .GetField("s_contexts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    /// <summary>Every asynchronous exit restores the exact validation action that preceded the boundary.</summary>
    /// <param name="outcome">The underlying save's success, failure, or cancellation result.</param>
    /// <param name="existingGuard">Whether the boundary replaces an existing action instead of an empty slot.</param>
    [Theory]
    [InlineData("success", false)]
    [InlineData("success", true)]
    [InlineData("failure", false)]
    [InlineData("failure", true)]
    [InlineData("cancellation", false)]
    [InlineData("cancellation", true)]
    public async Task AsyncBoundaryRestoresExactPreviousValidation(
        string outcome,
        bool existingGuard
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        using var stop = new CancellationTokenSource();
        var state = StateFor(context);
        Action? previous = existingGuard ? static () => { } : null;
        state.SaveValidation = previous;

        // Act
        var result = await ObserveBoundaryAsync(context, outcome, stop,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.During);
        Assert.NotSame(previous, result.During);
        Assert.Same(previous, result.After);
        Assert.True(result.ActiveDuring);
        Assert.False(state.Active);
        Assert.Equal(1, result.Calls);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        AssertOutcome(result.Failure, outcome, stop.Token);
    }

    /// <summary>A pooled context cannot carry an expired save-validation closure into its next lease.</summary>
    /// <param name="outcome">The first lease's underlying save result before the context is returned.</param>
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task PooledLeaseDoesNotRetainPreviousValidation(
        string outcome
    )
    {
        // Arrange
        await using var services = new ServiceCollection()
            .AddPooledDbContextFactory<HierarchyChangeDetectionTests.SnapshotContext>(
                options => options.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()),
                poolSize: 1)
            .BuildServiceProvider();

        var factory = services.GetRequiredService<IDbContextFactory<HierarchyChangeDetectionTests.SnapshotContext>>();

        // Act
        var result = await ObservePoolReuseAsync(factory, outcome,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.SameContext);
        Assert.True(result.SameState);
        Assert.NotNull(result.First.During);
        Assert.Null(result.First.After);
        Assert.Null(result.NextLeaseValidation);
        Assert.False(result.NextLeaseActive);
        AssertOutcome(result.First.Failure, outcome, result.CancellationToken);
    }

    /// <summary>Every synchronous exit restores the previous guard and the caller's original detection flag.</summary>
    /// <param name="outcome">The base delegate's success, failure, or cancellation exception.</param>
    /// <param name="existingGuard">Whether the boundary replaces a prior action instead of an empty slot.</param>
    /// <param name="automatic">The caller's flag before the delegate deliberately changes it.</param>
    [Theory]
    [InlineData("success", false, false)]
    [InlineData("success", false, true)]
    [InlineData("success", true, false)]
    [InlineData("success", true, true)]
    [InlineData("failure", false, false)]
    [InlineData("failure", false, true)]
    [InlineData("failure", true, false)]
    [InlineData("failure", true, true)]
    [InlineData("cancellation", false, false)]
    [InlineData("cancellation", false, true)]
    [InlineData("cancellation", true, false)]
    [InlineData("cancellation", true, true)]
    public async Task SynchronousBoundaryRestoresValidationAndDetection(
        string outcome,
        bool existingGuard,
        bool automatic
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        context.ChangeTracker.AutoDetectChangesEnabled = automatic;
        var state = StateFor(context);
        Action? previous = existingGuard ? static () => { } : null;
        state.SaveValidation = previous;

        // Act
        var result = ObserveSynchronousBoundary(context, outcome);

        // Assert
        Assert.NotNull(result.During);
        Assert.NotSame(previous, result.During);
        Assert.Same(previous, result.After);
        Assert.True(result.ActiveDuring);
        Assert.False(state.Active);
        Assert.Equal(automatic, result.DetectionDuring);
        Assert.Equal(automatic, result.DetectionAfter);
        Assert.Equal(1, result.Calls);
        AssertOutcome(result.Failure, outcome, result.CancellationToken);
    }

    /// <summary>The synchronous boundary releases its action before the same context is rented again.</summary>
    /// <param name="outcome">The first lease's base delegate outcome.</param>
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task PooledSynchronousLeaseDoesNotRetainValidationOrChangedDetection(
        string outcome
    )
    {
        // Arrange
        await using var services = new ServiceCollection()
            .AddPooledDbContextFactory<HierarchyChangeDetectionTests.SnapshotContext>(
                options => options.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()),
                poolSize: 1)
            .BuildServiceProvider();

        var factory = services.GetRequiredService<IDbContextFactory<HierarchyChangeDetectionTests.SnapshotContext>>();

        // Act
        var result = await ObserveSynchronousPoolReuseAsync(factory, outcome,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.SameContext);
        Assert.True(result.SameState);
        Assert.NotNull(result.First.During);
        Assert.Null(result.First.After);
        Assert.False(result.First.DetectionAfter);
        Assert.Null(result.NextLeaseValidation);
        Assert.False(result.NextLeaseActive);
        Assert.False(result.NextLeaseDetection);
        AssertOutcome(result.First.Failure, outcome, result.First.CancellationToken);
    }

    /// <summary>Changes the detection setting deliberately so omission of cleanup cannot pass unnoticed.</summary>
    private static SynchronousObservation ObserveSynchronousBoundary(
        HierarchyChangeDetectionTests.SnapshotContext context,
        string outcome
    )
    {
        var state = StateFor(context);
        Action? during = null;
        var active = false;
        var automatic = false;
        var calls = 0;
        var cancellationToken = new CancellationToken(canceled: true);
        // WHY: This intentionally tests a synchronous application override, including a synchronous OCE.
        var failure = Record.Exception(() =>
        {
            NestedSetSaveChanges.ExecuteSynchronous(context,
                () =>
                {
                    calls++;
                    during = state.SaveValidation;
                    active = state.Active;
                    automatic = context.ChangeTracker.AutoDetectChangesEnabled;
                    context.ChangeTracker.AutoDetectChangesEnabled = !automatic;

                    return outcome switch
                    {
                        "success" => 0,
                        "failure" => throw new SaveFailureException(),
                        "cancellation" => throw new OperationCanceledException(cancellationToken),
                        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
                    };
                });
        });

        return new SynchronousObservation(during,
            state.SaveValidation,
            active,
            automatic,
            context.ChangeTracker.AutoDetectChangesEnabled,
            calls,
            failure,
            cancellationToken);
    }

    /// <summary>Observes the returned pool slot before another save can replace a retained validation action.</summary>
    /// <param name="factory">The pooled factory whose lease ownership is observed.</param>
    /// <param name="outcome">The synthetic success, failure or cancellation case.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task<SynchronousPoolObservation> ObserveSynchronousPoolReuseAsync(
        IDbContextFactory<HierarchyChangeDetectionTests.SnapshotContext> factory,
        string outcome,
        CancellationToken cancellationToken
    )
    {
        HierarchyChangeDetectionTests.SnapshotContext initial;
        NestedSetSaveChanges.SaveState originalState;
        SynchronousObservation first;

        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            initial = context;
            originalState = StateFor(context);
            first = ObserveSynchronousBoundary(context, outcome);
        }

        await using var next = await factory.CreateDbContextAsync(cancellationToken);
        var state = StateFor(next);

        return new SynchronousPoolObservation(ReferenceEquals(initial, next),
            ReferenceEquals(originalState, state),
            first,
            state.SaveValidation,
            state.Active,
            next.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Captures the guard while an actual asynchronous delegate suspension is in progress.</summary>
    /// <param name="context">The context used by this fixture operation.</param>
    /// <param name="outcome">The synthetic success, failure or cancellation case.</param>
    /// <param name="stop">The separate source controlling cancellation within the observed save boundary.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task<BoundaryObservation> ObserveBoundaryAsync(
        HierarchyChangeDetectionTests.SnapshotContext context,
        string outcome,
        CancellationTokenSource stop,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: The caller controls fixture entry; stop.Token separately proves the save's exact cancellation identity.
        var state = StateFor(context);
        Action? during = null;
        var active = false;
        var calls = 0;
        var failure = await Record.ExceptionAsync(() => NestedSetSaveChanges.ExecuteAsync(
            context,
            acceptAllChangesOnSuccess: false,
            async token =>
            {
                calls++;
                during = state.SaveValidation;
                active = state.Active;
                await Task.Yield();

                if (outcome == "cancellation")
                {
                    await stop.CancelAsync();
                    token.ThrowIfCancellationRequested();
                }

                return outcome == "failure" ? throw new SaveFailureException() : 0;
            },
            stop.Token));

        return new BoundaryObservation(during, state.SaveValidation, active, calls, failure);
    }

    /// <summary>Returns and rents the same real EF pool slot without running another save that masks leakage.</summary>
    /// <param name="factory">The pooled factory whose lease ownership is observed.</param>
    /// <param name="outcome">The synthetic success, failure or cancellation case.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task<PoolObservation> ObservePoolReuseAsync(
        IDbContextFactory<HierarchyChangeDetectionTests.SnapshotContext> factory,
        string outcome,
        CancellationToken cancellationToken
    )
    {
        using var stop = new CancellationTokenSource();
        HierarchyChangeDetectionTests.SnapshotContext initial;
        NestedSetSaveChanges.SaveState originalState;
        BoundaryObservation first;

        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            initial = context;
            originalState = StateFor(context);
            first = await ObserveBoundaryAsync(context, outcome, stop, cancellationToken: cancellationToken);
        }

        await using var next = await factory.CreateDbContextAsync(cancellationToken);
        var state = StateFor(next);

        return new PoolObservation(ReferenceEquals(initial, next),
            ReferenceEquals(originalState, state),
            first,
            state.SaveValidation,
            state.Active,
            stop.Token);
    }

    /// <summary>Checks the save outcome without hiding cleanup failures behind an expected exception.</summary>
    private static void AssertOutcome(
        Exception? failure,
        string outcome,
        CancellationToken cancellationToken
    )
    {
        switch (outcome)
        {
            case "success":
                Assert.Null(failure);
                break;
            case "failure":
                Assert.IsType<SaveFailureException>(failure);
                break;
            case "cancellation":
                Assert.Equal(cancellationToken,
                    Assert.IsAssignableFrom<OperationCanceledException>(failure).CancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }

    /// <summary>Uses the same context-local state creation rule as the production coordinator.</summary>
    private static NestedSetSaveChanges.SaveState StateFor(
        DbContext context
    ) => s_states.GetValue(context, static _ => new NestedSetSaveChanges.SaveState());

    /// <summary>Distinguishes an injected payload failure from guard or context initialization failures.</summary>
    private sealed class SaveFailureException : Exception;

    /// <summary>Captures both sides of one guard lifetime without retaining the context itself.</summary>
    private sealed record BoundaryObservation(
        Action? During,
        Action? After,
        bool ActiveDuring,
        int Calls,
        Exception? Failure
    );

    /// <summary>Captures lease identity and the untouched validation slot of the reused context.</summary>
    private sealed record PoolObservation(
        bool SameContext,
        bool SameState,
        BoundaryObservation First,
        Action? NextLeaseValidation,
        bool NextLeaseActive,
        CancellationToken CancellationToken
    );

    /// <summary>Captures guard identity, intentional flag tampering, and the result of one sync save.</summary>
    private sealed record SynchronousObservation(
        Action? During,
        Action? After,
        bool ActiveDuring,
        bool DetectionDuring,
        bool DetectionAfter,
        int Calls,
        Exception? Failure,
        CancellationToken CancellationToken
    );

    /// <summary>Captures a synchronous boundary's state before and after a real EF pool lease transition.</summary>
    private sealed record SynchronousPoolObservation(
        bool SameContext,
        bool SameState,
        SynchronousObservation First,
        Action? NextLeaseValidation,
        bool NextLeaseActive,
        bool NextLeaseDetection
    );
}
