using Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;
using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Doka.EntityFrameworkCore.NestedSet.Shared.Structural;
using Doka.EntityFrameworkCore.NestedSet.Storage;
using Doka.EntityFrameworkCore.NestedSet.Tests;
using ProbeNode = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeNode;
using ProbeOwner = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeOwner;
using SnapshotContext = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.SnapshotContext;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures one prepared-import guard at each actual bounded native wave without timing EF setup.</summary>
public class PreparedImportGuardBenchmarks
{
    private const int BatchSize = 64;
    private ManagedSaveContextOwner<SnapshotContext>? _owner;
    private NestedSetSaveChanges.ManagedSaveScope? _managed;
    private IDisposable? _import;
    private ProbeOwner _principal = null!;
    private ProbeNode[] _nodes = null!;
    private IUpdateEntry _ownerEntry = null!;
    private INavigation _navigation = null!;
    private int _tracked;
    private bool _changed;
    private bool _executed;

    /// <summary>Gets or sets an exact wave or completed-snapshot case.</summary>
    [ParamsSource(nameof(Cases))]
    public PreparedImportGuardBenchmarkCase Case { get; set; }

    /// <summary>Gets every original 64-node wave and both independent completed-snapshot sizes.</summary>
    public static IEnumerable<PreparedImportGuardBenchmarkCase> Cases
    {
        get
        {
            foreach (var nodes in new[] { 1_000, 2_000, 4_000, 8_000 })
            {
                // WHY: Each former measured wave becomes its own BDN case; tracking and prefix advancement
                // stay outside the measured operation rather than recreating a handwritten sampling loop.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var offset = 0; offset < nodes; offset += BatchSize)
                {
                    yield return new PreparedImportGuardBenchmarkCase(nodes, offset, false);
                }
            }

            yield return new PreparedImportGuardBenchmarkCase(1_000, 1_000 - BatchSize, true);
            yield return new PreparedImportGuardBenchmarkCase(8_000, 8_000 - BatchSize, true);
        }
    }

    /// <summary>Creates the validated full plan, exact detached prefix and one active native wave.</summary>
    [GlobalSetup]
    public void Setup()
    {
        if (Case.Nodes <= 0 || Case.Offset < 0 || Case.Offset >= Case.Nodes)
        {
            throw new ArgumentException("Prepared import wave lies outside its complete collection.", nameof(Case));
        }

        _owner = ManagedSaveContextOwner<SnapshotContext>.Create(static options => new SnapshotContext(options), true);
        var context = _owner.Context;
        _principal = new ProbeOwner { Id = 7 };
        var entry = context.Entry(_principal);
        entry.State = EntityState.Unchanged;
        _ownerEntry = NestedSetInsertionContract.Instance.Entry(entry);
        _nodes = Enumerable.Range(1, Case.Nodes).Select(HierarchyChangeDetectionTests.CreateNode).ToArray();
        var metadata = context.Model.FindEntityType(typeof(ProbeNode))!;
        var store = new NestedSetStore<ProbeNode, int, Guid, int>(context, metadata, _principal.Id, _nodes[0].TreeId);
        var root = new NestedSetBranch<ProbeNode>(
            _nodes[0],
            _nodes.Skip(1).Select(node => new NestedSetBranch<ProbeNode>(node)).ToArray());

        var plan = new NestedSetBulkPlan<ProbeNode, int, Guid, int>(
            store,
            [root],
            new HashSet<ProbeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        var destination = new NestedSetPlacementResolver<ProbeNode, int, Guid, int>.Destination(1, default, 0, 0);
        plan.RequireCapacity(destination);
        plan.PrepareGeometry(destination, null, CancellationToken.None);
        var inputs = NestedSetBulkPlan<ProbeNode, int, Guid, int>.CreateImportInputs([plan]);

        foreach (var node in _nodes)
        {
            _principal.ScopeNodes.Add(node);
        }

        _navigation = metadata.FindNavigation(nameof(ProbeNode.ScopeOwner))!.Inverse!;
        _managed = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });
        _import = NestedSetSaveChanges.EnterImportInputs(context, inputs);
        var completed = Case.CompleteSnapshot ? Case.Nodes : Case.Offset;

        // WHY: Completed detached rows remain native reference-set members while future inputs are CLR-only.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < completed; index++)
        {
            NestedSetInsertionContract.Instance.AddCollectionSnapshot(_ownerEntry, _navigation, _nodes[index]);
        }

        _tracked = Math.Min(BatchSize, Case.Nodes - Case.Offset);

        // WHY: Entry.State tracks only the selected wave; Attach would traverse the entire prepared owner graph.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = Case.Offset; index < Case.Offset + _tracked; index++)
        {
            context.Entry(_nodes[index]).State = EntityState.Unchanged;
        }

        _changed = false;
        _executed = false;
    }

    /// <summary>Executes the real synchronous import guard once for this exact prefix and wave.</summary>
    /// <returns>Whether an unplanned hierarchy mutation was found.</returns>
    [Benchmark]
    public bool Probe()
    {
        _changed = NestedSetSaveChanges.HasHierarchyChanges(_owner!.Context);
        _executed = true;

        return _changed;
    }

    /// <summary>Checks complete authorization, bounded native membership and detection/connection state.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (!_executed
                || _changed
                || _principal.ScopeNodes.Count != Case.Nodes
                || _owner!.Context.ChangeTracker.Entries().Count() != _tracked + 1
                || _owner.Context.ChangeTracker.AutoDetectChangesEnabled
                || _owner.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
            {
                throw new InvalidOperationException("Prepared-import guard changed its validated wave contract.");
            }
        }
        finally
        {
            try
            {
                _import?.Dispose();
            }
            finally
            {
                try
                {
                    _managed?.Dispose();
                }
                finally
                {
                    _owner?.Dispose();
                    _owner = null;
                }
            }
        }
    }
}
