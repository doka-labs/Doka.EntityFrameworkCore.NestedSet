using System.Xml;
using System.Xml.Linq;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Retains cumulative SQL Server iterator output and reads before residual filtering.</summary>
/// <param name="MaximumOperatorTuples">The largest actual output summed across one operator's threads.</param>
/// <param name="MaximumOperatorRowsRead">The largest reported pre-filter read count across one operator's threads.</param>
/// <param name="ExecutedOperators">The number of operators with at least one actual execution.</param>
internal readonly record struct CompositionSqlServerPlanWork(
    double MaximumOperatorTuples,
    double MaximumOperatorRowsRead,
    int ExecutedOperators
)
{
    /// <summary>Rejects population replay using the existing independent path-work allowance.</summary>
    /// <param name="plan">The actual native XML plan of the measured statement.</param>
    /// <param name="budget">The unchanged cardinality-derived allowance for one population pass and path evidence.</param>
    /// <param name="profile">The synthetic profile retained with its native plan.</param>
    internal static void AssertWithinBudget(string plan, CompositionPlanWorkBudget budget, string profile)
    {
        var work = ReadXml(plan);

        // WHY: Correct final rows do not expose discarded index entries. Guard both iterator output
        // and pre-filter reads so the previously observed repeated PK scans cannot silently recur.
        Assert.True(work.MaximumOperatorTuples <= budget.OperatorTuples
            && work.MaximumOperatorRowsRead <= budget.OperatorTuples,
            FormattableString.Invariant(
                $"{profile}: output={work.MaximumOperatorTuples}; reads={work.MaximumOperatorRowsRead}; maximum={budget.OperatorTuples}."));
    }

    /// <summary>Reads actual Showplan counters without mistaking estimates or repeated executions for rows.</summary>
    /// <param name="plan">The complete native Showplan XML returned by the existing replay helper.</param>
    /// <returns>Cumulative work observations from the executed statement.</returns>
    /// <remarks>
    /// SQL Server reports cumulative rows per thread, unlike PostgreSQL's per-loop averages. Multiplying
    /// by ActualExecutions would count the same work again. ActualRowsRead exposes work hidden by residual
    /// predicates; a zero-output seek may omit it, so only reported pre-filter counters contribute.
    /// </remarks>
    internal static CompositionSqlServerPlanWork ReadXml(string plan)
    {
        try
        {
            using var input = new StringReader(plan);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });

            var document = XDocument.Load(reader);
            XNamespace showplan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

            if (document.Root?.Name != showplan + "ShowPlanXML")
            {
                throw new FormatException("The plan must be SQL Server Showplan XML.");
            }

            var maximumOutput = 0D;
            var maximumRead = 0D;
            var executed = 0;

            foreach (var operation in document.Descendants(showplan + "RelOp"))
            {
                var output = 0D;
                var read = 0D;
                var ran = false;
                var readsStorage = operation.Attribute("PhysicalOp")?.Value is
                    "Index Scan" or "Index Seek" or "Clustered Index Scan" or "Clustered Index Seek" or "Table Scan";

                foreach (var counter in operation.Elements(showplan + "RunTimeInformation")
                             .Elements(showplan + "RunTimeCountersPerThread"))
                {
                    var executions = Number(counter, "ActualExecutions", required: true);
                    var rows = Number(counter, "ActualRows", required: true);
                    var rowsRead = Number(counter, "ActualRowsRead", required: readsStorage && rows > 0);
                    output += rows;
                    read += rowsRead;
                    ran |= executions > 0;
                }

                if (!double.IsFinite(output) || !double.IsFinite(read))
                {
                    throw new FormatException("The SQL Server operator's cumulative work is not finite.");
                }

                maximumOutput = Math.Max(maximumOutput, output);
                maximumRead = Math.Max(maximumRead, read);
                executed += ran ? 1 : 0;
            }

            if (executed == 0)
            {
                throw new FormatException("The plan contains no executed SQL Server operators.");
            }

            return new CompositionSqlServerPlanWork(maximumOutput, maximumRead, executed);
        }
        catch (XmlException error)
        {
            throw new FormatException("The SQL Server plan is not valid XML execution evidence.", error);
        }
    }

    /// <summary>Rejects absent required, negative, nonnumeric and nonfinite native counters.</summary>
    private static double Number(XElement counter, string field, bool required)
    {
        var value = counter.Attribute(field);

        if (value is null)
        {
            return required ? throw new FormatException("The SQL Server operator is missing " + field + ".") : 0D;
        }

        if (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number) || number < 0)
        {
            throw new FormatException("The SQL Server operator has an invalid " + field + ".");
        }

        return number;
    }
}
