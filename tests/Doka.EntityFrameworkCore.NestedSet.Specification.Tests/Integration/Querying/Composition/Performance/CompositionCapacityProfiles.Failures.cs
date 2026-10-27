namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionCapacityProfiles
{
    /// <summary>Retains original failures and diagnostic plans without changing the qualification outcome.</summary>
    private static async Task RetainFailureAsync(
        TreeContext context,
        string engine,
        string profile,
        EnterpriseProbe probe,
        Exception failure
    )
    {
        var evidence = new List<string> { profile, failure.ToString() };

        if (probe.Commands.Count != 0)
        {
            evidence.Add(probe.Commands[0]);
        }

        try
        {
            if ((engine is "PostgreSql" or "MySql" or "MariaDb") && probe.Commands.Count != 0)
            {
                await context.Database.OpenConnectionAsync(CancellationToken.None);
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = (engine == "PostgreSql" ? "EXPLAIN (FORMAT JSON) " : "EXPLAIN FORMAT=JSON ")
                    + probe.Commands[0];

                // WHY: A failed actual-plan replay may still own its bindings; independent native clones
                // preserve provider metadata without attaching one parameter to two command collections.
                foreach (var parameter in probe.ParameterBindings[0])
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
