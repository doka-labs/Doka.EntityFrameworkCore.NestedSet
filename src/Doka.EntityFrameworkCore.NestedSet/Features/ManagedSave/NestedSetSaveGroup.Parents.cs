namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal sealed partial class NestedSetSaveGroup<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <inheritdoc />
    internal override void SuppressParentChanges()
    {
        foreach (var change in _parentChanges)
        {
            var property = change.Entry.Property(_map.Parent);
            property.CurrentValue = change.OriginalParent;
            property.IsModified = false;
        }
    }

    /// <inheritdoc />
    internal override void RestoreParentChanges()
    {
        foreach (var change in _parentChanges)
        {
            var property = change.Entry.Property(_map.Parent);
            property.CurrentValue = change.TargetParent;
            property.IsModified = false;
        }
    }

    /// <inheritdoc />
    internal override async Task PrepareLockedChangesAsync(
        CancellationToken cancellationToken
    )
    {
        await ValidateChangedTreeIdentitiesAsync(cancellationToken)
            .ConfigureAwait(false);

        if (_parentChanges.Length != 0)
        {
            _orderedParentChanges = await OrderParentChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Rejects a pre-lock node-to-tree resolution that no longer belongs to a locked tree.</summary>
    private async Task ValidateChangedTreeIdentitiesAsync(
        CancellationToken cancellationToken
    )
    {
        var capabilities = NestedSetProviderCapabilities.Resolve(_context);

        foreach (var (scope, keys) in _scopes)
        {
            if (!_requestedTrees.TryGetValue(scope, out var lockedTrees))
            {
                throw new DbUpdateConcurrencyException("A hierarchy node changed trees before locking.");
            }

            var source = Nodes();

            if (_map.Scope is not null)
            {
                source = source.Where(MatchesValues(_map.ScopeProperty!, [scope]));
            }

            foreach (var batch in keys.Chunk(NestedSetBatch.MaximumRows))
            {
                var matched =
                    _map.HasNativeKeyEquality
                    && batch.Length > 1
                    && capabilities.SupportsTrackedKeyCollection(_map.KeyProperty)
                        ? capabilities.MatchTrackedKeyCollection(source, _map.KeyProperty, batch)
                        : NestedSetTrackedRowset<TEntity>
                            .Match(source, _map.Key, batch)
                            .Select(row => row.Entity);

                var treeIds = await matched
                    .Select(node => EF.Property<TTreeId>(node, _map.TreeId))
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (treeIds.Length != batch.Length
                    || treeIds.Any(treeId => !lockedTrees.Contains(treeId)))
                {
                    // WHY: A node can change trees between discovery and lock acquisition. Saving its payload
                    // or sorting in the new tree without that tree's registry lock would violate isolation.
                    throw new DbUpdateConcurrencyException("A hierarchy node changed trees before locking.");
                }
            }
        }
    }

    /// <inheritdoc />
    internal override async Task ApplyParentChangesAsync(
        CancellationToken cancellationToken
    )
    {
        if (_parentChanges.Length == 0)
        {
            return;
        }

        var orderedChanges = _orderedParentChanges
            ?? throw new InvalidOperationException("Locked Parent changes were not prepared before the payload save.");

        var tracked = await NestedSetTrackedStructure<TEntity, TKey, TTreeId, TScope>
            .CaptureAsync(_context, _map, _treeRequests, includeModified: true, cancellationToken)
            .ConfigureAwait(false);

        using (NestedSetSaveChanges.EnterManagedMutation(_context))
        {
            foreach (var change in orderedChanges)
            {
                await MoveParentWithinSaveAsync(change, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (tracked is not null)
        {
            await tracked
                .RefreshAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Executes one prepared Parent move with the outer save's locks and rollback boundary.</summary>
    private async Task MoveParentWithinSaveAsync(
        ParentChange change,
        CancellationToken cancellationToken
    )
    {
        var source = Nodes();

        if (_map.Scope is not null)
        {
            source = source.Where(MatchesValues(_map.ScopeProperty!, [change.SourceScope]));
        }

        // WHY: Earlier moves in this save can transfer either endpoint to another already locked tree.
        // Resolve both current identities together, instead of repeating facade pre-lock lookups.
        var endpoints = await NestedSetTrackedRowset<TEntity>
            .Match(source, _map.Key, [change.Key, change.TargetParent])
            .Select(row => new TargetIdentity(row.Ordinal, EF.Property<TTreeId>(row.Entity, _map.TreeId)))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (endpoints.Length != 2
            || endpoints[0].Ordinal == endpoints[1].Ordinal)
        {
            throw new DbUpdateConcurrencyException("A prepared Parent endpoint no longer exists.");
        }

        var sourceTreeId = endpoints.Single(row => row.Ordinal == 0).TreeId;
        var targetTreeId = endpoints.Single(row => row.Ordinal == 1).TreeId;

        if (!_requestedTrees.TryGetValue(change.SourceScope, out var lockedTrees)
            || !lockedTrees.Contains(sourceTreeId)
            || !lockedTrees.Contains(targetTreeId))
        {
            throw new DbUpdateConcurrencyException("A prepared Parent endpoint left the locked trees.");
        }

        var sourceStore = new NestedSetStore<TEntity, TKey, TTreeId, TScope>(
            _context,
            _map.EntityType,
            change.SourceScope,
            sourceTreeId);

        // WHY: TreeId may use a database collation that differs from CLR equality. The exact-tree query
        // determines whether both endpoints share a tree without assuming a string comparer.
        var sameTree = await sourceStore
            .Nodes
            .Where(MatchesValues(_map.KeyProperty, [change.TargetParent]))
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sameTree)
        {
            var placement = new NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>(sourceStore);

            await new NestedSetMove<TEntity, TKey, TTreeId, TScope>(sourceStore, placement)
                .MoveToWithinSaveAsync(change.Key, change.TargetParent, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        await new NestedSetCrossTreeMover<TEntity, TKey, TTreeId, TScope>(_context, _map.EntityType, change.SourceScope)
            .MoveToWithinSaveAsync(change.Key, sourceTreeId, change.TargetParent, targetTreeId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Orders requested moves by their final adjacency dependencies using locked persisted bounds.</summary>
    private async Task<ParentChange[]> OrderParentChangesAsync(
        CancellationToken cancellationToken
    )
    {
        var plans = new List<ParentMovePlan>(_parentChanges.Length);

        foreach (var group in _parentChanges.GroupBy(change => change.SourceScope, _scopes.Comparer))
        {
            var changes = group.ToArray();
            var keys = new TKey[changes.Length * 2];

            for (var index = 0; index < changes.Length; index++)
            {
                keys[index * 2] = changes[index].Key;
                keys[(index * 2) + 1] = changes[index].TargetParent;
            }

            var endpoints = new ParentEndpoint[keys.Length];
            var source = Nodes();

            if (_map.Scope is not null)
            {
                source = source.Where(MatchesValues(_map.ScopeProperty!, [group.Key]));
            }

            for (var offset = 0; offset < keys.Length; offset += NestedSetBatch.MaximumRows)
            {
                var batch = keys
                    .AsSpan(offset, Math.Min(NestedSetBatch.MaximumRows, keys.Length - offset))
                    .ToArray();

                var rows = await NestedSetTrackedRowset<TEntity>
                    .Match(source, _map.Key, batch)
                    .Select(row => new ParentEndpoint(
                        row.Ordinal,
                        EF.Property<TTreeId>(row.Entity, _map.TreeId),
                        EF.Property<long>(row.Entity, _map.Left),
                        EF.Property<long>(row.Entity, _map.Right)))
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var row in rows)
                {
                    var index = offset + row.Ordinal;

                    if (endpoints[index] is not null)
                    {
                        throw new DbUpdateConcurrencyException("A hierarchy endpoint is ambiguous.");
                    }

                    endpoints[index] = row;
                }
            }

            if (!_requestedTrees.TryGetValue(group.Key, out var lockedTrees))
            {
                throw new DbUpdateConcurrencyException("A hierarchy endpoint changed before locking.");
            }

            for (var index = 0; index < changes.Length; index++)
            {
                var origin = endpoints[index * 2];
                var target = endpoints[(index * 2) + 1];

                if (origin is null
                    || target is null
                    || !lockedTrees.Contains(origin.TreeId)
                    || !lockedTrees.Contains(target.TreeId))
                {
                    // WHY: An endpoint moved into an unlocked tree after planning. Never widen the lock set
                    // after acquisition because a new lock could violate the global lock order.
                    throw new DbUpdateConcurrencyException("A hierarchy endpoint changed before locking.");
                }

                plans.Add(new ParentMovePlan(changes[index], group.Key, origin, target));
            }
        }

        var dependencies = new int[plans.Count];
        Array.Fill(dependencies, -1);
        foreach (var scopeGroup in plans
                     .Select((plan, index) => (plan, index))
                     .GroupBy(item => item.plan.Scope, _scopes.Comparer))
        {
            var trees = new Dictionary<TTreeId, ParentTreePlan>(_treeIdComparer);

            foreach (var item in scopeGroup)
            {
                if (!trees.TryGetValue(item.plan.Source.TreeId, out var sourceTree))
                {
                    sourceTree = new ParentTreePlan();
                    trees.Add(item.plan.Source.TreeId, sourceTree);
                }

                sourceTree.Sources.Add(item);

                if (!trees.TryGetValue(item.plan.Target.TreeId, out var targetTree))
                {
                    targetTree = new ParentTreePlan();
                    trees.Add(item.plan.Target.TreeId, targetTree);
                }

                targetTree.Targets.Add(item);
            }

            foreach (var tree in trees.Values)
            {
                // WHY: Cutting the original forest at changed nodes leaves fixed fragments. The nearest
                // changed ancestor containing a destination must move first; otherwise a valid final plan
                // can appear cyclic in an intermediate tree. Sorted interval sweeps bound planning cost.
                var sources = tree
                    .Sources
                    .OrderBy(item => item.plan.Source.Left)
                    .ThenByDescending(item => item.plan.Source.Right)
                    .ToArray();

                var targets = tree
                    .Targets
                    .OrderBy(item => item.plan.Target.Left)
                    .ToArray();

                var stack = new Stack<(ParentMovePlan plan, int index)>();
                var nextSource = 0;

                foreach (var (plan, index) in targets)
                {
                    while (nextSource < sources.Length
                           && sources[nextSource].plan.Source.Left <= plan.Target.Left)
                    {
                        var candidate = sources[nextSource++];

                        while (stack.TryPeek(out var enclosing)
                               && enclosing.plan.Source.Right < candidate.plan.Source.Left)
                        {
                            stack.Pop();
                        }

                        stack.Push(candidate);
                    }

                    while (stack.TryPeek(out var enclosing)
                           && enclosing.plan.Source.Right < plan.Target.Right)
                    {
                        stack.Pop();
                    }

                    if (stack.TryPeek(out var dependency))
                    {
                        dependencies[index] = dependency.index;
                    }
                }
            }
        }

        var states = new byte[plans.Count];
        var ordered = new List<ParentChange>(plans.Count);

        foreach (var start in Enumerable
                     .Range(0, plans.Count)
                     .OrderBy(index => plans[index].Source.Left))
        {
            var pending = new Stack<int>();
            var current = start;

            while (current >= 0
                   && states[current] == 0)
            {
                states[current] = 1;
                pending.Push(current);
                current = dependencies[current];
            }

            if (current >= 0
                && states[current] == 1)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.CycleDetected,
                    "The requested Parent changes create a hierarchy cycle.");
            }

            while (pending.TryPop(out var index))
            {
                states[index] = 2;
                ordered.Add(plans[index].Change);
            }
        }

        return ordered.ToArray();
    }
}
