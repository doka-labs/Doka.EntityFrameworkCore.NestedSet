using System.Linq.Expressions;
using System.Text;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes actual command size and EF compilation reuse within an owned service graph.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Changing scalar request values preserves an asynchronously warmed query shape with one or sixteen matches.
    /// </summary>
    /// <param name="fields">The number of real matching metadata fields in each request.</param>
    /// <param name="varyCardinality">Whether the second request grows to sixteen aligned field pairs.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(16, false)]
    [InlineData(1, true)]
    public async Task MatchingRoutingRequestsReuseTheScopedCompiledQuery(int fields, bool varyCardinality)
    {
        // Arrange
        var commands = new SupervisorCommandProbe();
        await using var seeded = await CreateAsync(new SupervisorCommandProbe());
        var first = await App.SupervisorScenarioData.CreateAsync(seeded, Guid.NewGuid(), CancellationToken.None);
        await SeedOriginProfileAsync(seeded, first, 1, varyCardinality ? 16 : fields);
        var second = await App.SupervisorScenarioData.CreateAsync(seeded, Guid.NewGuid(), CancellationToken.None);
        await SeedOriginProfileAsync(seeded, second, 1, varyCardinality ? 16 : fields);
        var firstFields = s_profileFields.Take(fields).ToArray();
        var secondFields = firstFields.ToArray();
        if (varyCardinality)
        {
            // WHY: The aligned field arrays are parameters. Increasing their length must not copy
            // owner-query templates, alter SQL size, or freeze values from the earlier request.
            secondFields = s_profileFields.ToArray();
        }
        else if (fields == 1)
        {
            secondFields[0] = "ChannelClass";
        }
        else
        {
            (secondFields[0], secondFields[1]) = (secondFields[1], secondFields[0]);
        }

        var firstRequest = first.Request(App.SearchStart.IncludeSelf) with
        {
            Groups = new App.NthTypeGroupThenValidate(4, 1),
            Matches = firstFields.Select(field => new App.MetadataMatch(
                App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        var secondRequest = second.Request(App.SearchStart.IncludeSelf) with
        {
            Groups = firstRequest.Groups,
            OnDay = new DateOnly(2026, 10, 17),
            Matches = secondFields.Select(field => new App.MetadataMatch(
                App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        var compilation = new SupervisorCompilationProbe();
        var options = new DbContextOptionsBuilder<App.UserGroupContext>(
            ModelCompatibilityDatabase.Options<App.UserGroupContext>(
                Engine, seeded.Database.GetConnectionString()!));

        // WHY: An owned service graph isolates this cache observation from parallel suites and their query shapes.
        // A real asynchronous execution warms the same cache used below; ToQueryString would warm another path.
        var services = new ServiceCollection();
        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        services.AddScoped<IInterceptor>(_ => commands);
        services.AddScoped<IInterceptor>(_ => compilation);
        await using var provider = services.BuildServiceProvider();
        await using var context = new App.UserGroupContext(options.UseInternalServiceProvider(provider).Options);
        commands.Enabled = true;
        var beforeWarmup = compilation.Count;
        var warmup = await App.SupervisorQueries.ResolveAsync(context, first.TenantId, firstRequest,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        var afterWarmup = compilation.Count;

        // Act
        var changed = await App.SupervisorQueries.ResolveAsync(context, second.TenantId, secondRequest,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        var repeated = await App.SupervisorQueries.ResolveAsync(context, first.TenantId, firstRequest,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(beforeWarmup + 1, afterWarmup);
        Assert.Equal(afterWarmup, compilation.Count);
        Assert.Equal(0, compilation.UnexpandedTextCalls);
        Assert.Equal(3, commands.Commands.Count);
        if (varyCardinality)
        {
            // WHY: A provider may specialize a singleton collection after the compilation cache.
            // Explicit catalog-array transport must still prevent criterion-sized binding expansion.
            Assert.Equal(commands.Commands[0], commands.Commands[2]);
            var commandBytes = commands.Commands.Select(Encoding.UTF8.GetByteCount).ToArray();
            Assert.InRange(commandBytes.Max() - commandBytes.Min(), 0, 4_096);
            Assert.Single(commands.ParameterCounts.Distinct());
        }
        else
        {
            Assert.Single(commands.Commands.Distinct(StringComparer.Ordinal));
            Assert.Single(commands.ParameterCounts.Distinct());
        }

        Assert.Equal(first.Users[1], Assert.Single(Assert.Single(warmup.Origins).Evidence).CandidateUserId);
        Assert.Equal(first.Users[1], Assert.Single(Assert.Single(repeated.Origins).Evidence).CandidateUserId);
        var changedOrigin = Assert.Single(changed.Origins);
        Assert.Equal(fields == 1 && !varyCardinality
                ? App.SupervisorResolutionStatus.NotFound : App.SupervisorResolutionStatus.Found,
            changedOrigin.Status);
        Assert.All(changedOrigin.Evidence, evidence =>
        {
            Assert.Equal(second.TenantId, evidence.TenantId);
            Assert.Equal(second.Users[0], evidence.SourceUserId);
            Assert.Equal(second.Users[1], evidence.CandidateUserId);
        });

        Assert.Contains(first.Users[0], commands.ParameterValues);
        Assert.Contains(second.Users[0], commands.ParameterValues);
        Assert.True(commands.HasStringParameterElement("Route"));
        Assert.True(commands.HasStringParameterElement("ChannelClass"));
        Assert.DoesNotContain("'Route'", commands.Commands[0], StringComparison.Ordinal);
        Assert.DoesNotContain("'Department'", commands.Commands[0], StringComparison.Ordinal);
        Assert.DoesNotContain(first.TenantId.ToString(), commands.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(second.TenantId.ToString(), commands.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, commands.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Engine={Engine}; fields={fields}; SQL={Encoding.UTF8.GetByteCount(commands.Commands[0])} B; "
            + $"lines={commands.Commands[0].Count(character => character == '\n') + 1}; "
            + $"parameters={commands.ParameterCounts[0]}; warm compilations={afterWarmup - beforeWarmup}; "
            + $"later compilations={compilation.Count - afterWarmup}; next fields={secondFields.Length}; "
            + $"SQL variants={commands.Commands.Distinct(StringComparer.Ordinal).Count()}; "
            + $"parameter counts={string.Join(',', commands.ParameterCounts)}.");
    }

    /// <summary>Counts compilation in one test-owned graph and rejects custom text calls before translation.</summary>
    private sealed class SupervisorCompilationProbe : ExpressionVisitor, IQueryExpressionInterceptor
    {
        /// <summary>Gets actual EF query-compilation callbacks, rather than command executions.</summary>
        internal int Count { get; private set; }

        /// <summary>Gets local comparison calls that should already be expanded to framework expressions.</summary>
        internal int UnexpandedTextCalls { get; private set; }

        /// <inheritdoc />
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Count++;
            Visit(queryExpression);

            return queryExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(App.SupervisorExactText))
            {
                UnexpandedTextCalls++;
            }

            return base.VisitMethodCall(node);
        }
    }
}
