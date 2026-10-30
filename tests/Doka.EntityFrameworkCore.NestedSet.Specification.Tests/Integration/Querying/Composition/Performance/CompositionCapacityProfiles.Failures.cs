namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionCapacityProfiles
{
    /// <summary>Retains original failures and diagnostic plans without changing the qualification outcome.</summary>
    /// <param name="context">The failed query's provider context.</param>
    /// <param name="engine">The native engine used for an optional estimated plan.</param>
    /// <param name="profile">The unique synthetic workload evidence name.</param>
    /// <param name="probe">The original statements and native bindings captured before execution.</param>
    /// <param name="failure">The original failure, retained even if estimated-plan capture fails.</param>
    /// <param name="commandIndex">The query's index after any fixture commands.</param>
    /// <returns>A task completing after the original failure and available diagnostics are retained.</returns>
    internal static async Task RetainFailureAsync(
        DbContext context,
        string engine,
        string profile,
        EnterpriseProbe probe,
        Exception failure,
        int commandIndex = 0
    )
    {
        var evidence = new List<string> { profile, failure.ToString() };

        if (probe.Commands.Count > commandIndex)
        {
            evidence.Add(probe.Commands[commandIndex]);
        }

        try
        {
            if ((engine is "PostgreSql" or "MySql" or "MariaDb") && probe.Commands.Count > commandIndex)
            {
                await context.Database.OpenConnectionAsync(CancellationToken.None);
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = (engine == "PostgreSql" ? "EXPLAIN (FORMAT JSON) " : "EXPLAIN FORMAT=JSON ")
                    + probe.Commands[commandIndex];

                // WHY: A failed actual-plan replay may still own its bindings; independent native clones
                // preserve provider metadata without attaching one parameter to two command collections.
                foreach (var parameter in probe.ParameterBindings[commandIndex])
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
            // WHY: An estimated-plan failure must not replace the original command timeout or cancellation.
            evidence.Add("Estimated-plan capture also failed: " + diagnosticFailure);
        }

        await QueryPlanTestSupport.WriteEvidenceAsync(
            engine + "-composition-capacity-failed-" + profile, evidence);
    }
}
