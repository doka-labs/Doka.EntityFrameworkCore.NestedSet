using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Summarizes emitted iterator tuples in an executed MySQL TREE plan, independently of elapsed time.</summary>
/// <param name="MaximumOperatorTuples">The largest displayed actual rows multiplied by actual loops.</param>
/// <param name="MaximumMaterializedTuples">The largest materialization iterator's rows multiplied by loops.</param>
/// <param name="ExecutedIterators">The number of iterators with actual execution observations.</param>
/// <param name="RootOutputTuples">The first executed iterator's output used to check replay completion.</param>
/// <param name="SumIteratorOutputTuples">
/// The sum of displayed rows multiplied by loops across executed iterators.
/// </param>
/// <remarks>
/// These observations describe iterator tuple work, not physical page reads or rows rejected inside an index.
/// MySQL displays average rows per loop; multiplication recovers cumulative tuples, subject to display rounding.
/// The sum includes intermediate pipeline outputs: one logical row can contribute at several stages, so it
/// is neither a count of disjoint rows nor an independent measure of database I/O.
/// </remarks>
internal readonly record struct CompositionPlanWork(
    double MaximumOperatorTuples,
    double MaximumMaterializedTuples,
    int ExecutedIterators,
    double RootOutputTuples,
    double SumIteratorOutputTuples
)
{
    /// <summary>Reads actual TREE observations and rejects missing or malformed execution evidence.</summary>
    /// <param name="plan">The native plan, optionally preceded by estimated JSON output.</param>
    /// <returns>The maximum and summed cumulative tuple observations from executed iterators only.</returns>
    internal static CompositionPlanWork ReadMySqlTree(
        string plan
    )
    {
        var maximum = 0D;
        var materialized = 0D;
        var executed = 0;
        var rootOutput = 0D;
        var sum = 0D;
        using var reader = new StringReader(plan);

        while (reader.ReadLine() is { } line)
        {
            var marker = line.IndexOf("(actual time=", StringComparison.Ordinal);

            if (marker < 0)
            {
                continue;
            }

            var actual = line.AsSpan(marker);
            var rows = ReadNumber(actual, " rows=");
            var loops = ReadNumber(actual, " loops=");
            var tuples = rows * loops;

            if (!double.IsFinite(tuples))
            {
                throw new FormatException("The TREE iterator's cumulative tuple count is not finite.");
            }

            sum += tuples;

            if (!double.IsFinite(sum))
            {
                throw new FormatException("The TREE iterator output sum is not finite.");
            }

            maximum = Math.Max(maximum, tuples);

            if (executed == 0)
            {
                rootOutput = tuples;
            }

            executed++;

            if (line.Contains("-> Materialize ", StringComparison.OrdinalIgnoreCase)
                || line.Contains("-> Union materialize ", StringComparison.OrdinalIgnoreCase)
                || line.Contains("temporary table with deduplication", StringComparison.OrdinalIgnoreCase))
            {
                materialized = Math.Max(materialized, tuples);
            }
        }

        if (executed == 0)
        {
            throw new FormatException("The plan contains no executed MySQL TREE iterators.");
        }

        return new CompositionPlanWork(maximum, materialized, executed, rootOutput, sum);
    }

    /// <summary>Reads executed PostgreSQL JSON operators, including work rejected by join predicates.</summary>
    /// <param name="plan">The native EXPLAIN ANALYZE FORMAT JSON array for one statement.</param>
    /// <returns>The largest cumulative emitted and rejected-join tuple observations.</returns>
    internal static CompositionPostgreSqlPlanWork ReadPostgreSqlJson(string plan)
    {
        try
        {
            // WHY: Generated multi-stage EF plans can exceed JSON's default depth of 64. This changes only
            // diagnostic parsing; it neither limits nor changes the query or its native work allowance.
            using var document = JsonDocument.Parse(plan, new JsonDocumentOptions { MaxDepth = 1_024 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 1
                || root[0].ValueKind != JsonValueKind.Object || !root[0].TryGetProperty("Plan", out var first)
                || first.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The plan must contain one executed PostgreSQL statement.");
            }

            var pending = new Stack<JsonElement>();
            pending.Push(first);
            var maximumOutput = 0D;
            var maximumRejected = 0D;
            var executed = 0;
            var rootOutput = ReadPostgreSqlNumber(first, "Actual Rows", required: true)
                * ReadPostgreSqlNumber(first, "Actual Loops", required: true);

            while (pending.TryPop(out var node))
            {
                if (node.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("A PostgreSQL plan operator must be an object.");
                }

                var loops = ReadPostgreSqlNumber(node, "Actual Loops", required: true);
                var output = ReadPostgreSqlNumber(node, "Actual Rows", required: true) * loops;
                var rejected = ReadPostgreSqlNumber(node, "Rows Removed by Join Filter", required: false) * loops;
                if (!double.IsFinite(output) || !double.IsFinite(rejected))
                {
                    throw new FormatException("A PostgreSQL operator's cumulative tuple count is not finite.");
                }

                maximumOutput = Math.Max(maximumOutput, output);
                maximumRejected = Math.Max(maximumRejected, rejected);
                executed += loops > 0 ? 1 : 0;
                if (node.TryGetProperty("Plans", out var children))
                {
                    if (children.ValueKind != JsonValueKind.Array)
                    {
                        throw new FormatException("A PostgreSQL operator's Plans property must be an array.");
                    }

                    foreach (var child in children.EnumerateArray())
                    {
                        pending.Push(child);
                    }
                }
            }

            if (executed == 0)
            {
                throw new FormatException("The plan contains no executed PostgreSQL operators.");
            }

            return new CompositionPostgreSqlPlanWork(maximumOutput, maximumRejected, executed, rootOutput);
        }
        catch (JsonException error)
        {
            throw new FormatException("The PostgreSQL plan is not valid JSON execution evidence.", error);
        }
    }

    /// <summary>Requires finite nonnegative native observations and distinguishes absent optional counters.</summary>
    private static double ReadPostgreSqlNumber(JsonElement node, string field, bool required)
    {
        if (!node.TryGetProperty(field, out var value))
        {
            return required ? throw new FormatException("The PostgreSQL operator is missing " + field + ".") : 0D;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
            || !double.IsFinite(number) || number < 0)
        {
            throw new FormatException("The PostgreSQL operator has an invalid " + field + ".");
        }

        return number;
    }

    /// <summary>Finds repeated whole-tree index prefixes in the fixed scoped TreeNode performance fixture.</summary>
    /// <param name="plan">The read-derived MySQL TREE plan.</param>
    /// <returns>The largest actual loop count for generated index access bound only by scope and tree.</returns>
    /// <remarks>
    /// This is an access-path guard, not a physical-read counter. InnoDB index-condition pushdown can hide
    /// rejected entries from displayed actual rows and handler counters while repeating the full key prefix.
    /// </remarks>
    internal static double FullTreePrefixLoops(
        string plan
    )
    {
        var maximum = 0D;
        using var reader = new StringReader(plan);

        while (reader.ReadLine() is { } line)
        {
            var actual = line.IndexOf("(actual time=", StringComparison.Ordinal);
            var index = line.IndexOf(" using IX_NestedSet_", StringComparison.Ordinal);
            var lookup = line.Contains("index lookup on", StringComparison.OrdinalIgnoreCase);
            var range = line.Contains("index range scan on", StringComparison.OrdinalIgnoreCase);

            if (actual < 0 || index < 0 || (!lookup && !range))
            {
                continue;
            }

            var start = line.IndexOf('(', index);

            if (start < 0)
            {
                throw new FormatException("The executed generated-index access has no key prefix.");
            }

            var end = line.IndexOf(')', start);

            if (end < 0)
            {
                throw new FormatException("The executed generated-index access has no complete key prefix.");
            }

            var keys = line.AsSpan(start + 1, end - start - 1);
            const string rangePrefix = "Tree = 1 AND TreeId = ";
            var scopeOnlyLookup = lookup && keys.StartsWith("Tree=1, TreeId=", StringComparison.Ordinal)
                && keys[(keys.IndexOf(',') + 1)..].IndexOf(',') < 0;

            var scopeOnlyRange = range && keys.StartsWith(rangePrefix, StringComparison.Ordinal)
                && !keys[rangePrefix.Length..].Contains(" AND ", StringComparison.Ordinal)
                && !keys[rangePrefix.Length..].Contains(" OR ", StringComparison.Ordinal);

            // WHY: These tests own one complete Tree=1/TreeId=Empty population. A third lookup key or an
            // actual coordinate range narrows its search domain. Equality-only range scans still cover the
            // entire prefix even when a later index condition rejects most entries inside the storage engine.
            if (scopeOnlyLookup || scopeOnlyRange)
            {
                maximum = Math.Max(maximum, ReadNumber(line.AsSpan(actual), " loops="));
            }
        }

        return maximum;
    }

    /// <summary>Parses a nonnegative finite actual observation, including MySQL engineering notation.</summary>
    private static double ReadNumber(
        ReadOnlySpan<char> actual,
        string field
    )
    {
        var start = actual.IndexOf(field.AsSpan(), StringComparison.Ordinal);

        if (start < 0)
        {
            throw new FormatException("The executed TREE iterator is missing " + field.Trim() + ".");
        }

        var value = actual[(start + field.Length)..];
        var end = value.IndexOfAny(' ', ')');

        if (end >= 0)
        {
            value = value[..end];
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number) || number < 0)
        {
            throw new FormatException("The executed TREE iterator has an invalid " + field.Trim() + ".");
        }

        return number;
    }
}

/// <summary>Summarizes emitted tuples and tuples rejected by an executed PostgreSQL join predicate.</summary>
/// <param name="MaximumOperatorTuples">The largest actual rows multiplied by actual loops.</param>
/// <param name="MaximumRejectedJoinTuples">The largest join-filter rejections multiplied by actual loops.</param>
/// <param name="ExecutedOperators">The number of operators that ran at least once.</param>
/// <param name="RootOutputTuples">The complete statement's output used to check replay completion.</param>
internal readonly record struct CompositionPostgreSqlPlanWork(
    double MaximumOperatorTuples,
    double MaximumRejectedJoinTuples,
    int ExecutedOperators,
    double RootOutputTuples
);

/// <summary>Derives conservative tuple-work limits from independent fixture cardinalities.</summary>
/// <param name="OperatorTuples">The maximum emitted tuples permitted at any executed iterator.</param>
/// <param name="MaterializedTuples">The maximum materialized tuples permitted at any iterator.</param>
internal readonly record struct CompositionPlanWorkBudget(long OperatorTuples, long MaterializedTuples)
{
    /// <summary>Allows either selective candidate work or one population scan plus the requested pair bag.</summary>
    internal static CompositionPlanWorkBudget ForPairs(
        int nodes,
        int anchors,
        int facts,
        long contributions
    )
    {
        // WHY: The factor two tolerates rounded native observations and equivalent legitimate access paths,
        // while separating requested output from a whole-population materialization for a selective payload.
        var candidateWork = Math.Min((long)anchors * facts, nodes + contributions);

        return new CompositionPlanWorkBudget(
            checked(2 * (anchors + (long)facts + candidateWork)),
            checked(2 * (anchors + (long)facts + contributions)));
    }

    /// <summary>Allows one population validation plus the independently required selected path evidence.</summary>
    internal static CompositionPlanWorkBudget ForPaths(
        int nodes,
        int occurrences,
        long requiredEdges
    ) => new(
        checked(2 * (nodes + (long)occurrences + requiredEdges)),
        checked(2L * nodes));
}
