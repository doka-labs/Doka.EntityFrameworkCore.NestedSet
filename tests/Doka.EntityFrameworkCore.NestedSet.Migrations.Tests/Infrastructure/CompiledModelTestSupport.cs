using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Compiles actual provider-generated runtime models for independent native consumer regressions.</summary>
internal static class CompiledModelTestSupport
{
    /// <summary>Uses the provider's real EF generator, then compiles and loads its complete source output.</summary>
    internal static IModel Generate(
        DbContext context
    )
    {
        var services = new ServiceCollection();
        services.AddDbContextDesignTimeServices(context);
        var provider = Assembly.Load(context.Database.ProviderName!);
        var attribute = provider.GetCustomAttribute<DesignTimeProviderServicesAttribute>()
            ?? throw new InvalidOperationException("The provider does not declare design-time services.");

        var type = provider.GetType(attribute.TypeName, throwOnError: true)!;
        ((IDesignTimeServices)Activator.CreateInstance(type)!).ConfigureDesignTimeServices(services);
        services.AddEntityFrameworkDesignTimeServices();
        using var serviceProvider = services.BuildServiceProvider();
        var options = new CompiledModelCodeGenerationOptions
        {
            ContextType = context.GetType(),
            ModelNamespace = "Doka.Generated.AncestorCompiled",
            Language = "C#",
            UseNullableReferenceTypes = true,
        };

        var generator = serviceProvider
            .GetRequiredService<ICompiledModelCodeGeneratorSelector>()
            .Select(options);

        var files = generator
            .GenerateModel(
                context.GetService<IDesignTimeModel>()
                    .Model,
                options)
            .ToArray();

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

        // WHY: A real compilation catches unsupported annotation serialization and missing generated metadata;
        // reusing a hand-built runtime model would only test the fixture's own reconstruction.
        var compilation = CSharpCompilation.Create(
            "AncestorCompiled_" + Guid.NewGuid().ToString("N"),
            files.Select(x => CSharpSyntaxTree.ParseText(x.Code, cancellationToken: CancellationToken.None)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: CancellationToken.None);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                "The actual generated runtime model failed compilation: "
                + string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)));
        }

        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var generated = assembly.GetType(
            "Doka.Generated.AncestorCompiled."
            + context.GetType().Name
            + "Model",
            throwOnError: true)!;

        return (IModel)generated.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }
}
