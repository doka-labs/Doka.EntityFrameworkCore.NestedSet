using Doka.EntityFrameworkCore.NestedSet.Tests;
using ProbeNode = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeNode;
using ProbeOwner = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeOwner;
using RollbackContext = Doka.EntityFrameworkCore.NestedSet.Tests.UnchangedForeignKeyRollbackTests.RollbackContext;
using RollbackNode = Doka.EntityFrameworkCore.NestedSet.Tests.UnchangedForeignKeyRollbackTests.RollbackNode;
using RollbackOwner = Doka.EntityFrameworkCore.NestedSet.Tests.UnchangedForeignKeyRollbackTests.RollbackOwner;
using SnapshotContext = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.SnapshotContext;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures complete FK capture and endpoint recovery through the actual shared fixture models.</summary>
public class RelationshipSnapshotBenchmarks
{
    private ManagedSaveContextOwner<SnapshotContext>? _probeOwner;
    private ManagedSaveContextOwner<RollbackContext>? _rollbackOwner;
    private IUpdateEntry[] _entries = null!;
    private NestedSetTrackerSnapshot? _snapshot;
    private NestedSetRelationshipSnapshot? _relationships;
    private NestedSetCollectionBaseline? _baseline;
    private Action<IUpdateEntry, INavigation, HashSet<object>?>? _captureKnown;
    private IUpdateEntry? _ownerEntry;
    private INavigation? _navigation;
    private ProbeOwner? _scopeOwner;
    private ProbeNode? _newMember;
    private ProbeNode[] _probeMembers = null!;
    private RollbackOwner? _owner;
    private RollbackOwner? _transient;
    private RollbackNode? _node;
    private RollbackNode[] _members = null!;
    private RollbackNode[] _expectedOrder = null!;
    private object? _result;

    /// <summary>Gets or sets one complete former measurement case.</summary>
    [ParamsSource(nameof(Cases))]
    public RelationshipSnapshotBenchmarkCase Case { get; set; }

    /// <summary>Gets every former owner width, selected-entry, cold-map and skipped-write variant.</summary>
    public static IEnumerable<RelationshipSnapshotBenchmarkCase> Cases
    {
        get
        {
            foreach (var nodes in new[] { 1, 128, 2_048, 20_000 })
            {
                yield return new("Capture", nodes, false, true, false);
                yield return new("Capture", nodes, true, true, false);
                yield return new("SupplementalRestore", nodes, false, false, false);
            }

            foreach (var nodes in new[] { 128, 2_048, 20_000 })
            {
                yield return new("Capture", nodes, false, false, false);
            }

            yield return new("NativeRestore", 128, true, false, false);
            yield return new("NativeRestore", 20_000, true, false, false);
            yield return new("MembershipDelta", 1, false, false, false);
            yield return new("MembershipDelta", 8_192, false, false, false);
            yield return new("KnownOwner", 1, false, false, false);

            foreach (var nodes in new[] { 1, 128, 8_000 })
            {
                yield return new("SkippedWrite", nodes, false, false, false);
                yield return new("SkippedWrite", nodes, false, false, true);
            }
        }
    }

    /// <summary>Recreates mutation preimages before each single measured operation.</summary>
    [IterationSetup]
    public void Setup()
    {
        _result = null;
        _snapshot = null;
        _relationships = null;
        _baseline = null;
        _captureKnown = null;

        if (Case.Operation is "NativeRestore" or "SupplementalRestore")
        {
            PrepareRecovery();

            return;
        }

        _probeOwner = ManagedSaveContextOwner<SnapshotContext>.Create(
            static options => new SnapshotContext(options),
            true);
        var context = _probeOwner.Context;

        if (Case.Operation == "SkippedWrite")
        {
            PrepareSkippedWrites(context);

            return;
        }

        _scopeOwner = new ProbeOwner { Id = 7 };
        _probeMembers = Enumerable.Range(1, Case.Nodes).Select(id =>
        {
            var node = HierarchyChangeDetectionTests.CreateNode(id);
            node.TreeId = Guid.NewGuid();

            return node;
        }).ToArray();

        foreach (var node in _probeMembers)
        {
            _scopeOwner.ScopeNodes.Add(node);
        }

        context.Attach(_scopeOwner);
        _ownerEntry = Entry(context, _scopeOwner);
        _navigation = _ownerEntry.EntityType.FindNavigation(nameof(ProbeOwner.ScopeNodes))!;
        var tracked = context.GetService<IUpdateAdapterFactory>().Create().Entries.ToArray();
        _entries = Case.AllEntries
            ? tracked
            : tracked.Where(entry => ReferenceEquals(entry.ToEntityEntry().Entity, _probeMembers[0])).ToArray();

        if (Case.Operation == "Capture")
        {
            if (Case.WarmNativeMaps)
            {
                _ = NestedSetTrackerSnapshot.Capture(context, _entries);
            }
        }
        else if (Case.Operation == "MembershipDelta")
        {
            _snapshot = NestedSetTrackerSnapshot.Capture(context, []);
            _newMember = HierarchyChangeDetectionTests.CreateNode(Case.Nodes + 1);
            _newMember.TreeId = Guid.NewGuid();
            _scopeOwner.ScopeNodes.Add(_newMember);
            context.Attach(_newMember);
        }
        else if (Case.Operation == "KnownOwner")
        {
            NestedSetCollectionBaseline.CaptureBeforeTracking(context.Entry(_probeMembers[0]), ref _baseline);
            _captureKnown = typeof(NestedSetCollectionBaseline).GetMethod(
                    "Capture",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<IUpdateEntry, INavigation, HashSet<object>?>>(_baseline);
        }
        else
        {
            throw new ArgumentException("Unknown relationship snapshot operation.", nameof(Case));
        }
    }

    /// <summary>Runs one actual capture, recovery, persisted-write or membership-journal operation.</summary>
    /// <returns>The real snapshot or capture result retained until validation.</returns>
    [Benchmark]
    public object? Execute()
    {
        switch (Case.Operation)
        {
            case "Capture":
                return _result = NestedSetTrackerSnapshot.Capture(_probeOwner!.Context, _entries);
            case "SkippedWrite":
                return _result = NestedSetCollectionBaseline.CapturePersisted(_entries);
            case "KnownOwner":
                _captureKnown!(_ownerEntry!, _navigation!, null);

                return _result = _baseline;
            case "MembershipDelta":
                _snapshot!.RecordPersistedAddition(_ownerEntry!, _navigation!, _newMember!);

                return _result = _snapshot;
            case "NativeRestore":
                _relationships!.RestoreSnapshots();
                _relationships.Verify();

                return _result = _relationships;
            case "SupplementalRestore":
                _snapshot!.Restore();

                return _result = _snapshot;
            default:
                throw new InvalidOperationException("Unknown relationship snapshot operation.");
        }
    }

    /// <summary>Validates exact native/CLR membership, flags and connection ownership before disposal.</summary>
    [IterationCleanup]
    public void Cleanup()
    {
        try
        {
            if (Case.Operation == "SkippedWrite")
            {
                if (_result is not null
                    || _entries.Length != Case.Nodes
                    || _entries.Any(entry => entry.EntityState != (Case.Hierarchy
                        ? EntityState.Added
                        : EntityState.Modified)))
                {
                    throw new InvalidOperationException("Skipped writes changed their original native write set.");
                }
            }
            else if (Case.Operation is "NativeRestore" or "SupplementalRestore")
            {
                ValidateRecovery();
            }
            else if (Case.Operation == "MembershipDelta")
            {
                NestedSetInsertionContract.Instance.AddCollectionSnapshot(_ownerEntry!, _navigation!, _newMember!);
                _snapshot!.Restore();
                var native = (ISet<object>)_ownerEntry!.GetRelationshipSnapshotValue(_navigation!)!;

                if (_result is null
                    || native.Count != Case.Nodes
                    || native.Contains(_newMember!)
                    || _probeMembers.Any(node => !native.Contains(node))
                    || _scopeOwner!.ScopeNodes.Count != Case.Nodes + 1
                    || _ownerEntry.EntityState != EntityState.Unchanged)
                {
                    throw new InvalidOperationException(
                        "Native membership delta changed an unrelated original member.");
                }
            }
            else if (_result is null
                || _scopeOwner!.ScopeNodes.Count != Case.Nodes
                || !_scopeOwner.ScopeNodes.SequenceEqual(_probeMembers, ReferenceEqualityComparer.Instance))
            {
                throw new InvalidOperationException("Relationship capture changed caller-owned membership.");
            }

            var context = (DbContext?)_probeOwner?.Context ?? _rollbackOwner!.Context;

            if (context.ChangeTracker.AutoDetectChangesEnabled
                || context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
            {
                throw new InvalidOperationException("Relationship workload changed detection or connection ownership.");
            }
        }
        finally
        {
            _probeOwner?.Dispose();
            _probeOwner = null;
            _rollbackOwner?.Dispose();
            _rollbackOwner = null;
        }
    }

    /// <summary>Preserves Added hierarchy and Modified ordinary-owner inputs without relationship proof.</summary>
    private void PrepareSkippedWrites(
        SnapshotContext context
    )
    {
        if (Case.Hierarchy)
        {
            context.AddRange(Enumerable.Range(1, Case.Nodes).Select(HierarchyChangeDetectionTests.CreateNode));
        }
        else
        {
            var owners = Enumerable.Range(1, Case.Nodes).Select(id => new ProbeOwner { Id = id }).ToArray();
            context.AttachRange(owners);

            foreach (var owner in owners)
            {
                context.Entry(owner).Property(value => value.Payload).CurrentValue = "written payload";
            }
        }

        _entries = context.GetService<IUpdateAdapterFactory>().Create().Entries.ToArray();
    }

    /// <summary>Stages the exact native round-trip or supplemental accepted write before recovery.</summary>
    private void PrepareRecovery()
    {
        _rollbackOwner = ManagedSaveContextOwner<RollbackContext>.Create(
            static options => new RollbackContext(options),
            true);
        var context = _rollbackOwner.Context;
        _owner = new RollbackOwner { Id = 2 };
        _transient = new RollbackOwner { Id = 1 };
        _members = Enumerable.Range(1, Case.Nodes).Select(id => new RollbackNode
        {
            Id = id,
            OwnerId = _owner.Id,
            Owner = _owner,
        }).ToArray();

        _owner.Nodes.AddRange(_members);
        context.AttachRange(_owner, _transient);
        _node = _members[Case.Nodes / 2];
        var entry = context.Entry(_node);
        _ownerEntry = Entry(context, _owner);
        _navigation = entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!.Inverse!;
        _entries = context.GetService<IUpdateAdapterFactory>().Create().Entries.ToArray();

        if (Case.Operation == "NativeRestore")
        {
            foreach (var candidate in _entries)
            {
                NestedSetRelationshipSnapshot.Capture(context, candidate, ref _relationships);
            }

            _relationships!.CaptureParentNavigations();
            _owner.Nodes.Add(_node);
            entry.Property(value => value.OwnerId).CurrentValue = _transient.Id;
            entry.Property(value => value.OwnerId).CurrentValue = _owner.Id;
            entry.Property(value => value.OwnerId).IsModified = false;
            _expectedOrder = _owner.Nodes.ToArray();
            _relationships.PrepareRestore();
        }
        else
        {
            _node.Name = "Pending name";
            entry.Property(value => value.Name).IsModified = true;
            _expectedOrder = _owner.Nodes.ToArray();
            _snapshot = NestedSetTrackerSnapshot.Capture(context, [Entry(context, _node)]);
            entry.Property(value => value.OwnerId).CurrentValue = _transient.Id;
            _node.OwnerId = _owner.Id;
            entry.Property(value => value.OwnerId).IsModified = true;
            _owner.Nodes.Add(_node);
            _snapshot.RecordPersistedAddition(_ownerEntry, _navigation, _node);
            NestedSetInsertionContract.Instance.AddCollectionSnapshot(_ownerEntry, _navigation, _node);
        }
    }

    /// <summary>Checks the independent ordered CLR and reference-set native recovery contracts.</summary>
    private void ValidateRecovery()
    {
        var context = _rollbackOwner!.Context;
        var entry = context.Entry(_node!);
        var native = (ISet<object>)_ownerEntry!.GetRelationshipSnapshotValue(_navigation!)!;

        if (_result is null
            || !_owner!.Nodes.SequenceEqual(_expectedOrder, ReferenceEqualityComparer.Instance)
            || !native.SetEquals(_members)
            || _node!.OwnerId != _owner.Id
            || !ReferenceEquals(_node.Owner, _owner)
            || entry.Property(value => value.OwnerId).IsModified
            || _transient!.Nodes.Count != 0
            || (Case.Operation == "SupplementalRestore" && !entry.Property(value => value.Name).IsModified))
        {
            throw new InvalidOperationException(
                "Relationship recovery did not preserve the original endpoint contract.");
        }
    }

    /// <summary>Reads the existing EF native update identity without an implicit detection pass.</summary>
    private static IUpdateEntry Entry(
        DbContext context,
        object entity
    ) => NestedSetInsertionContract.Instance.Entry(context.Entry(entity));
}
