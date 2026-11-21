namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Three direction-changing legs keep every source, pivot, target, ancestor, and leg distance.</summary>
    /// <param name="requiredPaths">The number of ordered legs whose direct edges must be explicitly checked.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SelectAncestors_UpDownUp_RetainsTheCompleteWalkBag(
        int requiredPaths
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(
            scope,
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            hidden: null,
            sql: null,
            chainNodes: null);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(scope);

        IReadOnlyDictionary<int, int?> parents = new Dictionary<int, int?>
        {
            [1] = null,
            [2] = 1,
            [3] = 2,
            [4] = 2,
            [5] = 1,
            [6] = 5,
        };

        int[] sourceIds = [3, 4];
        var expected = sourceIds
            .SelectMany(source => Ancestors(parents, source, includeSelf: false), (source, pivot) => (source, pivot))
            .SelectMany(
                first => parents.Keys.Where(target => Ancestors(parents, target, includeSelf: true)
                    .Contains(first.pivot)),
                (first, target) => (first.source, first.pivot, target))
            .SelectMany(
                second => Ancestors(parents, second.target, includeSelf: true),
                (second, upper) =>
                    (Source: second.source, Pivot: second.pivot, Target: second.target, Upper: upper,
                        First: Distance(parents, second.pivot, second.source),
                        Second: Distance(parents, second.pivot, second.target),
                        Third: Distance(parents, upper, second.target)))
            .OrderBy(row => row.Source)
            .ThenBy(row => row.Pivot)
            .ThenBy(row => row.Target)
            .ThenBy(row => row.Upper)
            .ToArray();

        // Act
        var first = hierarchy.SelectAncestors(
            node => node.Id == 3 || node.Id == 4,
            (source, pivot, distance) => new
            {
                Source = source,
                Pivot = pivot,
                First = distance,
            });

        var second = hierarchy.SelectSubtrees(
            pivot => first.Any(row => row.Pivot == pivot),
            (pivot, target, distance) => new
            {
                Pivot = pivot,
                Target = target,
                Second = distance,
            });

        var joined = first.Join(
            second,
            row => row.Pivot,
            row => row.Pivot,
            (left, right) => new
            {
                left.Source,
                left.Pivot,
                right.Target,
                left.First,
                right.Second,
            });

        var third = hierarchy.SelectAncestorsOrSelf(
            target => joined.Any(row => row.Target == target),
            (target, upper, distance) => new
            {
                Target = target,
                Upper = upper,
                Third = distance,
            });

        var walks = joined.Join(
            third,
            row => row.Target,
            row => row.Target,
            (left, right) => new
            {
                left.Source,
                left.Pivot,
                left.Target,
                right.Upper,
                left.First,
                left.Second,
                right.Third,
            });

        if (requiredPaths >= 1)
        {
            walks = hierarchy.WherePathEdges(
                walks,
                row => row.Pivot,
                row => row.Source,
                (row, parent, child) => child.ParentId == parent.Id);
        }

        if (requiredPaths >= 2)
        {
            walks = hierarchy.WherePathEdges(
                walks,
                row => row.Pivot,
                row => row.Target,
                (row, parent, child) => child.ParentId == parent.Id);
        }

        if (requiredPaths >= 3)
        {
            walks = hierarchy.WherePathEdges(
                walks,
                row => row.Upper,
                row => row.Target,
                (row, parent, child) => child.ParentId == parent.Id);
        }

        var query = walks
            .Select(row => new
            {
                Source = row.Source.Id,
                Pivot = row.Pivot.Id,
                Target = row.Target.Id,
                Upper = row.Upper.Id,
                row.First,
                row.Second,
                row.Third,
            })
            .OrderBy(row => row.Source)
            .ThenBy(row => row.Pivot)
            .ThenBy(row => row.Target)
            .ThenBy(row => row.Upper);

        var translationStarted = Stopwatch.GetTimestamp();
        await using var command = query.CreateDbCommand();
        var translationElapsed = Stopwatch.GetElapsedTime(translationStarted);
        var evidence = new List<string>
        {
            FormattableString.Invariant($"translation={translationElapsed.TotalMilliseconds:F3}ms"),
            query.ToQueryString(),
        };

        var evidenceName = Engine + "-composition-up-down-up-" + requiredPaths;
        await QueryPlanTestSupport.WriteEvidenceAsync(
            evidenceName,
            evidence,
            cancellationToken: TestContext.Current.CancellationToken);

        if (Engine == "MySql")
        {
            // WHY: A planning-only probe separates optimizer time from execution without replaying this costly shape.
            command.CommandText = "EXPLAIN FORMAT=JSON " + command.CommandText;
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            var explainStarted = Stopwatch.GetTimestamp();
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

            while (await reader.ReadAsync(CancellationToken.None))
            {
                evidence.Add(reader.GetString(0));
            }

            evidence.Add(
                FormattableString.Invariant(
                    $"explain={Stopwatch.GetElapsedTime(explainStarted).TotalMilliseconds:F3}ms"));

            await QueryPlanTestSupport.WriteEvidenceAsync(
                evidenceName,
                evidence,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var executionStarted = Stopwatch.GetTimestamp();
        var actual = await query.ToArrayAsync(CancellationToken.None);
        evidence.Add(
            FormattableString.Invariant(
                $"execution={Stopwatch.GetElapsedTime(executionStarted).TotalMilliseconds:F3}ms"));

        await QueryPlanTestSupport.WriteEvidenceAsync(
            evidenceName,
            evidence,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expected,
            actual.Select(row => (row.Source, row.Pivot, row.Target, row.Upper, row.First, row.Second, row.Third)));

        Assert.True(actual.Length < 200);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }
}
