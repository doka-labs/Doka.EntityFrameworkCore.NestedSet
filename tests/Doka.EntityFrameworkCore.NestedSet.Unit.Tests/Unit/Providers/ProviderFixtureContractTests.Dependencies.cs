namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderFixtureContractTests
{
    /// <summary>Fixture built-ins and dependencies at parent levels resolve without invoking any constructor.</summary>
    [Theory]
    [InlineData(typeof(FixtureBuiltinSubject), null)]
    [InlineData(typeof(ParentDependentSubject), typeof(CollectionFixtureDefinition))]
    public void FindErrors_SupportedFixtureArguments_AreAccepted(
        Type subject,
        Type? definition
    )
    {
        // Arrange
        var metadata = CreateMetadata(subject, definition);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// A class fixture can depend directly on an assembly fixture through the collection's parent chain.
    /// </summary>
    [Fact]
    public void FindErrors_FixtureDependency_ResolvesThroughAssemblyChain()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(ParentDependentSubject), assemblyFixtures: [typeof(Resource)]);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// A collection fixture can depend on an assembly fixture, then satisfy the concrete test constructor.
    /// </summary>
    [Fact]
    public void FindErrors_CollectionFixtureDependency_ResolvesFromAssembly()
    {
        // Arrange
        var metadata = CreateMetadata(
            typeof(DependencySubject),
            typeof(DependentCollectionDefinition),
            [typeof(Resource)]);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Fixtures cannot borrow same-level registrations, output helpers, or test-only fallback arguments.
    /// </summary>
    [Theory]
    [InlineData(typeof(SameLevelDependencySubject), "Resource resource")]
    [InlineData(typeof(OutputFixtureSubject), "ITestOutputHelper output")]
    [InlineData(typeof(DefaultFixtureSubject), "Resource resource")]
    [InlineData(typeof(OptionalFixtureSubject), "Resource resource")]
    [InlineData(typeof(ParamsFixtureSubject), "Object[] resources")]
    [InlineData(typeof(MultipleConstructorResource), "exactly one public constructor")]
    public void FindErrors_UnsupportedFixtureArguments_AreRejected(
        Type subject,
        string diagnostic
    )
    {
        // Arrange
        // WHY: The invalid fixture belongs to isolated metadata, outside the Unit runner's registration graph.
        var metadata = subject == typeof(MultipleConstructorResource)
            ? CreateMetadata(typeof(NoArgumentSubject), assemblyFixtures: [subject])
            : CreateMetadata(subject);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Contains(errors, error => error.Contains(diagnostic, StringComparison.Ordinal));
    }

    /// <summary>Assembly fixtures cannot use another assembly fixture as a constructor dependency.</summary>
    [Fact]
    public void FindErrors_AssemblyFixture_RejectsSameLevelDependency()
    {
        // Arrange
        var metadata = CreateMetadata(
            typeof(DependencySubject),
            assemblyFixtures: [typeof(Resource), typeof(DependentResource)]);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("Assembly fixture", StringComparison.Ordinal)
                && error.Contains("Resource resource", StringComparison.Ordinal));
    }

    /// <summary>A collection fixture cannot depend on a peer registered in the same collection.</summary>
    [Fact]
    public void FindErrors_CollectionFixture_RejectsSameLevelDependency()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(DependencySubject), typeof(SameLevelCollectionDefinition));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("Collection fixture", StringComparison.Ordinal)
                && error.Contains("Resource resource", StringComparison.Ordinal));
    }

    /// <summary>Invalid registered fixtures are checked even when the test constructor does not consume them.</summary>
    [Fact]
    public void FindErrors_UnusedRegisteredFixtureStill_HasToBeConstructible()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(NoArgumentSubject), assemblyFixtures: [typeof(DependentResource)]);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Contains(errors, error => error.Contains("Resource resource", StringComparison.Ordinal));
    }

    /// <summary>
    /// The audit reads signatures without constructing a fixture whose constructor deliberately throws.
    /// </summary>
    [Fact]
    public void FindErrors_StructuralAudit_DoesNotInitializeResources()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(ThrowingResourceSubject));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Empty(errors);
    }
}
