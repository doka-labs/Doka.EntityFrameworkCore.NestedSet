namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderFixtureContractTests
{
    /// <summary>Creates standard reflection metadata with optional isolated assembly-fixture registrations.</summary>
    private static XunitTestClass CreateMetadata(
        Type subject,
        Type? definition = null,
        Type[]? assemblyFixtures = null
    )
    {
        var assembly = CreateAssembly(subject.Assembly);
        var collection = definition is null
            ? new CollectionPerClassTestCollectionFactory(assembly).Get(subject)
            : new XunitTestCollection(assembly, definition, false, "Constructor contract inventory");

        if (assemblyFixtures is not null)
        {
            // WHY: Resolve named collections in the real owner before isolating assembly registrations.
            assembly = CreateFixtureAssembly(assemblyFixtures);
            collection = new XunitTestCollection(
                assembly,
                collection.CollectionDefinition,
                collection.DisableParallelization,
                collection.TestCollectionDisplayName);
        }

        return new XunitTestClass(subject, collection);
    }

    /// <summary>Selects the current public metadata constructor without its obsolete overload.</summary>
    private static XunitTestAssembly CreateAssembly(
        Assembly assembly
    ) => new(
        assembly,
        configFilePath: null,
        assemblyName: assembly.GetName()
            .FullName);

    /// <summary>
    /// Attaches real assembly fixture attributes without modifying or initializing the Unit executable.
    /// </summary>
    private static XunitTestAssembly CreateFixtureAssembly(
        IReadOnlyCollection<Type> fixtureTypes
    )
    {
        var name = new AssemblyName($"NestedSetFixtureContract.{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var constructor = typeof(AssemblyFixtureAttribute).GetConstructor([typeof(Type)])!;

        foreach (var fixture in fixtureTypes)
        {
            // WHY: Public metadata reads the same attribute as the runner without a serializable metadata substitute.
            assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [fixture]));
        }

        return new XunitTestAssembly(
            assembly,
            configFilePath: null,
            assemblyName: name.FullName,
            assemblyPath: string.Empty);
    }
}
