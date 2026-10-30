using System.Text.Json;
using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Attributes observed access tuples to the fixed hierarchy fixture's physical table aliases.</summary>
/// <param name="ObservedTuples">Summed physical access iterator tuples; these are not disk reads.</param>
/// <param name="PhysicalAccesses">The number of independently observed physical access leaves.</param>
/// <param name="UnexposedPreIndexAccesses">Accesses without an exposed pre-ICP count.</param>
internal readonly partial record struct AncestorIndexBaseTableWork(
    double ObservedTuples,
    int PhysicalAccesses,
    int UnexposedPreIndexAccesses
)
{
    /// <summary>Excludes derived relations while retaining every independently executed fixture-table access.</summary>
    internal static AncestorIndexBaseTableWork Read(
        string sql,
        string plan,
        string engine
    ) => ReadFixture(sql, plan, engine, "AncestorIndexQueryNodes");

    /// <summary>Attributes physical accesses to the existing unindexed TreeNode capacity fixture.</summary>
    internal static AncestorIndexBaseTableWork ReadUnindexed(
        string sql,
        string plan,
        string engine
    ) => ReadFixture(sql, plan, engine, "TreeNode");

    /// <summary>Uses the same strict alias and actual-work rules for the two explicitly known fixture tables.</summary>
    private static AncestorIndexBaseTableWork ReadFixture(
        string sql,
        string plan,
        string engine,
        string fixtureTable
    )
    {
        var bindings = BindingRegex().Matches(sql);
        var aliases = bindings
            .Where(match => match.Groups["table"].Value == fixtureTable)
            .Select(match => match.Groups["alias"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // WHY: This reader deliberately supports the emitted fixed fixture, rather than guessing SQL scopes.
        // A future reused base/derived alias must fail attribution instead of reporting a false zero.
        if (aliases.Count == 0
            || bindings
                .Where(match => aliases.Contains(match.Groups["alias"].Value))
                .GroupBy(match => match.Groups["alias"].Value, StringComparer.Ordinal)
                .Any(group => group.Count() != 1)
            || bindings.Any(match => aliases.Contains(match.Groups["alias"].Value)
                && match.Groups["table"].Value != fixtureTable)
            || AliasesRegex()
                .Matches(sql)
                .Any(match => aliases.Contains(match.Groups["alias"].Value)))
        {
            throw new FormatException("The fixture's base-table alias ownership is missing or ambiguous.");
        }

        var tuples = 0D;
        var accesses = 0;
        var unknown = 0;
        var accounted = new HashSet<string>(StringComparer.Ordinal);

        if (engine == "MySql")
        {
            foreach (var line in plan.Split('\n'))
            {
                var access = AccessRegex().Match(line);

                if (!access.Success
                    || !aliases.Contains(access.Groups["alias"].Value))
                {
                    continue;
                }

                accounted.Add(access.Groups["alias"].Value);

                if (line.Contains("(never executed)", StringComparison.Ordinal))
                {
                    continue;
                }

                // WHY: Reuse the established finite/nonnegative actual-number reader; Filter/hash outputs
                // are intentionally excluded, so a C lookup is not mistaken for another base-table visit.
                tuples += CompositionPlanWork.ReadMySqlTree(line).RootOutputTuples;
                accesses++;
                unknown++;
            }
        }
        else if (engine == "MariaDb")
        {
            using var document = JsonDocument.Parse(
                plan.Replace("\\x00", "\\u0000", StringComparison.Ordinal),
                new JsonDocumentOptions { MaxDepth = 1_024 });

            var observed = new Dictionary<string, (double Rows, bool UnknownPreIndex)>(StringComparer.Ordinal);
            var signatures =
                new Dictionary<string, (long? Block, double? Rows, double? Loops, double? IndexRows)>(
                    StringComparer.Ordinal);

            foreach (var (node, block) in Objects(document.RootElement))
            {
                if (!node.TryGetProperty("table_name", out var table)
                    || table.ValueKind != JsonValueKind.String
                    || !aliases.Contains(table.GetString()!))
                {
                    continue;
                }

                var alias = table.GetString()!;
                var signature = (Block: block, Rows: ReadNumber(node, "r_rows"), Loops: ReadNumber(node, "r_loops"),
                    IndexRows: ReadNumber(node, "r_index_rows"));

                if (signatures.TryGetValue(alias, out var previousSignature)
                    && (block is null || previousSignature != signature))
                {
                    throw new FormatException(
                        "Repeated fixture-table observations lack the same known SELECT block and counters; "
                        + "shared access cannot be attributed safely.");
                }

                signatures[alias] = signature;
                accounted.Add(alias);

                if (node.TryGetProperty("r_loops", out var loops)
                    && loops.ValueKind == JsonValueKind.Number
                    && loops.GetDouble() == 0)
                {
                    continue;
                }

                if (signature.Rows is not { } rows
                    || signature.Loops is not { } executions
                    || executions != Math.Truncate(executions)
                    || (node.TryGetProperty("r_index_rows", out _) && signature.IndexRows is null))
                {
                    throw new FormatException(
                        "The physical MariaDB access lacks complete actual row/loop observations.");
                }

                // WHY: MariaDB can repeat the same completed CTE definition's native observations
                // beneath several references. Matching counters alone are insufficient: cloned
                // nonrecursive references can belong to different SELECT blocks with equal cardinality.
                observed[alias] = ((signature.IndexRows ?? rows) * executions, signature.IndexRows is null);
            }

            tuples = observed.Sum(pair => pair.Value.Rows);
            accesses = observed.Count;
            unknown = observed.Count(pair => pair.Value.UnknownPreIndex);
        }
        else
        {
            throw new ArgumentOutOfRangeException(
                nameof(engine),
                engine,
                "The fixture reader supports MySQL and MariaDB only.");
        }

        if (accesses == 0
            || !double.IsFinite(tuples)
            || !aliases.SetEquals(accounted))
        {
            throw new FormatException(
                "The fixture plan lacks finite actual access evidence for its complete base-table inventory.");
        }

        return new AncestorIndexBaseTableWork(tuples, accesses, unknown);
    }

    /// <summary>Retains absent pre-index observations while rejecting malformed actual numbers.</summary>
    private static double? ReadNumber(
        JsonElement node,
        string name
    )
    {
        if (!node.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number)
            || number < 0)
        {
            throw new FormatException("The fixture plan contains an invalid actual access observation.");
        }

        return number;
    }

    /// <summary>Retains the enclosing SELECT identity without counting parent operators as physical accesses.</summary>
    private static IEnumerable<(JsonElement Node, long? Block)> Objects(
        JsonElement value,
        long? block = null
    )
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("select_id", out var identity))
            {
                if (identity.ValueKind != JsonValueKind.Number
                    || !identity.TryGetInt64(out var id)
                    || id <= 0)
                {
                    throw new FormatException("The MariaDB plan contains an invalid SELECT block identity.");
                }

                block = id;
            }

            yield return (value, block);

            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var property in value.EnumerateObject())
            {
                foreach (var child in Objects(property.Value, block))
                {
                    yield return child;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var item in value.EnumerateArray())
            {
                foreach (var child in Objects(item, block))
                {
                    yield return child;
                }
            }
        }
    }

    [GeneratedRegex(@"\b(?:FROM|JOIN) `(?<table>[^`]+)` AS `(?<alias>[^`]+)`")]
    private static partial Regex BindingRegex();

    [GeneratedRegex(@"\)\s+AS `(?<alias>[^`]+)`")]
    private static partial Regex AliasesRegex();

    [GeneratedRegex(
        @"-> (?:Single-row (?:covering )?index lookup|Covering index (?:range scan|scan|lookup)|Index (?:range scan|scan|lookup)|Table scan) on `?(?<alias>[A-Za-z_][A-Za-z0-9_]*)`?\b",
        RegexOptions.IgnoreCase,
        "en-DE")]
    private static partial Regex AccessRegex();
}
