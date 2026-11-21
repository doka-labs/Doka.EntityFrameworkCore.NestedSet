namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class UserSupervisorCommandTests
{
    /// <summary>Requests cancellation at a deterministic output boundary before database preparation.</summary>
    private sealed class CancelOnWriteWriter : StringWriter
    {
        private readonly CancellationTokenSource _cancellation;

        /// <summary>Retains a source owned and disposed by the enclosing test.</summary>
        internal CancelOnWriteWriter(
            CancellationTokenSource cancellation
        )
        {
            _cancellation = cancellation;
        }

        /// <summary>Cancels the invocation only after it has entered ordinary forward output.</summary>
        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: TextWriter requires a synchronous notification boundary here; no timing decides cancellation.
            // ReSharper disable once MethodHasAsyncOverload
            _cancellation.Cancel();

            return Task.CompletedTask;
        }
    }

    /// <summary>Fails an actual asynchronous writer boundary with a test-owned error identity.</summary>
    private sealed class ThrowOnWriteWriter : StringWriter
    {
        private readonly Exception _failure;

        /// <summary>Retains the exact error that both synchronous and awaited exception checks observe.</summary>
        internal ThrowOnWriteWriter(
            Exception failure
        )
        {
            _failure = failure;
        }

        /// <summary>Exposes a deterministic asynchronous sink failure independently of host IO speed.</summary>
        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default
        ) => Task.FromException(_failure);
    }
}
