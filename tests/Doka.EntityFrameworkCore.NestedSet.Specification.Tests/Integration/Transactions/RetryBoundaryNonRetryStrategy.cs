namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises EF's active-strategy marker without enabling replay.</summary>
internal sealed class RetryBoundaryNonRetryStrategy : ExecutionStrategy
{
    /// <summary>Creates a genuine EF execution boundary with zero retry attempts.</summary>
    internal RetryBoundaryNonRetryStrategy(
        DbContext context
    ) : base(context, 0, TimeSpan.Zero) { }

    /// <inheritdoc />
    protected override bool ShouldRetryOn(
        Exception exception
    ) => false;
}
