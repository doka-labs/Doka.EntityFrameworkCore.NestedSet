using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures actual admission counters and retained continuation paths on prepared mutable state.</summary>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class SupervisorChainBookkeepingBenchmarks
{
    private SupervisorQueries.ChainBudget? _budget;
    private SupervisorResolution? _resolution;
    private List<SupervisorQueries.ChainPath>? _paths;
    private readonly List<SupervisorChainBranch> _completed = [];
    private int _admitted;
    private bool _rejectedRead;
    private bool _rejectedVisit;

    /// <summary>Gets or sets the original independent admission or 1024-continuation workload.</summary>
    [Params("Admissions", "Continuations")]
    public string Shape { get; set; } = "Admissions";

    /// <summary>Restores exact operation-owned state outside each single measured iteration.</summary>
    [IterationSetup]
    public void Prepare()
    {
        var count = Shape switch
        {
            "Admissions" => 10_000,
            "Continuations" => 1024,
            _ => throw new ArgumentOutOfRangeException(nameof(Shape)),
        };

        _budget = new SupervisorQueries.ChainBudget(new SupervisorChainRequest
        {
            SourceUserId = 42,
            Steps = [],
            RoutingOrigin = RoutingOrigin.InitialSubject,
            MaxFrontierStates = count,
            MaxVisitedStates = count,
            MaxResultRows = count,
        });

        var candidate = new SupervisorCandidate
        {
            SourceUserId = 42,
            StartGroupId = 7,
            FoundGroupId = 6,
            CandidateUserId = 21,
            Distance = 1,
        };

        _resolution = new SupervisorResolution(SupervisorResolutionStatus.Found, 7, [candidate], candidate);
        _paths = new List<SupervisorQueries.ChainPath>(count);
        _completed.Clear();
        _admitted = 0;
        _rejectedRead = false;
        _rejectedVisit = false;
    }

    /// <summary>Executes the original fixed logical batch without handwritten statistical sampling.</summary>
    /// <returns>The complete number of admitted reads/visits or retained continuation paths.</returns>
    [Benchmark]
    public int Advance()
    {
        var budget = _budget ?? throw new InvalidOperationException("Prepare the operation first.");
        var count = Shape == "Admissions" ? 10_000 : 1024;

        // WHY: This is the actual counter/path workload batch; BenchmarkDotNet owns timing and GC accounting.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < count; index++)
        {
            var admitted = Shape == "Admissions"
                ? budget.Read() && budget.Visit()
                : SupervisorQueries.AdvanceBranch(
                    _resolution!, null, 42, 42, 7, Guid.Empty, 1,
                    SupervisorSelection.RequireUnique, budget, _paths!, _completed);

            _admitted += admitted ? 1 : 0;
        }

        if (Shape == "Admissions")
        {
            _rejectedRead = !budget.Read();
            _rejectedVisit = !budget.Visit();
        }

        return _admitted;
    }

    /// <summary>Requires complete independent counters or path/evidence identities after measurement.</summary>
    [IterationCleanup]
    public void Verify()
    {
        if (Shape == "Admissions")
        {
            BenchmarkFixture.Require(
                _admitted == 10_000 && _rejectedRead && _rejectedVisit && _budget!.RemainingReads == 0,
                "Counter admission or exhaustion changed.");

            return;
        }

        BenchmarkFixture.Require(
            _admitted == 1024 && _paths!.Count == 1024 && _completed.Count == 0
                && _paths.All(path => ReferenceEquals(path.Hop.Evidence, _resolution!.Continuation)),
            "Continuation state lost frontier paths or original evidence identity.");
    }
}
