namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Retains compiled, provider-generated migrations and the latest model snapshot.</summary>
public sealed class MigrationChain
{
    internal MigrationChain(
        IReadOnlyList<string> sources,
        string snapshot,
        IReadOnlyList<string> migrationIds,
        string latestMigrationCode,
        MigrationStage stage
    )
    {
        Sources = sources;
        Snapshot = snapshot;
        MigrationIds = migrationIds;
        LatestMigrationCode = latestMigrationCode;
        Stage = stage;
        Assembly = Compile(sources.Append(snapshot));
    }

    /// <summary>Gets the migration and metadata sources, excluding the replaceable latest snapshot.</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>Gets the generated current snapshot source.</summary>
    public string Snapshot { get; }

    /// <summary>Gets migration identifiers in creation order.</summary>
    public IReadOnlyList<string> MigrationIds { get; }

    /// <summary>Gets the latest full generated migration source for integration assertions.</summary>
    public string LatestMigrationCode { get; }

    /// <summary>Gets the latest snapshot's historical model version.</summary>
    public MigrationStage Stage { get; }

    /// <summary>Gets the compiled assembly consumed directly by EF's standard migration-assembly service.</summary>
    public Assembly Assembly { get; }

    /// <summary>Creates an actual generated migration through the configured provider's migration assembly.</summary>
    /// <param name="context">A context configured with this chain's assembly.</param>
    /// <param name="index">The zero-based migration index.</param>
    /// <returns>The generated migration including its actual Up and Down operations.</returns>
    public Migration GetMigration(
        DbContext context,
        int index
    )
    {
        var migrations = context.GetService<IMigrationsAssembly>();

        return migrations.CreateMigration(migrations.Migrations[MigrationIds[index]], context.Database.ProviderName!);
    }

    /// <summary>Compiles complete provider-generated sources against the assemblies they actually reference.</summary>
    private static Assembly Compile(
        IEnumerable<string> sources
    )
    {
        // WHY: Provider and optional integration generators can reference their own loaded public assemblies.
        // Reference their real binaries rather than reconstructing or substituting generated operations.
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        var loaded = AppDomain
            .CurrentDomain
            .GetAssemblies()
            .Where(x => !x.IsDynamic && !string.IsNullOrEmpty(x.Location))
            .Select(x => x.Location);

        var references = trusted
            .Concat(loaded)
            .Distinct(StringComparer.Ordinal)
            .Select(x => MetadataReference.CreateFromFile(x));

        var compilation = CSharpCompilation.Create(
            "NestedSetGeneratedMigrations_"
            + Guid
                .NewGuid()
                .ToString("N"),
            sources.Select(x => CSharpSyntaxTree.ParseText(x, cancellationToken: CancellationToken.None)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: CancellationToken.None);

        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error);

            throw new InvalidOperationException(
                "The actual generated migration source failed compilation:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors));
        }

        stream.Position = 0;

        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }
}
