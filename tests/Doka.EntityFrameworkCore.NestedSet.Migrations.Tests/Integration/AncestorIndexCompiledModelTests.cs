using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Executes the ancestor-index contract with freshly generated and compiled EF runtime models.</summary>
[DatabasePlatform]
[Collection(AncestorIndexFixtureRegistration.Name)]
public sealed class AncestorIndexCompiledModelTests
{
    private readonly MigrationFixture _fixture;

    /// <summary>Creates native runtime-model scenarios with isolated migrated databases.</summary>
    /// <param name="fixture">The provider engine owner.</param>
    public AncestorIndexCompiledModelTests(
        MigrationFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Preserves the optional descriptor and uses its generated shadow value during real operations.</summary>
    /// <param name="engine">The real provider whose actual generator creates the model.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task GeneratedModelRetainsAncestorMetadataAndExecutesQueriesAndWrites(
        string engine
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(_fixture, engine);
        var chain = database.Scaffold(true, "AncestorCompiledInitial");
        await database.MigrateAsync(chain);
        await using var design = database.CreateContext();
        var model = CompiledModelTestSupport.Generate(design);
        await using var context = database.CreateContext(model: model);
        var hierarchy = context
            .NestedSet<MigrationNode>()
            .ForScope(7);

        var root = new MigrationNode { NodeId = 1 };
        await hierarchy.InsertRootAsync(root, 11, CancellationToken.None);

        // Act
        await hierarchy.InsertChildAsync(new MigrationNode { NodeId = 2 }, 1, CancellationToken.None);
        var flags = await context
            .Set<MigrationNode>()
            .OrderBy(x => x.NodeId)
            .Select(x => EF.Property<bool>(x, AncestorIndexContext.FlagProperty))
            .ToArrayAsync(CancellationToken.None);

        var strictQuery = hierarchy.SelectAncestors(
            x => x.NodeId == 2,
            (node, owner, distance) => new
            {
                owner.NodeId,
                Distance = distance
            });

        var inclusiveQuery = hierarchy.SelectAncestorsOrSelf(
            x => x.NodeId == 2,
            (node, owner, distance) => new
            {
                owner.NodeId,
                Distance = distance
            });

        var capturedHierarchy = context
            .Set<MigrationNode>()
            .Where(node => node.NodeId == 2);

        var deferredQuery = hierarchy.SelectAncestorsOrSelf(
            node => capturedHierarchy.Any(selected => selected.NodeId == node.NodeId),
            (node, owner, distance) => new
            {
                owner.NodeId,
                Distance = distance
            });

        var strictSql = strictQuery.ToQueryString();
        var inclusiveSql = inclusiveQuery.ToQueryString();
        var deferredSql = deferredQuery.ToQueryString();
        var ancestors = await strictQuery
            .OrderBy(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var inclusive = await inclusiveQuery
            .OrderBy(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var deferred = await deferredQuery
            .OrderBy(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var tracked = await context
            .Set<MigrationNode>()
            .SingleAsync(x => x.NodeId == 1, CancellationToken.None);

        var annotation = model.FindEntityType(typeof(MigrationNode))!
            .FindAnnotation("Doka:NestedSet:AncestorIndexProperty")
            ?.Value;

        // Assert
        Assert.Same(model, context.Model);
        Assert.Equal(AncestorIndexContext.FlagProperty, annotation);
        Assert.Equal([true, false], flags);
        Assert.Equal([1], ancestors.Select(x => x.NodeId));
        Assert.Equal([1], ancestors.Select(x => x.Distance));
        Assert.Equal([1, 2], inclusive.Select(x => x.NodeId));
        Assert.Equal([1, 0], inclusive.Select(x => x.Distance));
        Assert.Equal(inclusive, deferred);
        // WHY: Captured hierarchy roots remain decorrelatable even with a generated runtime model.
        // Normalization must preserve the fallback instead of mistaking that root for independent facts.
        Assert.DoesNotContain("WITH RECURSIVE", deferredSql, StringComparison.OrdinalIgnoreCase);
        Assert.True(context.Entry(tracked).Property<bool>(AncestorIndexContext.FlagProperty).CurrentValue);

        if (engine is "MySql" or "MariaDb")
        {
            var sourceBinding = FindProviderCapability(context, "WithMaterializedSource", IsSourceBindingSignature);
            var straightJoin = FindProviderCapability(context, "StraightJoin", IsStraightJoinSignature);
            var inclusiveEnabled = sourceBinding is not null && straightJoin is not null;

            // WHY: These public capabilities can be deployed independently. Source-only providers optimize
            // strict ancestors, while inclusive queries retain the portable path without the ordered edge.
            Assert.Equal(sourceBinding is not null, UsesCapability(strictQuery.Expression, sourceBinding));
            Assert.Equal(inclusiveEnabled, UsesCapability(inclusiveQuery.Expression, sourceBinding));
            Assert.False(UsesCapability(strictQuery.Expression, straightJoin));
            Assert.Equal(inclusiveEnabled, UsesCapability(inclusiveQuery.Expression, straightJoin));
            Assert.Equal(0, strictSql.Split("STRAIGHT_JOIN").Length - 1);

            // WHY: Inclusive queries direct the candidate edge and final owner hydration separately;
            // source hydration stays ordinary, and strict queries require neither directed edge.
            Assert.Equal(
                inclusiveEnabled ? 2 : 0,
                inclusiveSql.Split("STRAIGHT_JOIN").Length - 1);

            if (sourceBinding is not null)
            {
                Assert.Contains(AncestorIndexContext.FlagColumn, strictSql, StringComparison.Ordinal);
            }

            if (inclusiveEnabled)
            {
                Assert.Contains(AncestorIndexContext.FlagColumn, inclusiveSql, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Inspects only exact public capability signatures owned by the configured provider assembly.</summary>
    private static MethodInfo? FindProviderCapability(
        DbContext context,
        string name,
        Predicate<MethodInfo> matchesSignature
    )
    {
        var assembly = context
            .GetService<IDbContextOptions>()
            .Extensions
            .Where(extension => extension.Info.IsDatabaseProvider)
            .Select(extension => extension.GetType().Assembly)
            .Distinct()
            .Single();

        Assert.Equal(context.Database.ProviderName, assembly.GetName().Name);
        var extension = assembly.GetType("Doka.EntityFrameworkCore.MySql.MySqlQueryableExtensions");

        if (extension is null)
        {
            return null;
        }

        Assert.True(extension.IsVisible);
        Assert.False(extension.ContainsGenericParameters);
        Assert.Same(assembly, extension.Assembly);
        var named = extension.GetMethods(BindingFlags.Public | BindingFlags.Static
                | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == name).ToArray();

        // WHY: An unrelated overload is not this capability. Existing malformed or ambiguous public
        // contracts fail the test instead of being guessed as available or silently treated as absent.
        return named.Length == 0 ? null : Assert.Single(named, matchesSignature);
    }

    /// <summary>Recognizes the exact public source-binding contract, including its generic type identities.</summary>
    private static bool IsSourceBindingSignature(
        MethodInfo method
    )
    {
        if (!IsUnconstrainedGeneric(method, 2, out var generic))
        {
            return false;
        }

        var source = typeof(IQueryable<>).MakeGenericType(generic[0]);
        var result = typeof(IQueryable<>).MakeGenericType(generic[1]);
        var compose = typeof(Expression<>).MakeGenericType(typeof(Func<,>).MakeGenericType(source, result));
        var parameters = method.GetParameters();

        return method.ReturnType == result
            && parameters.Length == 2
            && parameters[0].ParameterType == source
            && parameters[1].ParameterType == compose;
    }

    /// <summary>Recognizes the exact public five-argument ordered join and its four generic type identities.</summary>
    private static bool IsStraightJoinSignature(
        MethodInfo method
    )
    {
        if (!IsUnconstrainedGeneric(method, 4, out var generic))
        {
            return false;
        }

        var outer = typeof(IQueryable<>).MakeGenericType(generic[0]);
        var inner = typeof(IQueryable<>).MakeGenericType(generic[1]);
        var result = typeof(IQueryable<>).MakeGenericType(generic[3]);
        var outerKey = typeof(Expression<>).MakeGenericType(typeof(Func<,>).MakeGenericType(generic[0], generic[2]));
        var innerKey = typeof(Expression<>).MakeGenericType(typeof(Func<,>).MakeGenericType(generic[1], generic[2]));
        var projection = typeof(Expression<>).MakeGenericType(
            typeof(Func<,,>).MakeGenericType(generic[0], generic[1], generic[3]));

        var parameters = method.GetParameters();

        return method.ReturnType == result
            && parameters.Length == 5
            && parameters[0].ParameterType == outer
            && parameters[1].ParameterType == inner
            && parameters[2].ParameterType == outerKey
            && parameters[3].ParameterType == innerKey
            && parameters[4].ParameterType == projection;
    }

    /// <summary>Requires a public concrete static generic definition without variance or type constraints.</summary>
    private static bool IsUnconstrainedGeneric(
        MethodInfo method,
        int arity,
        out Type[] generic
    )
    {
        generic = method.GetGenericArguments();

        return method is { IsPublic: true, IsStatic: true, IsAbstract: false, IsGenericMethodDefinition: true }
            && generic.Length == arity
            && generic.All(parameter => parameter.GenericParameterAttributes == GenericParameterAttributes.None
                && parameter.GetGenericParameterConstraints().Length == 0);
    }

    /// <summary>
    /// Checks consumption of the exact public provider method without requiring its SQL implementation.
    /// </summary>
    private static bool UsesCapability(
        Expression expression,
        MethodInfo? method
    )
    {
        if (method is null)
        {
            return false;
        }

        var probe = new CapabilityProbe(method);
        probe.Visit(expression);

        return probe.Found;
    }

    /// <summary>Recognizes only the exact configured provider's generic public capability.</summary>
    private sealed class CapabilityProbe(MethodInfo method) : ExpressionVisitor
    {
        /// <summary>Gets whether the deferred query contains the exact public capability method.</summary>
        internal bool Found { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (node.Method.IsGenericMethod && node.Method.GetGenericMethodDefinition() == method)
            {
                Found = true;
            }

            return base.VisitMethodCall(node);
        }
    }
}
