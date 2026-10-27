using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Retains actual native tuple observations without interpreting elapsed time as a work budget.</summary>
/// <param name="OperatorTuples">
/// MySQL emitted iterator tuples; MariaDB enumerated input, preferring pre-ICP r_index_rows over r_rows.
/// </param>
/// <param name="PostIndexTuples">MariaDB r_rows times r_loops after index filtering; unexposed on MySQL.</param>
/// <param name="MaterializedOutput">
/// The largest exposed cumulative materialized output, or null if none is exposed.
/// </param>
/// <param name="UnknownMaterializedOutputs">Materializations whose terminal output is not exposed.</param>
/// <param name="ExecutedOperators">The number of native operators with execution observations.</param>
/// <param name="RootRows">Native root output rows when the provider exposes that scalar.</param>
/// <param name="FullPrefixLoops">Repeated full-population index prefix loops, where exposed.</param>
internal readonly record struct DensePathNativeWork(
    double OperatorTuples,
    double? PostIndexTuples,
    double? MaterializedOutput,
    int UnknownMaterializedOutputs,
    int ExecutedOperators,
    double? RootRows,
    double? FullPrefixLoops
)
{
    /// <summary>Reuses the existing MySQL TREE reader and full-prefix access guard.</summary>
    internal static DensePathNativeWork MySql(
        string plan
    )
    {
        var work = CompositionPlanWork.ReadMySqlTree(plan);

        return new DensePathNativeWork(
            work.MaximumOperatorTuples,
            null,
            work.MaximumMaterializedTuples,
            0,
            work.ExecutedIterators,
            work.RootOutputTuples,
            CompositionPlanWork.FullTreePrefixLoops(plan));
    }

    /// <summary>Reads actual MariaDB JSON counters instead of estimates or elapsed milliseconds.</summary>
    /// <remarks>
    /// MariaDB reports average r_rows after index checks and optional r_index_rows before them. Multiplication
    /// by r_loops recovers cumulative input work. Materialized output instead uses filesort.r_output_rows times
    /// its filesort execution count. Native averages may be rounded, so this measures cumulative output work,
    /// not peak memory. Missing terminal output stays unknown rather than borrowing scanned input row counts.
    /// </remarks>
    internal static DensePathNativeWork MariaDb(
        string plan
    )
    {
        // WHY: MariaDB's native binary-zero literal uses a non-JSON \x00 escape. Normalize only that observed
        // fixture representation; the original native plan is retained unchanged before this numeric read.
        using var document = JsonDocument.Parse(plan.Replace("\\x00", "\\u0000", StringComparison.Ordinal));
        var observations = Objects(document.RootElement)
            .Select(Actual)
            .Where(value => value.HasValue)
            .Select(value => value.GetValueOrDefault())
            .ToArray();

        if (observations.Length == 0)
        {
            throw new FormatException("The MariaDB plan contains no actual row/loop observations.");
        }

        var materializations = Objects(document.RootElement)
            .Where(value => value.TryGetProperty("materialized", out _))
            .Select(value => MaterializedRows(value.GetProperty("materialized")))
            .ToArray();

        // WHY: An unrelated unexposed materialization must not hide an excessive known output.
        // Nullable Max keeps every observed maximum while the separate count records missing evidence.
        var materialized = materializations.Max();

        double? root = null;

        if (document.RootElement.TryGetProperty("query_block", out var block)
            && block.TryGetProperty("filesort", out var sort)
            && sort.TryGetProperty("r_output_rows", out var rows))
        {
            root = Number(rows);
        }

        return new DensePathNativeWork(
            observations.Max(value => value.Enumerated),
            observations.Max(value => value.PostIndex),
            materialized,
            materializations.Count(value => !value.HasValue),
            observations.Length,
            root,
            null);
    }

    /// <summary>Reads terminal materialization output without equating scanned input with stored rows.</summary>
    private static double? MaterializedRows(
        JsonElement value
    )
    {
        if (!value.TryGetProperty("query_block", out var block)
            || !block.TryGetProperty("filesort", out var sort)
            || !sort.TryGetProperty("temporary_table", out var temporary)
            || temporary.ValueKind != JsonValueKind.Object
            || !sort.TryGetProperty("r_output_rows", out var output))
        {
            return null;
        }

        if (!sort.TryGetProperty("r_loops", out var loops)
            && !block.TryGetProperty("r_loops", out loops))
        {
            throw new FormatException("The materialized output has no actual execution loop count.");
        }

        if (loops.ValueKind == JsonValueKind.Null
            && output.ValueKind == JsonValueKind.Null)
        {
            // WHY: MariaDB leaves both sort counters null when this materialization was never executed.
            // Missing output is unknown, so it cannot erase another relation's exposed maximum or prove zero.
            return null;
        }

        var executions = Loops(loops);

        if (executions == 0
            && output.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        return Product(Number(output), executions);
    }

    /// <summary>Walks standard JSON containers without depending on MariaDB's nested-loop wrapper names.</summary>
    private static IEnumerable<JsonElement> Objects(
        JsonElement value
    )
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            yield return value;

            foreach (var property in value.EnumerateObject())
            {
                foreach (var child in Objects(property.Value))
                {
                    yield return child;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var child in Objects(item))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>Rejects malformed counters while retaining explicitly unexecuted native branches as zero.</summary>
    private static (double Enumerated, double PostIndex)? Actual(
        JsonElement value
    )
    {
        if (!value.TryGetProperty("r_rows", out var rows))
        {
            if (value.TryGetProperty("r_index_rows", out _))
            {
                throw new FormatException("The MariaDB pre-index observation has no actual row count.");
            }

            return null;
        }

        if (!value.TryGetProperty("r_loops", out var loops))
        {
            throw new FormatException("The MariaDB actual row observation has no actual loop count.");
        }

        var count = Loops(loops);

        if (count == 0)
        {
            if (rows.ValueKind != JsonValueKind.Null)
            {
                _ = Number(rows);
            }

            if (value.TryGetProperty("r_index_rows", out var unusedIndexRows)
                && unusedIndexRows.ValueKind != JsonValueKind.Null)
            {
                _ = Number(unusedIndexRows);
            }

            return null;
        }

        var postIndex = Number(rows);
        var enumerated = value.TryGetProperty("r_index_rows", out var indexRows) ? Number(indexRows) : postIndex;

        return (Product(enumerated, count), Product(postIndex, count));
    }

    /// <summary>Requires complete executions rather than accepting a fractional invocation count.</summary>
    private static double Loops(
        JsonElement value
    )
    {
        var loops = Number(value);

        return loops != Math.Truncate(loops)
            ? throw new FormatException("The MariaDB actual loop count is not an integer.")
            : loops;
    }

    /// <summary>Rejects overflow rather than allowing an invalid observation to satisfy a finite work budget.</summary>
    private static double Product(
        double rows,
        double loops
    )
    {
        var tuples = rows * loops;

        return double.IsFinite(tuples)
            ? tuples
            : throw new FormatException("The MariaDB cumulative row observation is not finite.");
    }

    /// <summary>Requires finite, nonnegative actual numeric observations instead of accepting estimated text.</summary>
    private static double Number(
        JsonElement value
    )
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number)
            || number < 0)
        {
            throw new FormatException("The MariaDB actual work observation is not a nonnegative finite number.");
        }

        return number;
    }
}
