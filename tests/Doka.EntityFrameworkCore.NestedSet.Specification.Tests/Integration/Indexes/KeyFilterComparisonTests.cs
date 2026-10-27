using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Compares portable bounded key predicates against each provider's native collection translation.</summary>
public abstract partial class KeyFilterComparisonTests : ProviderTest
{
    private readonly KeyFilterComparisonFixture _fixture;

    /// <summary>
    /// Shares immutable comparison rows while sibling native-mapping cases retain their resettable rows.
    /// </summary>
    protected KeyFilterComparisonTests(
        IProviderFixture<KeyFilterComparisonFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Gets separately discoverable key representations and membership cardinalities.</summary>
    public static TheoryData<string, int> ComparisonCases
    {
        get
        {
            var cases = new TheoryData<string, int>();

            foreach (var count in new[] { 0, 1, 2, 64 })
            {
                foreach (var representation in new[] { "Int32", "Guid", "Binary", "String" })
                {
                    cases.Add(representation, count);
                }
            }

            return cases;
        }
    }

    /// <summary>Three query forms preserve equality for one native key representation and cardinality.</summary>
    /// <param name="representation">The independently discovered mapped key representation.</param>
    /// <param name="count">The requested number of distinct existing keys.</param>
    [Theory]
    [MemberData(nameof(ComparisonCases))]
    public Task NativeContainsAndBalancedEqualityHaveEquivalentResults(
        string representation,
        int count
    ) => representation switch
    {
        "Int32" =>
            CompareSeededAsync<TreeNode, int>(representation, nameof(TreeNode.NodeId), count, static value => value),
        "Guid" => CompareSeededAsync<GuidNode, Guid>(
            representation,
            nameof(GuidNode.Id),
            count,
            static value => new Guid(
                value,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0)),
        "Binary" => CompareSeededAsync<BinaryNode, byte[]>(
            representation,
            nameof(BinaryNode.Id),
            count,
            BitConverter.GetBytes),
        "String" => CompareSeededAsync<TextNode, string>(
            representation,
            nameof(TextNode.Id),
            count,
            static value => $"Node-{value:D4}".ToLowerInvariant()),
        _ => throw new ArgumentOutOfRangeException(nameof(representation)),
    };

    /// <summary>Runs one comparison against a shared immutable seed with a fresh context and observer.</summary>
    private async Task CompareSeededAsync<TEntity, TKey>(
        string representation,
        string property,
        int count,
        Func<int, TKey> createKey
    )
        where TEntity : class
    {
        // Arrange
        var database = await _fixture.ComparisonAsync(Engine);
        var keys = Enumerable
            .Range(0, count)
            .Select(index => createKey((index * 4) + 1))
            .ToArray();

        var probe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = database.CreateContext(probe);
        var evidence = new List<string>();
        var expected = representation == "String" && Engine == "PostgreSql" ? 0 : count;

        // Act
        var (equality, contains, production) = await CompareAsync<TEntity, TKey>(
            context,
            probe,
            Engine,
            property,
            keys,
            evidence);

        _fixture.RecordEvidence(representation, count, evidence);

        // Assert
        Assert.Equal(expected, equality.Length);
        Assert.Equal(equality, contains);
        Assert.Equal(equality, production);
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
            .Where(node => Enumerable.Contains(keys, EF.Property<TKey>(node, property)));

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
                .ToArrayAsync(CancellationToken.None);

            var elapsed = Stopwatch.GetElapsedTime(start)
                .TotalMilliseconds;
            results[index] = rows
                .Select(value => value is byte[] bytes ? Convert.ToHexString(bytes) : value!.ToString()!)
                .Order(StringComparer.Ordinal)
                .ToArray();

            var plan = await QueryPlanTestSupport.ExplainAsync(context, probe, commandIndex, engine);
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
