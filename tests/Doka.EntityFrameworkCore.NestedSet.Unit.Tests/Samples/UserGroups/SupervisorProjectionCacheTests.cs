using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.UserGroups;

/// <summary>Verifies that shared sample projections retain shape rather than invocation-owned state.</summary>
[Collection("Allocation measurements")]
public sealed class SupervisorProjectionCacheTests
{
    /// <summary>Bounds actual warm resolution construction without translating or executing a query.</summary>
    /// <param name="engine">The configured Doka dialect whose actual sample model supplies the query roots.</param>
    /// <param name="origins">The benchmark's singleton or sixteen-origin request form.</param>
    /// <param name="fields">The number of real sample metadata fields in the request.</param>
    /// <returns>A task completing after the owned context and provider services have been disposed.</returns>
    [Theory]
    [InlineData("MySql", 1, 1)]
    [InlineData("MySql", 1, 4)]
    [InlineData("MySql", 16, 1)]
    [InlineData("MySql", 16, 4)]
    [InlineData("MariaDb", 1, 1)]
    [InlineData("MariaDb", 1, 4)]
    [InlineData("MariaDb", 16, 1)]
    [InlineData("MariaDb", 16, 4)]
    public async Task WarmResolutionConstructionKeepsItsAllocationBudget(string engine, int origins, int fields)
    {
        // Arrange
        const int warmup = 32;
        const int batches = 4;
        const int operations = 64;
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<UserGroupContext>(
                ModelCompatibilityDatabase.Options<UserGroupContext>(engine,
                    "Server=127.0.0.1;Database=supervisor_projection_construction;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary, boundary.Commands);

        var services = new ServiceCollection();
        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: An owned provider graph keeps this no-I/O calibration independent of process-wide
        // service-cache history. Initial model and selector construction are outside the observation.
        await using var provider = services.BuildServiceProvider();
        await using var context = new UserGroupContext(options.UseInternalServiceProvider(provider).Options);
        var tenantId = Guid.Parse("a296dbcb-a9c1-4c70-9bbb-ff506af5d546");
        var catalog = new[] { "Route", "ChannelClass", "ManagerRoute", "RoutingZone" };
        var request = new SupervisorRequest
        {
            SourceUserId = 17, StartGroupId = origins == 1 ? 31 : null,
            SearchStart = SearchStart.ParentsOnly,
            Roles = new MembershipRoleFilter(Guid.Parse("358bc287-e4ec-4413-93c4-23ec6dd4ef93")),
            Groups = new NthEligibleGroup([], 1),
            Matches = catalog.Take(fields).Select(field => new MetadataMatch(
                MetadataOwner.SourceUser, field, MetadataOwner.CandidateMembership, field)).ToArray(),
            OnDay = new DateOnly(2026, 10, 16),
        };

        // WHY: The actual benchmark selects one explicit group for one origin and leaves StartGroupId
        // unset for sixteen origins. A deferred builder has no database cardinality to inspect.
        for (var index = 0; index < warmup; index++)
        {
            GC.KeepAlive(SupervisorQueries.ResolutionRows(context, tenantId, request,
                SupervisorSelection.RequireUnique));
        }

        var allocated = new long[batches];
        IQueryable<SupervisorQueries.SupervisorReadRow>? last = null;

        // Act
        for (var batch = 0; batch < batches; batch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < operations; index++)
            {
                last = SupervisorQueries.ResolutionRows(context, tenantId, request, SupervisorSelection.RequireUnique);
            }

            allocated[batch] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        // Assert
        Assert.NotNull(last);
        Assert.All(allocated, bytes => Assert.True(bytes > 0));
        // WHY: The eight fixed observations use about 218-220 KB/build after projection reuse. A 224 KiB
        // ceiling leaves headroom while rejecting the original 242-244 KB path without a timing gate.
        // Every batch contributes to the mean; no favorable sample or retry chooses the result.
        Assert.InRange(allocated.Average() / operations, 1, 224 * 1024);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Engine={engine}; benchmark origins={origins}; fields={fields}; warmup={warmup}; "
            + $"batches={batches}; operations/batch={operations}; "
            + $"batch bytes=[{string.Join(',', allocated)}]; mean={allocated.Average() / operations:F2} B/build.");
        GC.KeepAlive(last);
    }

    /// <summary>Every actual cached selector contains only parameters, members, and literal constants.</summary>
    [Fact]
    public void CachedSelectorsDoNotRetainInvocationState()
    {
        // Arrange
        var fields = typeof(SupervisorQueries).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => typeof(LambdaExpression).IsAssignableFrom(field.FieldType)).ToArray();

        var probe = new CapturedStateProbe();

        // Act
        foreach (var field in fields)
        {
            probe.Visit((LambdaExpression)field.GetValue(null)!);
        }

        // Assert
        Assert.NotEmpty(fields);
        Assert.Contains(fields, field => field.Name == "s_originMarkerProjection");
        Assert.Contains(fields, field => field.Name == "s_singleGroupRowProjection");
        Assert.Contains(fields, field => field.Name == "s_originOutcomeMarker");
        Assert.All(fields, field => Assert.True(field.IsInitOnly, field.Name));
        Assert.Empty(probe.CapturedNodes);
    }

    /// <summary>The capture inspection rejects actual request, query, model, context, and closure objects.</summary>
    /// <param name="shape">The forbidden retained state or provider extension.</param>
    [Theory]
    [InlineData("context")]
    [InlineData("model")]
    [InlineData("query")]
    [InlineData("request")]
    [InlineData("closure")]
    [InlineData("extension")]
    public async Task CaptureInspectionRejectsInvocationOwnedNodes(string shape)
    {
        // Arrange
        await using var context = new DbContext(new DbContextOptions<DbContext>());
        var source = 17;
        Expression<Func<int>> closure = () => source;
        var expression = shape switch
        {
            "context" => Expression.Constant(context),
            "model" => Expression.Constant(new ModelBuilder().Model),
            "query" => Expression.Constant(Array.Empty<int>().AsQueryable()),
            "request" => Expression.Constant(new SupervisorRequest
            {
                SourceUserId = source, SearchStart = SearchStart.ParentsOnly,
                Roles = new NoRoleFilter(), Groups = new AllEligibleGroups(), Matches = [],
            }),
            "closure" => (Expression)closure,
            "extension" => new UnboundProjectionExtension(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var probe = new CapturedStateProbe();

        // Act
        probe.Visit(expression);

        // Assert
        Assert.Single(probe.CapturedNodes);
    }

    /// <summary>Origin markers and single-group outcomes read the current exact origin's source key.</summary>
    /// <param name="field">The actual cached projection being executed.</param>
    /// <param name="count">The missing, unique, or ambiguous person category; null selects an origin marker.</param>
    [Theory]
    [InlineData("s_originMarkerProjection", null)]
    [InlineData("s_singleGroupRowProjection", 0L)]
    [InlineData("s_singleGroupRowProjection", 1L)]
    [InlineData("s_singleGroupRowProjection", 2L)]
    public void ReadRowsUseEachAuthorizedMembershipWithoutCapturingASource(string field, long? count)
    {
        // Arrange
        var selector = (LambdaExpression)typeof(SupervisorQueries)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        var read = selector.Compile();
        var first = CreateProjectionInput(selector.Parameters[0].Type, 17, count);
        var second = CreateProjectionInput(selector.Parameters[0].Type, 73, count);

        // Act
        var secondRow = (SupervisorQueries.SupervisorReadRow)read.DynamicInvoke(second)!;
        var firstRow = (SupervisorQueries.SupervisorReadRow)read.DynamicInvoke(first)!;

        // Assert
        Assert.Equal(17, firstRow.SourceUserId);
        Assert.Equal(73, secondRow.SourceUserId);
        Assert.All(new[] { firstRow, secondRow }, row =>
        {
            Assert.Equal(1, row.Kind);
            Assert.Equal(31, row.StartGroupId);
            Assert.Equal(count ?? 0, row.Count);
            Assert.Equal(count == 1, row.HasEvidence);
            Assert.Equal(count == 1 ? 29 : 0, row.CandidateUserId);
        });
    }

    /// <summary>Single-group outcomes receive their source key from each origin's exact membership.</summary>
    [Fact]
    public void OriginOutcomeMarkersReadEachAuthorizedMembershipWithoutCapturingASource()
    {
        // Arrange
        var selector = (LambdaExpression)typeof(SupervisorQueries)
            .GetField("s_originOutcomeMarker", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        var read = selector.Compile();
        var first = CreateProjectionInput(selector.Parameters[0].Type, 17, null);
        var second = CreateProjectionInput(selector.Parameters[0].Type, 73, null);

        // Act
        var secondMarker = read.DynamicInvoke(second)!;
        var firstMarker = read.DynamicInvoke(first)!;

        // Assert
        var markerType = selector.ReturnType;
        Assert.Equal(17, markerType.GetProperty("SourceUserId")!.GetValue(firstMarker));
        Assert.Equal(73, markerType.GetProperty("SourceUserId")!.GetValue(secondMarker));
        Assert.All(new[] { firstMarker, secondMarker }, marker =>
        {
            Assert.Equal(31, markerType.GetProperty("StartGroupId")!.GetValue(marker));
            Assert.Null(markerType.GetProperty("CandidateUserId")!.GetValue(marker));
            Assert.Null(markerType.GetProperty("FoundGroupId")!.GetValue(marker));
        });
    }

    /// <summary>Creates only the private alias shape required by the actual selector under test.</summary>
    private static object CreateProjectionInput(Type inputType, int source, long? count)
    {
        // WHY: The alias types deliberately stay private in the sample. Reflecting their data-only
        // inputs here executes the real cached lambda without exposing a production testing API.
        if (count is null)
        {
            var origin = Activator.CreateInstance(inputType, nonPublic: true)!;
            inputType.GetProperty("Membership")!.SetValue(origin, new UserGroupMembership { UserId = source });
            inputType.GetProperty("Group")!.SetValue(origin, new UserGroup { Id = 31 });

            return origin;
        }

        var outcome = Activator.CreateInstance(inputType, nonPublic: true)!;
        inputType.GetProperty("SourceUserId")!.SetValue(outcome, source);
        inputType.GetProperty("StartGroupId")!.SetValue(outcome, 31);
        inputType.GetProperty("Count")!.SetValue(outcome, count.Value);
        inputType.GetProperty("PersonId")!.SetValue(outcome, count == 1 ? (int?)29 : null);

        return outcome;
    }

    /// <summary>Collects nonliteral captured objects and provider-owned extension expressions.</summary>
    private sealed class CapturedStateProbe : ExpressionVisitor
    {
        /// <summary>Gets the nodes that a process-wide immutable projection must not retain.</summary>
        internal List<Expression> CapturedNodes { get; } = [];

        /// <inheritdoc />
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is not null && node.Value is not string
                && !node.Type.IsPrimitive && !node.Type.IsEnum && node.Value is not decimal)
            {
                CapturedNodes.Add(node);
            }

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(Expression node)
        {
            CapturedNodes.Add(node);

            return node;
        }
    }

    /// <summary>Represents an unreduced provider node that a request-free projection must reject.</summary>
    private sealed class UnboundProjectionExtension : Expression
    {
        /// <inheritdoc />
        public override ExpressionType NodeType => ExpressionType.Extension;

        /// <inheritdoc />
        public override Type Type => typeof(int);
    }
}
