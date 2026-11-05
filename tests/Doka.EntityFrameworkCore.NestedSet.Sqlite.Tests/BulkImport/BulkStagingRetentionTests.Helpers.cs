using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkStagingRetentionTests
{
    /// <summary>Contains failed shadow-assignment input in a completed frame before collection assertions.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<StagingNode> Input, Exception? Error, bool Restored)>
        ImportShadowWithoutRetainingInputAsync(
            ShadowStagingContext context,
            CancellationToken cancellationToken
        )
    {
        var input = new StagingNode
        {
            Id = 1,
            Left = 17,
            Right = 18,
            Version = 91,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(123)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                cancellationToken));

        return (new WeakReference<StagingNode>(input), error, input is { Id: 1, Left: 17, Right: 18, Version: 91 });
    }

    /// <summary>Rejects one shadow assignment while leaving rollback and comparison retries available.</summary>
    private static bool SameShadowScope(
        int left,
        int right
    )
    {
        if (left == 0
            && right == 123
            && Interlocked.Exchange(ref s_rejectShadowComparison, 0) != 0)
        {
            throw new InjectedCommandException();
        }

        return left == right;
    }

    /// <summary>Contains rejected aggregate and foreign references outside the collection assertion frame.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="existing">The existing tracked or stored entity used by the scenario.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<StagingNode> Input, WeakReference<StagingPayload> Payload,
        WeakReference<StagingForeign> Foreign, Exception? Error)> ImportForeignWithoutRetainingInputAsync(
        StagingContext context,
        StagingForeign? existing,
        CancellationToken cancellationToken
    )
    {
        var foreign = existing ?? new StagingForeign { Id = 7 };
        var payload = new StagingPayload { Foreign = foreign };
        var input = new StagingNode
        {
            Id = 1,
            Payload = payload,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                cancellationToken));

        return (new WeakReference<StagingNode>(input), new WeakReference<StagingPayload>(payload),
            new WeakReference<StagingForeign>(foreign), error);
    }

    /// <summary>Keeps the strong input reference outside the caller's completed async frame.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="alternate">Whether the conflict uses an alternate key.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<StagingNode> Input, Exception? Error, bool Restored)>
        ConflictingImportWithoutRetainingInputAsync(
            StagingContext context,
            bool alternate,
            CancellationToken cancellationToken
        )
    {
        var input = new StagingNode
        {
            Id = alternate
                ? 2
                : 1,
            Alias = alternate
                ? "existing"
                : "input",
            Scope = 9,
            ParentId = 999,
            Left = 17,
            Right = 18,
            Depth = 9,
            Position = 11,
            Version = 91,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                cancellationToken));

        var restored = input.Id
            == (alternate
                ? 2
                : 1)
            && input is { Scope: 9, ParentId: 999, Left: 17, Right: 18, Depth: 9, Position: 11, Version: 91 };

        return (new WeakReference<StagingNode>(input), error, restored);
    }

    /// <summary>Keeps root and owned payload references outside the caller's completed async frame.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="getter">Whether the fault originates from the mapped getter.</param>
    /// <param name="tracking">Whether rejected inputs are intentionally already tracked.</param>
    /// <param name="payload">The synthetic owned payload selected by the scenario.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<StagingNode> Input, WeakReference<StagingPayload>? Payload,
        Exception? Error, bool Restored)> ImportWithoutRetainingInputAsync(
        StagingContext context,
        bool getter,
        CancellationToken cancellationToken,
        bool tracking = false,
        bool payload = false
    )
    {
        var input = new StagingNode
        {
            Id = 1,
            Scope = 9,
            ParentId = 999,
            Left = 17,
            Right = 18,
            Depth = 9,
            Position = 11,
            Version = 91,
            FailGetter = getter,
            FailSetter = !getter && !tracking,
            Payload = payload
                ? new StagingPayload { Label = "original" }
                : null,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                cancellationToken));

        var restored = input is { Scope: 9, ParentId: 999, Left: 17, Right: 18, Depth: 9, Position: 11, Version: 91 };

        return (new WeakReference<StagingNode>(input),
            input.Payload is { } detail
                ? new WeakReference<StagingPayload>(detail)
                : null, error, restored);
    }
}
