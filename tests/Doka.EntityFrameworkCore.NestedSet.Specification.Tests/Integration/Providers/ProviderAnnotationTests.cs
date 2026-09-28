namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Prevents shared integration cases from silently bypassing fixture-owned provider coverage.</summary>
public abstract class ProviderAnnotationTests : ProviderTest
{
    /// <summary>Uses the concrete provider fixture as the immutable engine owner.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    protected ProviderAnnotationTests(
        IProviderFixture<ProviderResources> fixture
    ) : base(fixture) { }

    /// <summary>Shared ordinary tests run on every engine, with only valid, explained exclusions.</summary>
    [EngineFact(
        ExcludedEngines = ["MariaDb"],
        Reason = "MySql audits this same executable assembly and verifies both owned engine registrations.")]
    public async Task SharedTestAnnotationsPreserveProviderOwnership()
    {
        // Arrange
        var methods = typeof(ProviderAnnotationTests)
            .Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            .ToArray();

        var errors = new List<string>();

        // Act
        foreach (var method in methods)
        {
            errors.AddRange(ProviderTestContract.FindSharedAnnotationErrors(method));
            errors.AddRange(await ProviderTestContract.FindSharedDataErrorsAsync(method));
        }

        // Assert
        Assert.True(ProviderEngineOwnership.Includes(Engine));
        Assert.Empty(errors);
    }

    /// <summary>The common library defines abstract contracts, including nested suites.</summary>
    [EngineFact(
        ExcludedEngines = ["MariaDb"],
        Reason = "MySql audits this same executable assembly and verifies both owned engine registrations.")]
    public void SharedSuitesRemainAbstract()
    {
        // Arrange
        var assembly = typeof(ProviderAnnotationTests).Assembly;

        // Act
        var suites = assembly
            .GetTypes()
            .Where(ProviderTestContract.HasDeclaredTestCases)
            .ToArray();

        var concrete = suites
            .Where(type => !type.IsAbstract)
            .Select(type => type.FullName)
            .ToArray();

        // Assert
        Assert.True(ProviderEngineOwnership.Includes(Engine));
        Assert.NotEmpty(suites);
        Assert.Empty(concrete);
    }

    /// <summary>
    /// Every shared suite has one body-free concrete subclass per fixture engine with resolvable arguments.
    /// </summary>
    [EngineFact(
        ExcludedEngines = ["MariaDb"],
        Reason = "MySql audits this same executable assembly and verifies both owned engine registrations.")]
    public void ProviderSuitesPreserveOwnedSharedContracts()
    {
        // Arrange
        var shared = typeof(ProviderAnnotationTests).Assembly;
        var provider = GetType().Assembly;
        var engines = ProviderTestContract.OwnedEngines(provider.GetName().Name!);
        var candidates = provider
            .GetExportedTypes()
            .Where(type => !type.IsAbstract)
            .ToArray();

        var suites = shared
            .GetTypes()
            .Where(ProviderTestContract.HasDeclaredTestCases)
            .ToArray();

        var errors = new List<string>();

        // Act
        foreach (var suite in suites)
        {
            var wrappers = candidates
                .Where(suite.IsAssignableFrom)
                .ToArray();

            foreach (var engine in engines)
            {
                var matching = wrappers
                    .Where(type => EngineTestSelection.ResolveEngine(type) == engine)
                    .ToArray();

                if (matching.Length != 1)
                {
                    errors.Add($"{suite.FullName}: expected one '{engine}' wrapper, found {matching.Length}.");

                    continue;
                }

                var concrete = matching[0];

                if (suite.Name != concrete.Name
                    || !concrete.IsSealed)
                {
                    errors.Add($"{concrete.FullName}: shared suite wrappers must be sealed and preserve the name.");
                }

                if (ProviderTestContract.HasDeclaredTestCases(concrete))
                {
                    errors.Add($"{concrete.FullName}: wrapper duplicates inherited test bodies.");
                }

                // WHY: Inherited fixture interfaces prove nothing about whether the leaf's constructor is injectable.
                errors.AddRange(ProviderFixtureContract.FindErrors(concrete));
            }
        }

        // Assert
        Assert.True(ProviderEngineOwnership.Includes(Engine));
        Assert.NotEmpty(suites);
        Assert.Contains(suites, type => type.IsNested);
        Assert.Empty(errors);
    }

    /// <summary>
    /// Every local suite and every inherited contract receives its owning engine through an exact fixture.
    /// </summary>
    [EngineFact(
        ExcludedEngines = ["MariaDb"],
        Reason = "MySql audits this same executable assembly and verifies both owned engine registrations.")]
    public async Task ProviderAssemblyContainsOnlyOwnedSuites()
    {
        // Arrange
        var provider = GetType().Assembly;
        var assemblyName = provider.GetName().Name!;
        var suites = provider
            .GetExportedTypes()
            .Where(type => !type.IsAbstract
                && type
                    .GetMethods()
                    .Any(method =>
                        method.CustomAttributes.Any(attribute =>
                            typeof(IFactAttribute).IsAssignableFrom(attribute.AttributeType))))
            .ToArray();

        var errors = new List<string>();

        // Act
        foreach (var suite in suites)
        {
            errors.AddRange(await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, assemblyName));
            errors.AddRange(ProviderFixtureContract.FindErrors(suite));

            if (!ProviderTestContract.HasLocalTestCases(suite))
            {
                // WHY: Inherited cases have no provider opt-in rows; the immutable constructor fixture owns them all.
                _ = EngineTestSelection.ResolveEngine(suite);
            }
        }

        // Assert
        Assert.True(ProviderEngineOwnership.Includes(Engine));
        Assert.NotEmpty(suites);
        Assert.Empty(errors);
    }

    /// <summary>
    /// Server lifetime and named collection registrations belong to the executable provider assembly.
    /// </summary>
    [EngineFact(
        ExcludedEngines = ["MariaDb"],
        Reason = "MySql audits this same executable assembly and verifies both owned engine registrations.")]
    public void ProviderFixtureRegistrationsRemainLocal()
    {
        // Arrange
        var shared = typeof(ProviderAnnotationTests).Assembly;
        var provider = GetType()
            .Assembly;

        // Act
        var assemblyFixtures = provider
            .GetCustomAttributes<AssemblyFixtureAttribute>()
            .Select(attribute => attribute.AssemblyFixtureType)
            .ToArray();

        var definitions = provider
            .GetExportedTypes()
            .Select(type => (Type: type, Definition: type.GetCustomAttribute<CollectionDefinitionAttribute>()))
            .Where(entry => entry.Definition is not null)
            .ToArray();

        var model = definitions.Single(entry => entry.Definition!.Name == "Model compatibility");
        var registrationErrors = ProviderCollectionFixtureContract.FindErrors(
            model.Type,
            provider.GetName().Name!);

        // Assert
        Assert.True(ProviderEngineOwnership.Includes(Engine));
        Assert.Equal([typeof(TestDatabaseServers)], assemblyFixtures);
        Assert.Empty(shared.GetCustomAttributes<AssemblyFixtureAttribute>());
        Assert.DoesNotContain(
            shared.GetExportedTypes(),
            type => type.GetCustomAttribute<CollectionDefinitionAttribute>() is not null);

        Assert.Empty(registrationErrors);
        var allocation = Assert.Single(definitions, entry => entry.Definition!.Name == "Allocation measurements");
        Assert.True(allocation.Definition!.DisableParallelization);
    }
}
