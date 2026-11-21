using System.Runtime.ExceptionServices;
using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;
using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures actual sample builders using the original capacity-only adapted models and requests.</summary>
/// <remarks>
/// These models qualify construction over read-only capacity mappings, not physical sample migrations.
/// </remarks>
public class CapacityConsumerConstructionBenchmarks : IAsyncDisposable
{
    private NativeCapacitySignalContext? _signals;
    private NativeCapacityGroupContext? _groups;
    private SignalApp.ValidatedSignalReport? _signalRequest;
    private GroupApp.SupervisorRequest? _groupRequest;

    private static readonly string[] s_signalFields = ["Band", "ModuleClass", "Cooling", "RackCode"];
    private static readonly string[] s_groupFields =
    [
        "Route", "ChannelClass", "ManagerRoute", "RoutingZone", "Lane", "Cluster", "Protocol", "Mode",
        "Profile", "Phase", "Zone", "Port", "Rack", "Batch", "Firmware", "DeviceProfile",
    ];

    /// <summary>Gets or sets the provider whose real adapted scalar mappings supply expression binding.</summary>
    [Params(BenchmarkEngine.MySql, BenchmarkEngine.MariaDb, BenchmarkEngine.PostgreSql,
        BenchmarkEngine.SqlServer, BenchmarkEngine.SqliteMemory)]
    public BenchmarkEngine Engine { get; set; }

    /// <summary>Gets or sets a complete original capacity-only construction request.</summary>
    [ParamsSource(nameof(Cases))]
    public CapacityConsumerCase Case { get; set; } = new("Signal", 1_000_000, 1, 1, false);

    /// <summary>Gets all original capacity counts, anchor/source grains, routing widths and ordinal controls.</summary>
    public static IEnumerable<CapacityConsumerCase> Cases
    {
        get
        {
            foreach (var nodes in new[] { 1_000_000, 1_000_001, 10_000_000, 100_001 })
            {
                yield return new("Signal", nodes, 1, 1, false);
                yield return new("Signal", nodes, 4, 4, false);
                yield return new("Signal", nodes, 16, 4, true);

                foreach (var origins in new[] { 1, 16, 100, 1_000 })
                {
                    yield return new("Groups", nodes, origins, origins == 1 ? 1 : origins == 16 ? 4 : 16, false);
                }
            }

            // WHY: The prior chain counter measured its actual first query, not construction of later-hop state.
            yield return new("ChainInitial", 100_001, 1, 0, false);
            yield return new("TypeLastPresent", 100_001, 1, 1, false);
            yield return new("TypeAfterLastAbsent", 100_001, 1, 1, false);
            yield return new("EligibleSecondAbsent", 100_001, 1, 1, false);
        }
    }

    /// <summary>Finalizes the actual capacity adapters and request snapshots outside measurement.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        var signalOptions = new DbContextOptionsBuilder<NativeCapacitySignalContext>();
        var groupOptions = new DbContextOptionsBuilder<NativeCapacityGroupContext>();
        var connection = Engine switch
        {
            BenchmarkEngine.SqliteMemory => "Data Source=:memory:",
            BenchmarkEngine.PostgreSql => "Host=127.0.0.1;Database=Construction;Username=unused;Password=unused",
            BenchmarkEngine.SqlServer => "Server=127.0.0.1;Database=Construction;User Id=sa;Password=unused;"
                + "TrustServerCertificate=True",
            _ => "Server=127.0.0.1;Database=Construction;User=unused;Password=unused",
        };
        BenchmarkEnvironment.ConfigureProvider(signalOptions, Engine, connection);
        BenchmarkEnvironment.ConfigureProvider(groupOptions, Engine, connection);
        _signals = new NativeCapacitySignalContext(signalOptions.Options);
        _groups = new NativeCapacityGroupContext(groupOptions.Options);
        GC.KeepAlive(_signals.Model);
        GC.KeepAlive(_groups.Model);
        PrepareRequest();
        GC.KeepAlive(Construct());
    }

    /// <summary>Constructs exactly the original sample query over adapted capacity metadata, without I/O.</summary>
    [Benchmark]
    public object Construct()
    {
        if (Case.Profile == "Signal")
        {
            return SignalApp.SignalReportBuilder.Build(_signals!, _signalRequest!);
        }

        return GroupApp.SupervisorQueries.Candidates(_groups!, NativeCapacityConsumerModel.Tenant, _groupRequest!);
    }

    /// <summary>
    /// Prepares each request's input arrays and immutable snapshot outside the measured expression build.
    /// </summary>
    internal void PrepareRequest()
    {
        if (Case.Profile == "Signal")
        {
            var broad = Case.Origins != 1;
            var request = new SignalApp.SignalReportRequest(
                NativeCapacityConsumerModel.Tenant,
                NativeCapacityConsumerModel.Tree,
                Enumerable.Range(Case.Nodes - Case.Origins + 2, Case.Origins - 1).Prepend(1)
                    .Select(NativeCapacityConsumerModel.Identity).ToArray(),
                Enumerable.Range(101, broad ? 2 : 1).Select(NativeCapacityConsumerModel.Identity).ToArray(),
                Enumerable.Range(0, broad ? 3 : 1).Select(source => (SignalApp.CapturePort)source).ToArray(),
                NativeCapacityConsumerModel.FirstDay,
                NativeCapacityConsumerModel.FirstDay.AddDays(broad ? 2 : 1),
                SignalApp.MeasurementGranularity.Daily,
                s_signalFields.Take(Case.Fields).Select(field => new SignalApp.TagCriterion(
                    NativeCapacityConsumerModel.Identity(1), field, "yes",
                    Case.Nearest ? SignalApp.TagOwnerMode.Nearest : SignalApp.TagOwnerMode.Any)).ToArray());
            _signalRequest = SignalApp.SignalReportValidation.Snapshot(request);

            return;
        }

        _groupRequest = new GroupApp.SupervisorRequest
        {
            SourceUserId = SourceUser(Case.Origins),
            SearchStart = GroupApp.SearchStart.ParentsOnly,
            Roles = new GroupApp.EitherRole(NativeCapacityConsumerModel.Identity(10)),
            Groups = new GroupApp.AllEligibleGroups(1),
            Matches = s_groupFields.Take(Case.Fields).Select(field => new GroupApp.MetadataMatch(
                GroupApp.MetadataOwner.SourceUser, field, GroupApp.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        _groupRequest = Case.Profile switch
        {
            "Groups" => _groupRequest,
            "ChainInitial" => _groupRequest with
            {
                StartGroupId = Case.Nodes,
                Roles = new GroupApp.MembershipRoleFilter(NativeCapacityConsumerModel.Identity(201)),
                Matches = [],
            },
            "TypeLastPresent" => _groupRequest with
            {
                Groups = new GroupApp.NthTypeGroupThenValidate(1, Case.Nodes - 1),
            },
            "TypeAfterLastAbsent" => _groupRequest with
            {
                Groups = new GroupApp.NthTypeGroupThenValidate(1, Case.Nodes),
            },
            "EligibleSecondAbsent" => _groupRequest with { Groups = new GroupApp.NthEligibleGroup([1], 2) },
            _ => throw new InvalidOperationException("The capacity consumer construction profile is unknown."),
        };
    }

    /// <summary>Reproduces the capacity dataset's disjoint source membership identities.</summary>
    private static int SourceUser(int origins) => origins switch
    {
        1 => 100_001,
        16 => 100_016,
        100 => 100_100,
        1_000 => 101_000,
        _ => throw new ArgumentOutOfRangeException(nameof(origins)),
    };

    /// <summary>Checks no-I/O ownership before releasing the independently owned adapted contexts.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            var signals = _signals;
            var groups = _groups;
            _signals = null;
            _groups = null;

            Exception? signalFailure = null;

            if (signals is not null)
            {
                try
                {
                    try
                    {
                        BenchmarkFixture.Require(
                            signals.Database.GetDbConnection().State == System.Data.ConnectionState.Closed,
                            "Capacity signal construction opened a database connection.");
                    }
                    finally
                    {
                        await signals.DisposeAsync();
                    }
                }
                catch (Exception exception)
                {
                    signalFailure = exception;
                }
            }

            try
            {
                if (groups is not null)
                {
                    try
                    {
                        BenchmarkFixture.Require(
                            groups.Database.GetDbConnection().State == System.Data.ConnectionState.Closed,
                            "Capacity group construction opened a database connection.");
                    }
                    finally
                    {
                        await groups.DisposeAsync();
                    }
                }
            }
            catch (Exception groupFailure) when (signalFailure is not null)
            {
                // WHY: Both independent contexts must close; a later cleanup failure must not erase the first one.
                throw new AggregateException(signalFailure, groupFailure);
            }

            if (signalFailure is not null)
            {
                ExceptionDispatchInfo.Capture(signalFailure).Throw();
            }
        }
        finally
        {
            // WHY: Finalization suppression belongs to the owned lifecycle even when cleanup fails.
            GC.SuppressFinalize(this);
        }
    }
}
