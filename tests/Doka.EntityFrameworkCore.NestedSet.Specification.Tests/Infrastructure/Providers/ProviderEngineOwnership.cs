namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps each executable integration test project to its owned database engines.</summary>
internal static class ProviderEngineOwnership
{
    private const string MySqlAssembly = "Doka.EntityFrameworkCore.NestedSet.MySql.Tests";

    // WHY: Read-only entries and storage prevent callers from changing any ownership projection independently.
    private static readonly IReadOnlyList<ProviderEngineDefinition> s_engines =
        Array.AsReadOnly<ProviderEngineDefinition>(
        [
            Define<MySqlEngine>(MySqlAssembly),
            Define<MariaDbEngine>(MySqlAssembly),
            Define<PostgreSqlEngine>("Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests"),
            Define<SqlServerEngine>("Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests"),
            Define<SqliteEngine>("Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests"),
        ]);

    /// <summary>Gets the immutable built-in name, marker, and executable ownership catalog.</summary>
    internal static IReadOnlyList<ProviderEngineDefinition> Engines => s_engines;

    /// <summary>Returns whether the current test assembly owns the specified provider case.</summary>
    internal static bool Includes(
        string engine
    )
    {
        // WHY: The referenced helper serves every provider; xUnit's current assembly owns runtime selection.
        var assembly = TestContext.Current.TestAssembly?.AssemblyName
            ?? throw new InvalidOperationException("No current integration test assembly is available.");

        return Includes(engine, new AssemblyName(assembly).Name!);
    }

    /// <summary>Maps a provider case to its test assembly without depending on test execution.</summary>
    internal static bool Includes(
        string engine,
        string assembly
    )
    {
        // WHY: Validate before filtering, so a misspelled engine cannot disappear from every provider project.
        var definition = s_engines.FirstOrDefault(entry => entry.Name == engine)
            ?? throw new InvalidOperationException($"Unknown integration test provider '{engine}'.");

        ValidateAssembly(assembly);

        return definition.AssemblyName == assembly;
    }

    /// <summary>Identifies the exact provider names accepted by the shared database fixture.</summary>
    internal static bool IsKnown(
        string? engine
    ) => s_engines.Any(entry => entry.Name == engine);

    /// <summary>Gets every built-in definition belonging to a known executable test assembly.</summary>
    internal static IEnumerable<ProviderEngineDefinition> OwnedBy(
        string assemblyName
    )
    {
        ValidateAssembly(assemblyName);

        return s_engines.Where(entry => entry.AssemblyName == assemblyName);
    }

    /// <summary>Rejects unknown executables before an empty projection could hide a missing owner.</summary>
    private static void ValidateAssembly(
        string assemblyName
    )
    {
        if (s_engines.All(entry => entry.AssemblyName != assemblyName))
        {
            throw new InvalidOperationException($"Unknown integration test assembly '{assemblyName}'.");
        }
    }

    /// <summary>Reads a built-in marker's static identity without constructing a fixture or resource.</summary>
    private static ProviderEngineDefinition Define<TEngine>(
        string assemblyName
    )
        where TEngine : struct, IProviderEngine => new(TEngine.Name, typeof(TEngine), assemblyName);
}
