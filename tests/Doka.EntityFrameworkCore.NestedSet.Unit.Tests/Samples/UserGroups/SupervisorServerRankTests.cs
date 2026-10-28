using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Guards the actual server rank expression and its bounded status-and-evidence reconstruction.</summary>
public sealed class SupervisorServerRankTests
{
    /// <summary>The translated rank retains every Int32 priority at both nonnegative distance extrema.</summary>
    /// <param name="distance">The structural distance supplied by the hierarchy projection.</param>
    /// <param name="priority">The signed business priority.</param>
    /// <param name="expected">The independently known exact Int64 representation.</param>
    [Theory]
    [InlineData(0, int.MinValue, 0L)]
    [InlineData(0, -1, 2_147_483_647L)]
    [InlineData(0, 0, 2_147_483_648L)]
    [InlineData(0, int.MaxValue, 4_294_967_295L)]
    [InlineData(int.MaxValue, int.MinValue, 9_223_372_032_559_808_512L)]
    [InlineData(int.MaxValue, -1, 9_223_372_034_707_292_159L)]
    [InlineData(int.MaxValue, 0, 9_223_372_034_707_292_160L)]
    [InlineData(int.MaxValue, int.MaxValue, long.MaxValue)]
    public void ActualServerRankFitsTheCompleteSupportedRange(int distance, int priority, long expected)
    {
        // Arrange
        var rank = ActualRank();
        var candidate = Candidate(1, distance, priority);

        // Act
        var actual = rank(candidate);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Every priority at a nearer distance precedes every priority at the next distance.</summary>
    [Fact]
    public void DistancePrecedesTheEntirePriorityRangeWithoutUsingPersonIdentity()
    {
        // Arrange
        var rank = ActualRank();
        var nearer = Candidate(99, int.MaxValue - 1, int.MaxValue);
        var farther = Candidate(1, int.MaxValue, int.MinValue);
        var tied = Candidate(42, nearer.Distance, nearer.LeadershipPriority);

        // Act
        var nearerRank = rank(nearer);
        var fartherRank = rank(farther);
        var tiedRank = rank(tied);

        // Assert
        Assert.Equal(nearerRank + 1, fartherRank);
        Assert.Equal(nearerRank, tiedRank);
    }

    /// <summary>
    /// Server ranking ignores a repeated person's farther priority for Nth, retains its evidence, and keeps ties
    /// and unoccupied slots explicit. Highest deliberately uses the farther best-priority occurrence instead.
    /// </summary>
    /// <param name="highest">Whether business priority replaces Nth-person selection.</param>
    /// <param name="slot">The requested one-based distinct-person slot.</param>
    /// <param name="tie">Whether both nearest people occupy the same rank band.</param>
    /// <param name="expected">The expected explicit resolution status.</param>
    /// <param name="personIndex">The expected fixture user, or a negative unresolved sentinel.</param>
    /// <param name="evidenceCount">The number of retained original occurrences.</param>
    /// <param name="cursorGroupIndex">The canonical continuation group, or a negative unresolved sentinel.</param>
    [Theory]
    [InlineData(false, 1, false, "Found", 2, 1, 4)]
    [InlineData(false, 2, false, "Found", 1, 2, 4)]
    [InlineData(false, 1, true, "Ambiguous", -1, 0, -1)]
    [InlineData(false, 2, true, "Ambiguous", -1, 0, -1)]
    [InlineData(false, 99, false, "NotFound", -1, 0, -1)]
    [InlineData(false, int.MaxValue, false, "NotFound", -1, 0, -1)]
    [InlineData(true, 1, false, "Found", 1, 2, 2)]
    public async Task ActualReaderPreservesNearestPersonRanksAndAllOriginalEvidence(
        bool highest,
        int slot,
        bool tie,
        string expected,
        int personIndex,
        int evidenceCount,
        int cursorGroupIndex
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var services = CreateServices(probe);
        await using var scope = services.CreateAsyncScope();
        await using var context = CreateContext(scope.ServiceProvider);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.UserGroupMemberships.Where(member => member.TenantId == data.TenantId
                && member.UserId == data.Users[1])
            .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.LeadershipPriority,
                tie ? 0 : int.MaxValue), CancellationToken.None);
        await context.UserGroupMemberships.Where(member => member.TenantId == data.TenantId
                && member.UserId == data.Users[2])
            .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.LeadershipPriority, 0),
                CancellationToken.None);
        context.UserGroupMemberships.Add(new UserGroupMembership
        {
            TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[2],
            LeadershipPriority = int.MinValue,
        });

        context.MembershipRoles.Add(new MembershipRole
        {
            TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[2],
            RoleId = data.ManagerRoleId,
        });

        context.MembershipMetadatas.Add(new MembershipMetadata
        {
            TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[2],
            GroupTypeId = 1, FieldCode = "Route", Value = "lane-blue",
        });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = data.Request(group: data.Groups[5]) with { Groups = new AllEligibleGroups() };
        var selection = highest ? SupervisorSelection.HighestBusinessPriority : SupervisorSelection.NthPerson(slot);
        probe.Enabled = true;

        // Act
        var result = await SupervisorQueries.ResolveAsync(
            context, data.TenantId, request, selection, CancellationToken.None);

        // Assert
        Assert.Equal(SupervisorBatchStatus.Completed, result.Status);
        var origin = Assert.Single(result.Origins);
        Assert.Equal(expected, origin.Status.ToString());
        Assert.Equal(personIndex < 0 ? null : data.Users[personIndex], origin.Continuation?.CandidateUserId);
        Assert.Equal(cursorGroupIndex < 0 ? null : data.Groups[cursorGroupIndex],
            origin.Continuation?.FoundGroupId);
        Assert.Equal(evidenceCount, origin.Evidence.Count);
        Assert.All(origin.Evidence, row =>
        {
            Assert.Equal(data.Users[personIndex], row.CandidateUserId);
            Assert.Equal(data.Groups[5], row.StartGroupId);
            Assert.Equal(data.TreeId, row.StartTreeId);
        });

        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A real zero rank remains occupied while a later absent slot cannot become rank zero.</summary>
    /// <param name="slot">The requested one-based person slot.</param>
    /// <param name="found">Whether that slot contains the sole qualifying person.</param>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task ValidRankZeroIsDistinctFromAnUnoccupiedSlot(int slot, bool found)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var services = CreateServices(probe);
        await using var scope = services.CreateAsyncScope();
        await using var context = CreateContext(scope.ServiceProvider);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        context.UserGroupMemberships.Add(new UserGroupMembership
        {
            TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[5],
            LeadershipPriority = int.MinValue,
        });

        context.MembershipRoles.Add(new MembershipRole
        {
            TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[5],
            RoleId = data.ManagerRoleId,
        });

        context.MembershipMetadatas.Add(new MembershipMetadata
        {
            TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[5],
            GroupTypeId = 4, FieldCode = "Route", Value = "lane-blue",
        });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = data.Request(SearchStart.IncludeSelf, data.Groups[5]) with
        {
            Groups = new AllEligibleGroups([4]),
        };

        probe.Enabled = true;

        // Act
        var result = await SupervisorQueries.ResolveAsync(
            context, data.TenantId, request, SupervisorSelection.NthPerson(slot), CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(found ? SupervisorResolutionStatus.Found : SupervisorResolutionStatus.NotFound, origin.Status);
        Assert.Equal(found ? data.Users[1] : (int?)null, origin.Continuation?.CandidateUserId);
        Assert.Equal(found ? 1 : 0, origin.Evidence.Count);
        Assert.All(origin.Evidence, row =>
        {
            Assert.Equal(0, row.Distance);
            Assert.Equal(int.MinValue, row.LeadershipPriority);
        });

        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Compiles the actual production MIN selector rather than duplicating its arithmetic in the test.
    /// </summary>
    private static Func<SupervisorCandidate, long> ActualRank()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        using var context = CreateContext(scope.ServiceProvider);
        var request = new SupervisorRequest
        {
            SearchStart = SearchStart.ParentsOnly,
            Matches = [],
            SourceUserId = 1, Roles = new NoRoleFilter(), Groups = new AllEligibleGroups(),
        };

        var query = SupervisorQueries.ResolutionRows(
            context, Guid.NewGuid(), request, SupervisorSelection.NthPerson(1));

        var probe = new RankSelectorProbe();
        probe.Visit(query.Expression);
        if (probe.Selectors.FirstOrDefault() is not Expression<Func<SupervisorCandidate, long>> selector)
        {
            throw new InvalidOperationException("The server query did not expose its candidate rank selector.");
        }

        return selector.Compile();
    }

    /// <summary>Owns each test's EF graph so singleton interceptor identities cannot enter the global cache.</summary>
    private static ServiceProvider CreateServices(SupervisorCommandProbe? probe = null)
    {
        var services = new ServiceCollection();
        foreach (var extension in Options().Extensions)
        {
            extension.ApplyServices(services);
        }

        if (probe is not null)
        {
            // WHY: Register through the owned graph, rather than adding a fresh singleton interceptor to options.
            // Otherwise each theory case creates a cached internal provider and contaminates unrelated tests.
            services.AddScoped<IInterceptor>(_ => probe);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Creates the actual sample context inside the test-owned service scope.</summary>
    private static UserGroupContext CreateContext(IServiceProvider services)
        => new(new DbContextOptionsBuilder<UserGroupContext>(Options()).UseInternalServiceProvider(services).Options);

    /// <summary>Uses the actual sample model and standard in-memory SQLite provider configuration.</summary>
    private static DbContextOptions<UserGroupContext> Options()
        => new DbContextOptionsBuilder<UserGroupContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()).Options;

    /// <summary>Creates only the scalar input consumed by the production rank selector.</summary>
    private static SupervisorCandidate Candidate(int person, int distance, int priority) => new()
    {
        TenantId = Guid.Empty, SourceUserId = 0, StartGroupId = 0, StartTreeId = Guid.Empty,
        FoundGroupId = 0, CandidateUserId = person, Distance = distance, LeadershipPriority = priority,
    };

    /// <summary>Finds the actual Int64 aggregate selector within the deferred server query.</summary>
    private sealed class RankSelectorProbe : ExpressionVisitor
    {
        /// <summary>Gets selectors whose inputs and return type identify the person-rank aggregate.</summary>
        internal List<LambdaExpression> Selectors { get; } = [];

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(Enumerable.Min) && node.Arguments.Count == 2)
            {
                var argument = node.Arguments[1] is UnaryExpression { NodeType: ExpressionType.Quote } quote
                    ? quote.Operand : node.Arguments[1];

                if (argument is LambdaExpression { ReturnType: { } result } selector
                    && result == typeof(long) && selector.Parameters.Count == 1
                    && selector.Parameters[0].Type == typeof(SupervisorCandidate))
                {
                    Selectors.Add(selector);
                }
            }

            return base.VisitMethodCall(node);
        }
    }
}
