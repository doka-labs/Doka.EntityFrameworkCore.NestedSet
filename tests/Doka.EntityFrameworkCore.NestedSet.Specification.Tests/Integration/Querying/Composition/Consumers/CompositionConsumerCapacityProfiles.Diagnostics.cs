using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionConsumerCapacityProfiles
{
    /// <summary>Retains the failed reader and a nonexecuting estimate without replaying its report.</summary>
    private static async Task RetainReadFailureAsync(
        DbContext context,
        string engine,
        string profile,
        EnterpriseProbe probe,
        Exception failure
    )
    {
        var evidence = new List<string>
        {
            profile,
            ModelBoundary,
            failure.ToString(),
        };

        var index = probe.Commands.Count - 1;

        if (index >= 0)
        {
            evidence.Add(probe.Commands[index]);
        }

        try
        {
            if (index >= 0)
            {
                evidence.AddRange(
                    probe
                        .ParameterBindings[index]
                        .Select(parameter =>
                            $"{parameter.ParameterName}; type={parameter.GetType().Name}; dbType={parameter.DbType}; "
                            + $"size={parameter.Size}; value={JsonSerializer.Serialize(parameter.Value)}"));
            }

            if (index >= 0
                && engine is "PostgreSql" or "MySql" or "MariaDb")
            {
                // WHY: Diagnostic retention completes after failed work, including an already canceled request.
                await context.Database.OpenConnectionAsync(CancellationToken.None);
                await using var command = context
                    .Database
                    .GetDbConnection()
                    .CreateCommand();

                command.CommandText = (engine == "PostgreSql"
                        ? "EXPLAIN (FORMAT JSON) "
                        : "EXPLAIN FORMAT=JSON ")
                    + probe.Commands[index];
                command.CommandTimeout = 0;

                // WHY: The original report failed. EXPLAIN without ANALYZE plans that exact command without
                // rerunning its reads, and independent clones retain native array/date/key parameter types.
                foreach (var parameter in probe.ParameterBindings[index])
                {
                    command.Parameters.Add(((ICloneable)parameter).Clone());
                }

                await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

                while (await reader.ReadAsync(CancellationToken.None))
                {
                    evidence.Add(reader.GetString(0));
                }
            }
        }
        catch (Exception diagnosticFailure)
        {
            // WHY: Diagnostic plan failures cannot replace the original command failure or cancellation.
            evidence.Add("Parameter or estimated-plan capture also failed: " + diagnosticFailure);
        }

        try
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                engine + "-composition-consumer-failed-" + profile,
                evidence,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception diagnosticFailure)
        {
            // WHY: An unavailable evidence directory must also preserve the actual qualification failure.
            failure.Data["ConsumerCapacityEvidenceFailure"] = diagnosticFailure.ToString();
        }
    }

    /// <summary>Retains plans and diagnostic duration without introducing a hardware-dependent release gate.</summary>
    private static Task WriteAsync(
        string engine,
        CompositionConsumerCapacityObservation observation,
        string command,
        CancellationToken cancellationToken
    )
        => QueryPlanTestSupport.WriteEvidenceAsync(
            engine + "-composition-consumer-" + observation.Profile,
            [
                $"{observation.Profile}; commands={observation.Commands}; rows={observation.Actual.Length}; "
                + $"parameters={observation.MaximumParameters}; "
                + $"entities={observation.Materialized}; "
                + FormattableString.Invariant($"elapsed={observation.Elapsed.TotalMilliseconds:F3} ms"),
                ModelBoundary,
                command,
                observation.Plan,
            ],
            cancellationToken: cancellationToken);
}
