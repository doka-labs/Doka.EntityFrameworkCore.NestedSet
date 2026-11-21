using Fixture = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyGraphRecoveryTests;
using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;
using TopologyDetails = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyDetails;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyLeaf = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyLeaf;
using TopologyPayload = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyPayload;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures complex-graph capture and native/scalar recovery using the shared model and oracle.</summary>
public class ComplexGraphSnapshotBenchmarks
{
    private ManagedSaveContextOwner<TopologyContext>? _owner;
    private TopologyPayload _payload = null!;
    private EntityEntry<TopologyPayload> _entry = null!;
    private List<TopologyItem> _rows = null!;
    private TopologyItem[] _items = null!;
    private TopologyDetails?[] _details = null!;
    private List<TopologyLeaf>[] _leafLists = null!;
    private TopologyLeaf[][] _leaves = null!;
    private TopologyDetails? _optional;
    private Fixture.ScalarObservation[] _scalars = null!;
    private Fixture.ComplexObservation[] _states = null!;
    private NestedSetTrackerSnapshot? _snapshot;
    private object? _result;
    private EntityState _state;

    /// <summary>Gets or sets one exact former graph-measurement variant.</summary>
    [ParamsSource(nameof(Cases))]
    public ComplexGraphBenchmarkCase Case { get; set; }

    /// <summary>Gets all original complete/native capture sizes and detected-removal recovery sizes.</summary>
    public static IEnumerable<ComplexGraphBenchmarkCase> Cases
    {
        get
        {
            foreach (var rows in new[] { 1, 128, 2_048, 20_000 })
            {
                yield return new ComplexGraphBenchmarkCase("Capture", rows);
                yield return new ComplexGraphBenchmarkCase("NativeCapture", rows);
                yield return new ComplexGraphBenchmarkCase("ExceptionalRestore", rows);
            }

            yield return new ComplexGraphBenchmarkCase("DetectedRestore", 128);
            yield return new ComplexGraphBenchmarkCase("DetectedRestore", 2_048);
        }
    }

    /// <summary>Creates the real graph and, for recovery, stages dirty callback state outside measurement.</summary>
    [IterationSetup]
    public void Setup()
    {
        _owner = ManagedSaveContextOwner<TopologyContext>.Create(static options => new TopologyContext(options), false);
        _payload = Fixture.CreatePayload(Case.Rows);
        _entry = _owner.Context.Attach(_payload);
        _ = _owner.Context.Model.GetRelationalModel();
        Fixture.MarkPending(_entry);
        _rows = _payload.Rows!;
        _items = _rows.ToArray();
        _details = _items.Select(item => item.Details).ToArray();
        _leafLists = _items.Select(item => item.Leaves!).ToArray();
        _leaves = _leafLists.Select(list => list.ToArray()).ToArray();
        _optional = _payload.Optional;
        _scalars = Fixture.ObserveScalars(_entry);
        _states = Fixture.ObserveStates(_entry);
        _state = _entry.State;
        _result = null;
        _snapshot = null;

        if (Case.Operation == "DetectedRestore")
        {
            _snapshot = NestedSetTrackerSnapshot.Capture(_owner.Context);
            _rows.Clear();
            _payload.Optional = null;
            _owner.Context.ChangeTracker.DetectChanges();
        }
        else if (Case.Operation == "ExceptionalRestore")
        {
            _snapshot = NestedSetTrackerSnapshot.Capture(_owner.Context);
            _owner.Context.ChangeTracker.AcceptAllChanges();
            _payload.Optional!.Text = "Callback optional";
            _payload.Optional = null;

            foreach (var item in _items)
            {
                item.Text = "Callback item";
                item.Details = null;
                item.Leaves![0].Text = "Callback leaf";
                item.Leaves.Clear();
            }

            _rows.Clear();
            _payload.Rows = [];
        }
        else if (Case.Operation is not ("Capture" or "NativeCapture"))
        {
            throw new ArgumentException("Unknown complex graph operation.", nameof(Case));
        }
    }

    /// <summary>Executes exactly one selected production operation; BenchmarkDotNet owns all measurements.</summary>
    /// <returns>The actual snapshot retained until its independent iteration oracle runs.</returns>
    [Benchmark]
    public object Execute()
    {
        if (Case.Operation == "Capture")
        {
            return _result = NestedSetTrackerSnapshot.Capture(_owner!.Context);
        }

        if (Case.Operation == "NativeCapture")
        {
            return _result = new NestedSetComplexSnapshot(_entry, NestedSetInsertionContract.Instance.Entry(_entry));
        }

        _snapshot!.Restore();

        return _result = _snapshot;
    }

    /// <summary>Checks exact graph membership, scalar originals and flags before disposing owned services.</summary>
    [IterationCleanup]
    public void Cleanup()
    {
        try
        {
            if (_result is null)
            {
                throw new InvalidOperationException("The selected complex graph operation did not execute.");
            }

            if (_result is NestedSetComplexSnapshot native)
            {
                native.Verify();
            }

            if (!ReferenceEquals(_rows, _payload.Rows)
                || !ReferenceEquals(_optional, _payload.Optional)
                || _rows.Count != Case.Rows
                || _state != _entry.State
                || !_scalars.SequenceEqual(Fixture.ObserveScalars(_entry))
                || !_states.SequenceEqual(Fixture.ObserveStates(_entry)))
            {
                throw new InvalidOperationException("Complex graph state did not match the independent preimage.");
            }

            // WHY: Equal scalar values cannot prove that the caller's exact graph objects survived recovery.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _items.Length; index++)
            {
                if (!ReferenceEquals(_rows[index], _items[index])
                    || !ReferenceEquals(_details[index], _items[index].Details)
                    || !ReferenceEquals(_leafLists[index], _items[index].Leaves)
                    || _leafLists[index].Count != 2
                    || !ReferenceEquals(_leaves[index][0], _leafLists[index][0])
                    || !ReferenceEquals(_leaves[index][1], _leafLists[index][1]))
                {
                    throw new InvalidOperationException("Complex graph reference or ordinal identity changed.");
                }
            }

            if (_owner!.Context.ChangeTracker.Entries().Count() != 1
                || _owner.Context.ChangeTracker.AutoDetectChangesEnabled
                || _owner.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
            {
                throw new InvalidOperationException(
                    "The complex graph workload changed tracker or connection ownership.");
            }
        }
        finally
        {
            _owner?.Dispose();
            _owner = null;
        }
    }
}
