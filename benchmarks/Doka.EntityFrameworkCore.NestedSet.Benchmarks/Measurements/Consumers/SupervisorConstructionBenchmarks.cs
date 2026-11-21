using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures deferred candidate and reduced-resolution construction on the actual sample model.</summary>
public class SupervisorConstructionBenchmarks : IAsyncDisposable
{
    private ConsumerMetadataServices<UserGroupContext>? _services;
    private UserGroupContext? _context;
    private SupervisorRequest? _request;
    private static readonly Guid s_tenantId = Guid.Parse("a296dbcb-a9c1-4c70-9bbb-ff506af5d546");

    /// <summary>Gets or sets the configured dialect without creating a native server.</summary>
    [Params("MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite")]
    public string Engine { get; set; } = "Sqlite";

    /// <summary>Gets or sets the explicit single-group or deferred all-memberships request form.</summary>
    /// <remarks>No persisted cardinality is implied by a disconnected query-construction measurement.</remarks>
    [Params(1, 16)]
    public int Origins { get; set; } = 1;

    /// <summary>Gets or sets the original one/four-field and added fifth-field request variants.</summary>
    [Params(1, 4, 5)]
    public int Fields { get; set; } = 1;

    /// <summary>Gets or sets whether every match repeats the same field, as in the original native control.</summary>
    [Params(false, true)]
    public bool RepeatedField { get; set; }

    /// <summary>Finalizes provider metadata and captures one fixed consumer request outside measurement.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        if (Origins is not (1 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(Origins), "Select a declared origin request shape.");
        }

        if (Fields is not (1 or 4 or 5))
        {
            throw new ArgumentOutOfRangeException(nameof(Fields), "Select a declared field request shape.");
        }

        _services = ConsumerMetadataServices<UserGroupContext>.Create(Engine);
        _context = new UserGroupContext(_services.Options);
        // WHY: Preserve the original four-field request; the added comparison uses the sample's Text catalog.
        string[] catalog = ["Route", "ChannelClass", "ManagerRoute", "RoutingZone", "Lane"];
        _request = new SupervisorRequest
        {
            SourceUserId = 17,
            StartGroupId = Origins == 1 ? 31 : null,
            SearchStart = SearchStart.ParentsOnly,
            Roles = new MembershipRoleFilter(Guid.Parse("358bc287-e4ec-4413-93c4-23ec6dd4ef93")),
            Groups = new NthEligibleGroup([], 1),
            Matches = Enumerable.Range(0, Fields)
                .Select(index => new MetadataMatch(
                    MetadataOwner.SourceUser,
                    catalog[RepeatedField ? 0 : index],
                    MetadataOwner.CandidateMembership,
                    catalog[RepeatedField ? 0 : index]))
                .ToArray(),
            OnDay = new DateOnly(2026, 10, 16),
        };

        GC.KeepAlive(_context.Model);
    }

    /// <summary>Builds the complete reduced-resolution expression without executing a database command.</summary>
    /// <returns>The actual deferred scalar outcome query.</returns>
    [Benchmark]
    public object ResolutionRows() => SupervisorQueries.ResolutionRows(
        _context ?? throw new InvalidOperationException("Prepare consumer metadata first."),
        s_tenantId,
        _request ?? throw new InvalidOperationException("Prepare the request first."),
        SupervisorSelection.RequireUnique);

    /// <summary>Builds the complete occurrence query without reducing or materializing candidates.</summary>
    /// <returns>The actual deferred candidate query.</returns>
    [Benchmark]
    public object Candidates() => SupervisorQueries.Candidates(
        _context ?? throw new InvalidOperationException("Prepare consumer metadata first."),
        s_tenantId,
        _request ?? throw new InvalidOperationException("Prepare the request first."));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _request = null;
        var context = _context;
        var services = _services;
        _context = null;
        _services = null;
        if (services is not null)
        {
            await services.DisposeContextAsync(context);
        }
        else if (context is not null)
        {
            await context.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes the unopened context before its explicitly owned provider graph.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();
}
