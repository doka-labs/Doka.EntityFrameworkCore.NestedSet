namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Supplies a deterministic retry strategy that exercises EF's actual save retry boundary.</summary>
public sealed class SaveBoundaryRetryFactory : IExecutionStrategyFactory
{
    private readonly ExecutionStrategyDependencies _dependencies;

    /// <summary>Creates the scoped factory with EF's ordinary execution services.</summary>
    /// <param name="dependencies">The provider-independent services required by the execution strategy.</param>
    public SaveBoundaryRetryFactory(
        ExecutionStrategyDependencies dependencies
    )
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public IExecutionStrategy Create() => new SaveBoundaryRetry(_dependencies);

    /// <summary>Retries only the test's single injected failure and never hides unrelated database errors.</summary>
    private sealed class SaveBoundaryRetry : ExecutionStrategy
    {
        /// <summary>Creates a retry strategy with one immediate retry and no sleep-based coordination.</summary>
        /// <param name="dependencies">The real context and diagnostic services.</param>
        internal SaveBoundaryRetry(
            ExecutionStrategyDependencies dependencies
        ) : base(dependencies, 1, TimeSpan.Zero) { }

        /// <inheritdoc />
        protected override bool ShouldRetryOn(
            Exception exception
        ) => exception is SaveBoundaryTransientException;
    }
}
