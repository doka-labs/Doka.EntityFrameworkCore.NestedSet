using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkIdentityRetentionTests
{
    /// <summary>Ends both input frames before native retention is observed by the assertion frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<ScalarNode> First, WeakReference<ScalarNode> Second,
        Exception? Error, bool Restored)> ImportTwoRootsAsync(
        IdentityContext context
    )
    {
        var first = new ScalarNode
        {
            Id = 1,
            Left = 71,
            Right = 72,
        };

        var second = new ScalarNode
        {
            Id = 2,
            Left = 81,
            Right = 82,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<ScalarNode>()
            .InsertForestAsync(
                [
                    new NestedSetTreeImport<ScalarNode, Guid>(Guid.Empty, new NestedSetBranch<ScalarNode>(first)),
                    new NestedSetTreeImport<ScalarNode, Guid>(Guid.NewGuid(), new NestedSetBranch<ScalarNode>(second)),
                ],
                CancellationToken.None));

        return (new WeakReference<ScalarNode>(first), new WeakReference<ScalarNode>(second), error,
            first.Id == 1 && second.Id == 2 && first is { Left: 71, Right: 72 } && second is { Left: 81, Right: 82 });
    }

    /// <summary>Contains failed input references in a completed frame instead of the assertion frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<ScalarNode> Input, Exception? Error, bool Restored)> ImportScalarAsync(
        IdentityContext context,
        bool generated
    )
    {
        var node = new ScalarNode
        {
            Id = generated ? 0 : 3,
            Left = 71,
            Right = 72,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<ScalarNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<ScalarNode, Guid>(Guid.Empty, new NestedSetBranch<ScalarNode>(node))],
                CancellationToken.None));

        return (new WeakReference<ScalarNode>(node), error,
            node.Id == (generated ? 0 : 3) && node is { Left: 71, Right: 72 });
    }

    /// <summary>Uses the configured mutable representation while keeping weak references outside JIT roots.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<object> Input, WeakReference<IdentityPayload> Payload,
        Exception? Error, bool Restored)> ImportMutableAsync(
        DbContext context,
        bool binary
    )
    {
        var payload = new IdentityPayload();

        if (binary)
        {
            var node = new BinaryNode
            {
                Id = [3],
                Scope = [8],
                Left = 71,
                Right = 72,
                Payload = payload,
            };

            var error = await Record.ExceptionAsync(() => context
                .NestedSet<BinaryNode>()
                .ForScope<byte[]>([1])
                .InsertForestAsync(
                    [new NestedSetTreeImport<BinaryNode, Guid>(Guid.Empty, new NestedSetBranch<BinaryNode>(node))],
                    CancellationToken.None));

            return (new WeakReference<object>(node), new WeakReference<IdentityPayload>(payload), error,
                node.Id.SequenceEqual(new byte[] { 3 })
                && node.Scope.SequenceEqual(new byte[] { 8 })
                && node is { Left: 71, Right: 72 });
        }

        var mutable = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Scope = new MutableIdentity("original"),
            Left = 71,
            Right = 72,
            Payload = payload,
        };

        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<MutableNode>()
            .ForScope(new MutableIdentity("scope"))
            .InsertForestAsync(
                [new NestedSetTreeImport<MutableNode, Guid>(Guid.Empty, new NestedSetBranch<MutableNode>(mutable))],
                CancellationToken.None));

        return (new WeakReference<object>(mutable), new WeakReference<IdentityPayload>(payload), failure,
            mutable.Id.Value == "node" && mutable.Scope.Value == "original" && mutable is { Left: 71, Right: 72 });
    }

    /// <summary>Collects only after the no-inlining import frame has relinquished its caller-owned inputs.</summary>
    private static void CollectInputs()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
