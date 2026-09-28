namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Writes query-plan and command evidence for independent shared and provider-local suites.</summary>
internal static class QueryPlanTestSupport
{
    /// <summary>
    /// Retains provider plans outside source control for independent inspection and CI artifact collection.
    /// </summary>
    internal static async Task WriteEvidenceAsync(
        string name,
        IReadOnlyList<string> evidence
    )
    {
        var directory = Environment.GetEnvironmentVariable("DOKA_NESTEDSET_QUERY_PLAN_DIR")
            ?? Path.Combine(Path.GetTempPath(), "doka-nestedset-query-plans");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, name + ".txt"),
            string.Join("\n\n", evidence),
            CancellationToken.None);
    }
}
