using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Compares portable bounded key predicates against each provider's native collection translation.</summary>
public abstract partial class KeyFilterComparisonTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Shares provider provisioning while comparison cases use independently reset rows.</summary>
    protected KeyFilterComparisonTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Both query forms preserve native mapped integer, Guid, binary, and collated string equality.</summary>
    [Fact]
    public async Task NativeContainsAndBalancedEqualityHaveEquivalentResults()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        var integers = Enumerable
            .Range(1, 256)
            .ToArray();

        var guids = integers
            .Select(value => new Guid(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))
            .ToArray();

        var binaries = integers
            .Select(BitConverter.GetBytes)
            .ToArray();

        var strings = integers
            .Select(value => $"Node-{value:D4}")
            .ToArray();

        for (var index = 0; index < integers.Length; index++)
        {
            await setup.AddAsync(
                new TreeNode
                {
                    NodeId = integers[index],
                    Tree = 1,
                    TreeId = guids[index],
                    Start = 1,
                    End = 2,
                },
                CancellationToken.None);

            await setup.AddAsync(
                new GuidNode
                {
                    Id = guids[index],
                    Tree = 1,
                    TreeId = guids[index],
                    Left = 1,
                    Right = 2,
                },
                CancellationToken.None);

            await setup.AddAsync(
                new BinaryNode
                {
                    Id = binaries[index],
                    Tree = 1,
                    TreeId = guids[index],
                    Left = 1,
                    Right = 2,
                },
                CancellationToken.None);

            await setup.AddAsync(
                new TextNode
                {
                    Id = strings[index],
                    Tree = "scope",
                    TreeId = guids[index],
                    Left = 1,
                    Right = 2,
                },
                CancellationToken.None);
        }

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await ServerQueryPlanTests.RefreshStatisticsAsync<TreeNode>(setup, Engine);
        await ServerQueryPlanTests.RefreshStatisticsAsync<GuidNode>(setup, Engine);
        await ServerQueryPlanTests.RefreshStatisticsAsync<BinaryNode>(setup, Engine);
        await ServerQueryPlanTests.RefreshStatisticsAsync<TextNode>(setup, Engine);
        var probe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = database.CreateContext(probe);
        var results = new List<(string[] Equality, string[] Contains, string[] Production)>();
        var evidence = new List<string>();
        int[] expectedStringCounts = Engine == "PostgreSql" ? [0, 0, 0, 0] : [0, 1, 2, 64];

        // Act
        foreach (var count in new[] { 0, 1, 2, 64 })
        {
            results.Add(
                await CompareAsync<TreeNode, int>(
                    context,
                    probe,
                    Engine,
                    nameof(TreeNode.NodeId),
                    integers
                        .Where((_, index) => index % 4 == 0)
                        .Take(count)
                        .ToArray(),
                    evidence));

            results.Add(
                await CompareAsync<GuidNode, Guid>(
                    context,
                    probe,
                    Engine,
                    nameof(GuidNode.Id),
                    guids
                        .Where((_, index) => index % 4 == 0)
                        .Take(count)
                        .ToArray(),
                    evidence));

            results.Add(
                await CompareAsync<BinaryNode, byte[]>(
                    context,
                    probe,
                    Engine,
                    nameof(BinaryNode.Id),
                    binaries
                        .Where((_, index) => index % 4 == 0)
                        .Take(count)
                        .ToArray(),
                    evidence));

            results.Add(
                await CompareAsync<TextNode, string>(
                    context,
                    probe,
                    Engine,
                    nameof(TextNode.Id),
                    strings
                        .Where((_, index) => index % 4 == 0)
                        .Take(count)
                        .Select(value => value.ToLowerInvariant())
                        .ToArray(),
                    evidence));
        }

        await QueryPlanTestSupport.WriteEvidenceAsync(Engine + "-key-filter", evidence);

        // Assert
        Assert.All(results, result => Assert.Equal(result.Equality, result.Contains));
        Assert.All(results, result => Assert.Equal(result.Equality, result.Production));
        Assert.Equal(
            [0, 0, 0, 1, 1, 1, 2,2, 2, 64, 64, 64],
            results
                .Where((_, index) => index % 4 != 3)
                .Select(result => result.Equality.Length));
        Assert.Equal(
            expectedStringCounts,
            results
                .Where((_, index) => index % 4 == 3)
                .Select(result => result.Equality.Length));
        Assert.All(probe.Commands, sql => Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Warms each shape once, records a second execution, and retains its generated SQL and native plan.
    /// </summary>
    private static async Task<(string[] Equality, string[] Contains, string[] Production)> CompareAsync<TEntity, TKey>(
        DbContext context,
        EnterpriseProbe probe,
        string engine,
        string property,
        TKey[] keys,
        List<string> evidence
    )
        where TEntity : class
    {
        var equality = context
            .Set<TEntity>()
            .Where(BalancedEquality<TEntity, TKey>(property, keys));

        var contains = context
            .Set<TEntity>()
            .Where(node => keys
                .AsEnumerable()
                .Contains(EF.Property<TKey>(node, property)));

        var mappedProperty = context.Model.FindEntityType(typeof(TEntity))!.FindProperty(property)!;
        var production = context
            .Set<TEntity>()
            .Where(NestedSetKeyFilter<TEntity>.Matches(mappedProperty, keys));

        var results = new string[3][];
        var queries = new[] { equality, contains, production };

        for (var index = 0; index < queries.Length; index++)
        {
            await queries[index]
                .Select(node => EF.Property<TKey>(node, property))
                .ToArrayAsync(CancellationToken.None);

            var commandIndex = probe.Commands.Count;
            var start = Stopwatch.GetTimestamp();
            var rows = await queries[index]
                .Select(node => EF.Property<TKey>(node, property))
                .ToArrayAsync(CancellationToken.None)
                ;
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            results[index] = rows
                .Select(value => value is byte[] bytes ? Convert.ToHexString(bytes) : value!.ToString()!)
                .Order(StringComparer.Ordinal)
                .ToArray();

            var plan = await ServerQueryPlanTests.ExplainAsync(context, probe, commandIndex, engine);
            var shape = index == 0
                ? "equality"
                : index == 1
                    ? "contains"
                    : "production";

            evidence.Add(
                $"entity={typeof(TEntity).Name}; shape={shape}; "
                + $"keys={keys.Length}; rows={rows.Length}; observed_ms={elapsed:F3}\n"
                + probe.Commands[commandIndex]
                + "\n"
                + plan);
        }

        return (results[0], results[1], results[2]);
    }

    /// <summary>
    /// Keeps the pre-optimization predicate independent of the production implementation under comparison.
    /// </summary>
    private static Expression<Func<TEntity, bool>> BalancedEquality<TEntity, TKey>(
        string property,
        TKey[] keys
    )
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var access = NestedSetExpressions.Property(parameter, property, typeof(TKey));

        return Expression.Lambda<Func<TEntity, bool>>(Combine(0, keys.Length), parameter);

        Expression Combine(
            int start,
            int count
        )
        {
            if (count == 0)
            {
                return Expression.Constant(false);
            }

            if (count == 1)
            {
                var value = keys[start];
                Expression<Func<TKey>> captured = () => value;

                return Expression.Equal(access, captured.Body);
            }

            var half = count / 2;

            return Expression.OrElse(Combine(start, half), Combine(start + half, count - half));
        }
    }
}
