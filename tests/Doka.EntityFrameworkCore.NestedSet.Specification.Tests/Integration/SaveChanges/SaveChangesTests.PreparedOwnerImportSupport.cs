namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Prepares all detached operation inputs in one owner collection without requesting detection.</summary>
    private static async Task<PreparedOwnerGraph> PrepareOwnerImportAsync(
        DbContext context,
        int nodes,
        CancellationToken cancellationToken
    )
    {
        var owners = await context
            .Set<NavigationScopeOwner>()
            .OrderBy(owner => owner.Id)
            .ToArrayAsync(cancellationToken);

        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(cancellationToken);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var noteValue = context
            .Entry(note)
            .Property(value => value.Value);

        var inputs = Enumerable
            .Range(1000, nodes)
            .Select(id => new NavigationNode
            {
                Id = id,
                Scope = owners[0].Id,
                Name = "Imported " + id.ToString("D4", CultureInfo.InvariantCulture),
            })
            .ToArray();

        owners[0]
            .Nodes
            .AddRange(inputs);

        return new PreparedOwnerGraph(owners[0], owners[1], noteValue, inputs);
    }

    /// <summary>Builds one wide subtree or two tree plans from the same exact detached input objects.</summary>
    private static Task InsertPreparedOwnerImportAsync(
        DbContext context,
        string shape,
        NavigationNode[] inputs,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<NavigationNode>()
            .ForScope(1);

        return shape switch
        {
            "subtree" => tree.InsertSubtreeAsync(
                PreparedOwnerBranch(inputs, 0, inputs.Length),
                100,
                cancellationToken),
            "forest" => tree.InsertForestAsync(
                [
                    new NestedSetTreeImport<NavigationNode, Guid>(
                        s_manualFirstImportTree,
                        PreparedOwnerBranch(inputs, 0, inputs.Length / 2)),
                    new NestedSetTreeImport<NavigationNode, Guid>(
                        s_manualSecondImportTree,
                        PreparedOwnerBranch(inputs, inputs.Length / 2, inputs.Length / 2)),
                ],
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    /// <summary>Describes topology separately from caller-owned CLR navigation collections.</summary>
    private static NestedSetBranch<NavigationNode> PreparedOwnerBranch(
        NavigationNode[] inputs,
        int offset,
        int count
    )
        => new(
            inputs[offset],
            inputs
                .Skip(offset + 1)
                .Take(count - 1)
                .Select(input => new NestedSetBranch<NavigationNode>(input))
                .ToArray());

    /// <summary>Captures detached inputs without creating EF entries or normalizing their CLR graph.</summary>
    private static PreparedOwnerInputState[] CapturePreparedOwnerInputs(
        IEnumerable<NavigationNode> inputs
    )
        => inputs
            .Select(input => new PreparedOwnerInputState(
                input.Id,
                input.Scope,
                input.TreeId,
                input.ParentId,
                input.Left,
                input.Right,
                input.Depth,
                input.Position,
                input.Name,
                input.Parent,
                input.ScopeOwner,
                input.PayloadOwnerId,
                input.PayloadOwner,
                input.Children))
            .ToArray();

    /// <summary>Reads exact stored structure and validates every remaining tree from a separate context.</summary>
    private static async Task<PreparedOwnerStoreState> ReadPreparedOwnerStoreAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: cancellationToken);

        var nodes = await verification
            .Set<NavigationNode>()
            .OrderBy(node => node.Id)
            .Select(node => new PreparedOwnerStoredNode(
                node.Id,
                node.Scope,
                node.TreeId,
                node.ParentId,
                node.Left,
                node.Right,
                node.Depth,
                node.Position,
                node.Name))
            .ToArrayAsync(cancellationToken);

        var note = await verification
            .Set<NavigationNote>()
            .Select(value => value.Value)
            .SingleAsync(cancellationToken);

        var issues = 0;

        foreach (var identity in nodes
            .Select(node => (node.Scope, node.TreeId))
            .Distinct())
        {
            var validation = await verification
                .NestedSet<NavigationNode>()
                .ForScope(identity.Scope)
                .InTree(identity.TreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

            issues += validation.Issues.Count;
        }

        return new PreparedOwnerStoreState(nodes, note, issues);
    }

    /// <summary>Retains only caller-visible inputs and an already acquired ordinary payload entry.</summary>
    private sealed record PreparedOwnerGraph(
        NavigationScopeOwner Owner,
        NavigationScopeOwner OtherOwner,
        PropertyEntry<NavigationNote, string> NoteValue,
        NavigationNode[] Inputs
    );

    /// <summary>Copies every mutable detached scalar and preserves each original navigation object reference.</summary>
    private sealed record PreparedOwnerInputState(
        int Id,
        int Scope,
        Guid TreeId,
        int? ParentId,
        long Left,
        long Right,
        int Depth,
        long Position,
        string Name,
        NavigationNode? Parent,
        NavigationScopeOwner? ScopeOwner,
        int? PayloadOwnerId,
        NavigationPayloadOwner? PayloadOwner,
        List<NavigationNode> Children
    );

    /// <summary>Represents a complete persisted row independently of tracked entity fixup.</summary>
    private sealed record PreparedOwnerStoredNode(
        int Id,
        int Scope,
        Guid TreeId,
        int? ParentId,
        long Left,
        long Right,
        int Depth,
        long Position,
        string Name
    );

    /// <summary>Holds detached database evidence for exact rollback and full-validation assertions.</summary>
    private sealed record PreparedOwnerStoreState(
        PreparedOwnerStoredNode[] Nodes,
        string Note,
        int Issues
    );

    /// <summary>Fails after the second native batch has completed, then permits an identical retry.</summary>
    private sealed class PreparedOwnerBatchFailure : SaveChangesInterceptor
    {
        /// <summary>Gets the exact callback failure expected after the first batch's completed writes.</summary>
        internal Exception Error { get; } = new InvalidOperationException("Injected second import batch failure.");

        /// <summary>Gets the number of native save completions reached by this context.</summary>
        internal int CompletedSaves { get; private set; }

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            CompletedSaves++;

            if (CompletedSaves == 2)
            {
                throw Error;
            }

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Cancels between the completed first batch and the next staging wave.</summary>
    private sealed class PreparedOwnerBatchCancellation(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        /// <summary>Gets the native completion count at the cancellation boundary.</summary>
        internal int CompletedSaves { get; private set; }

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            CompletedSaves++;
            cancellation.Cancel();

            return ValueTask.FromResult(result);
        }
    }
}
