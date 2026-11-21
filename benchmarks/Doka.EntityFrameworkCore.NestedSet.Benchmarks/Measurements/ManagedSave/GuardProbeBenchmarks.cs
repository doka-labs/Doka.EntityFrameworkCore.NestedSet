using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Doka.EntityFrameworkCore.NestedSet.Tests;
using SnapshotContext = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.SnapshotContext;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures one real non-mutating hierarchy guard operation with independent correctness checks.</summary>
public class GuardProbeBenchmarks
{
    private ManagedSaveContextOwner<SnapshotContext>? _owner;
    private object? _plan;
    private IUpdateEntry? _entry;
    private IProperty? _property;
    private bool _changed;
    private int _expectedEntries;

    /// <summary>Gets or sets one complete original guard workload.</summary>
    [ParamsSource(nameof(Cases))]
    public GuardProbeBenchmarkCase Case { get; set; }

    /// <summary>Gets the original plan, clean scalar and populated-reference variants.</summary>
    public static IEnumerable<GuardProbeBenchmarkCase> Cases =>
    [
        new("Plan", 1024), new("Unchanged", 1), new("Unchanged", 128),
        new("Populated", 1), new("Populated", 64), new("Scalar", 1),
    ];

    /// <summary>Builds actual EF snapshots and binds metadata before measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _owner = ManagedSaveContextOwner<SnapshotContext>.Create(static options => new SnapshotContext(options), true);
        var context = _owner.Context;

        if (Case.Operation == "Populated")
        {
            // WHY: Independent tree identities prevent parent pairs from collapsing into one native relationship.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < Case.Nodes; index++)
            {
                var parent = HierarchyChangeDetectionTests.CreateNode(index * 2 + 1);
                parent.TreeId = Guid.NewGuid();
                parent.Right = 4;
                var child = HierarchyChangeDetectionTests.CreateNode(index * 2 + 2);
                child.TreeId = parent.TreeId;
                child.ParentId = parent.Id;
                child.Parent = parent;
                child.Left = 2;
                child.Right = 3;
                child.Depth = 1;
                parent.Children!.Add(child);
                context.Attach(parent);
            }

            _expectedEntries = Case.Nodes * 2;
        }
        else
        {
            var nodes = Enumerable.Range(1, Case.Nodes).Select(HierarchyChangeDetectionTests.CreateNode).ToArray();
            context.AttachRange(nodes);
            _expectedEntries = Case.Nodes;

            if (Case.Operation == "Plan")
            {
                var entry = context.Entry(nodes[0]);
                entry.Property(value => value.Name).IsModified = true;
                _plan = HierarchyChangeDetectionTests.CapturePlan([entry]);
            }
            else if (Case.Operation == "Scalar")
            {
                _entry = NestedSetInsertionContract.Instance.Entry(context.Entry(nodes[0]));
                _property = _entry.EntityType.FindProperty(nameof(HierarchyChangeDetectionTests.ProbeNode.Left));
            }
            else if (Case.Operation != "Unchanged")
            {
                throw new ArgumentException("Unknown hierarchy probe operation.", nameof(Case));
            }
        }
    }

    /// <summary>Executes one complete native guard or one bound typed scalar comparison.</summary>
    /// <returns>Whether an unplanned hierarchy change was found.</returns>
    [Benchmark]
    public bool Probe()
    {
        _changed = Case.Operation switch
        {
            "Plan" => HierarchyChangeDetectionTests.PlanHasChanges(_owner!.Context, _plan!),
            "Scalar" => NestedSetTrackedProperty.HasChanged(_entry!, _property!),
            _ => NestedSetSaveChanges.HasHierarchyChanges(_owner!.Context),
        };

        return _changed;
    }

    /// <summary>Checks the guard result without using numeric timing or allocation thresholds.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (_changed
                || _owner!.Context.ChangeTracker.Entries().Count() != _expectedEntries
                || _owner.Context.ChangeTracker.AutoDetectChangesEnabled
                || _owner.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
            {
                throw new InvalidOperationException("Hierarchy probe changed an unchanged native graph.");
            }
        }
        finally
        {
            _owner?.Dispose();
            _owner = null;
        }
    }
}
