using System.Text;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class GroupSupervisorTests
{
    /// <summary>Records an estimated failure plan without repeating the timed-out payload operation.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="context">The context whose open connection supplies the estimated plan.</param>
    /// <param name="probe">The observer holding the original SQL and parameter bindings.</param>
    /// <param name="index">The original command's position in the observer.</param>
    private async Task<string> EstimatedOriginPlanAsync(
        App.UserGroupContext context,
        EnterpriseProbe probe,
        int index,
        CancellationToken cancellationToken
    )
    {
        if (Engine is not ("MySql" or "MariaDb" or "PostgreSql"))
        {
            return "Estimated failure plan is available for server capacity engines only.";
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = (Engine == "PostgreSql"
                ? "EXPLAIN (FORMAT JSON) "
                : "EXPLAIN FORMAT=JSON ")
            + probe.Commands[index];

        foreach (var parameter in probe.ParameterBindings[index])
        {
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var output = new StringBuilder();
        while (await reader.ReadAsync(cancellationToken))
        {
            output.AppendLine(reader.GetString(0));
        }

        return output.ToString();
    }
}
