using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Guards the actual server rank expression and its bounded status-and-evidence reconstruction.</summary>
public sealed partial class SupervisorServerRankTests
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
    public void Rank_ActualServerRank_FitsTheCompleteSupportedRange(
        int distance,
        int priority,
        long expected
    )
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
    public void Rank_Distance_PrecedesTheEntirePriorityRangeWithoutUsingPersonIdentity()
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
            SourceUserId = 1,
            Roles = new NoRoleFilter(),
            Groups = new AllEligibleGroups(),
        };

        var query = SupervisorQueries.ResolutionRows(
            context,
            Guid.NewGuid(),
            request,
            SupervisorSelection.NthPerson(1));

        var probe = new RankSelectorProbe();
        probe.Visit(query.Expression);
        if (probe.Selectors.FirstOrDefault() is not Expression<Func<SupervisorCandidate, long>> selector)
        {
            throw new InvalidOperationException("The server query did not expose its candidate rank selector.");
        }

        return selector.Compile();
    }

    /// <summary>Owns each test's EF graph so singleton interceptor identities cannot enter the global cache.</summary>
    private static ServiceProvider CreateServices(
        SupervisorCommandProbe? probe = null
    )
    {
        var services = new ServiceCollection();
        foreach (var extension in Options()
            .Extensions)
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
    private static UserGroupContext CreateContext(
        IServiceProvider services
    )
        => new(
            new DbContextOptionsBuilder<UserGroupContext>(Options())
                .UseInternalServiceProvider(services)
                .Options);

    /// <summary>Uses the actual sample model and standard in-memory SQLite provider configuration.</summary>
    private static DbContextOptions<UserGroupContext> Options()
        => new DbContextOptionsBuilder<UserGroupContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

    /// <summary>Creates only the scalar input consumed by the production rank selector.</summary>
    private static SupervisorCandidate Candidate(
        int person,
        int distance,
        int priority
    ) => new()
    {
        TenantId = Guid.Empty,
        SourceUserId = 0,
        StartGroupId = 0,
        StartTreeId = Guid.Empty,
        FoundGroupId = 0,
        CandidateUserId = person,
        Distance = distance,
        LeadershipPriority = priority,
    };

    /// <summary>Finds the actual Int64 aggregate selector within the deferred server query.</summary>
    private sealed class RankSelectorProbe : ExpressionVisitor
    {
        /// <summary>Gets selectors whose inputs and return type identify the person-rank aggregate.</summary>
        internal List<LambdaExpression> Selectors { get; } = [];

        /// <inheritdoc />
        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (node.Method.Name == nameof(Enumerable.Min)
                && node.Arguments.Count == 2)
            {
                var argument = node.Arguments[1] is UnaryExpression { NodeType: ExpressionType.Quote } quote
                    ? quote.Operand
                    : node.Arguments[1];

                if (argument is LambdaExpression { ReturnType: { } result } selector
                    && result == typeof(long)
                    && selector.Parameters.Count == 1
                    && selector.Parameters[0].Type == typeof(SupervisorCandidate))
                {
                    Selectors.Add(selector);
                }
            }

            return base.VisitMethodCall(node);
        }
    }
}
