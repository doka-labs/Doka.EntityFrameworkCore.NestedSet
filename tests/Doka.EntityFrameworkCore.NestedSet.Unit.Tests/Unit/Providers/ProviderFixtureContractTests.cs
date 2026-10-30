namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks constructor support against xUnit's public reflection metadata without creating resources.</summary>
public sealed partial class ProviderFixtureContractTests
{
    /// <summary>
    /// Inherited class fixtures satisfy the actual concrete constructor rather than its base constructor.
    /// </summary>
    [Fact]
    public void InheritedClassFixtureResolvesConcreteConstructor()
    {
        // Arrange
        var subject = typeof(InheritedFixtureSubject);

        // Act
        var errors = ProviderFixtureContract.FindErrors(subject);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Inheriting a valid fixture interface cannot hide a different unsupported leaf constructor argument.
    /// </summary>
    [Fact]
    public void InheritedRegistrationCannotHideWrongLeafConstructor()
    {
        // Arrange
        var subject = typeof(WrongLeafFixtureSubject);

        // Act
        var errors = ProviderFixtureContract.FindErrors(subject);

        // Assert
        Assert.Contains(typeof(IClassFixture<Resource>), subject.GetInterfaces());
        Assert.Contains(errors, error => error.Contains("DerivedResource resource", StringComparison.Ordinal));
    }

    /// <summary>
    /// Class and collection fixture registrations from the resolved definition both support injection.
    /// </summary>
    [Theory]
    [InlineData(typeof(ClassFixtureDefinition))]
    [InlineData(typeof(CollectionFixtureDefinition))]
    public void DefinitionFixtureResolvesConcreteConstructor(
        Type definition
    )
    {
        // Arrange
        var metadata = CreateMetadata(typeof(ResourceSubject), definition);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Assembly registration supports exact injection even when no class or collection fixture exists.
    /// </summary>
    [Fact]
    public void AssemblyFixtureResolvesConcreteConstructor()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(ResourceSubject), assemblyFixtures: [typeof(Resource)]);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Equal([typeof(Resource)], metadata.TestCollection.TestAssembly.AssemblyFixtureTypes);
        Assert.Empty(errors);
    }

    /// <summary>Removing the required assembly registration makes the same constructor unsupported.</summary>
    [Fact]
    public void MissingAssemblyFixtureRejectsConcreteConstructor()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(ResourceSubject));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Contains(errors, error => error.Contains("Resource resource", StringComparison.Ordinal));
    }

    /// <summary>
    /// Partial open-generic fixture registrations resolve a closed request through its generic definition.
    /// </summary>
    [Fact]
    public void GenericFixtureDefinitionResolvesClosedConstructor()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(GenericResourceSubject), typeof(GenericCollectionDefinition<>));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A directly registered open generic cannot be eagerly constructed as an assembly fixture.</summary>
    [Fact]
    public void DirectOpenGenericAssemblyFixtureIsRejected()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(GenericResourceSubject), assemblyFixtures: [typeof(GenericResource<>)]);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("Assembly fixture", StringComparison.Ordinal)
                && error.Contains("concrete and closed", StringComparison.Ordinal));
    }

    /// <summary>The two exact test built-ins and legitimate test-constructor fallback rules remain supported.</summary>
    [Theory]
    [InlineData(typeof(BuiltinSubject))]
    [InlineData(typeof(DefaultArgumentSubject))]
    [InlineData(typeof(OptionalArgumentSubject))]
    [InlineData(typeof(ParamsArgumentSubject))]
    public void SupportedTestArgumentsAreAccepted(
        Type subject
    )
    {
        // Arrange
        var metadata = CreateMetadata(subject);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Static test classes do not require a public instance constructor.</summary>
    [Fact]
    public void StaticClassNeedsNoInstanceConstructor()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(StaticSubject));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Null(metadata.Constructors);
        Assert.Empty(errors);
    }

    /// <summary>
    /// Nullable annotations, fixture-only built-ins, and assignable interfaces cannot supply test arguments.
    /// </summary>
    [Theory]
    [InlineData(typeof(NullableArgumentSubject), "Resource resource")]
    [InlineData(typeof(MessageSinkSubject), "IMessageSink messageSink")]
    [InlineData(typeof(TestContextSubject), "ITestContext context")]
    [InlineData(typeof(DerivedOutputSubject), "IExtendedOutput output")]
    [InlineData(typeof(AssignableFixtureSubject), "Resource resource")]
    public void UnsupportedTestArgumentsAreRejected(
        Type subject,
        string missingArgument
    )
    {
        // Arrange
        var metadata = CreateMetadata(subject);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Contains(errors, error => error.Contains(missingArgument, StringComparison.Ordinal));
    }

    /// <summary>Malformed concrete constructor and fixture registration shapes produce useful diagnostics.</summary>
    [Theory]
    [InlineData(typeof(NoPublicConstructorSubject), "exactly one public constructor")]
    [InlineData(typeof(MultipleConstructorSubject), "exactly one public constructor")]
    [InlineData(typeof(MisplacedCollectionFixtureSubject), "ICollectionFixture")]
    public void InvalidTestClassShapesAreRejected(
        Type subject,
        string diagnostic
    )
    {
        // Arrange
        var metadata = CreateMetadata(subject);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Contains(errors, error => error.Contains(diagnostic, StringComparison.Ordinal));
    }

    /// <summary>A collection attribute on a base suite resolves the provider's public named definition.</summary>
    [Fact]
    public void InheritedNamedCollectionSuppliesFixture()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(InheritedCollectionSubject));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Equal(typeof(NamedDefinition), metadata.TestCollection.CollectionDefinition);
        Assert.Empty(errors);
    }

    /// <summary>
    /// A nested class does not inherit its enclosing class's collection or its fixture registrations.
    /// </summary>
    [Fact]
    public void NestedClassCannotBorrowEnclosingCollection()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(CollectionOuter.NestedSubject));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Null(metadata.TestCollection.CollectionDefinition);
        Assert.Contains(errors, error => error.Contains("Resource resource", StringComparison.Ordinal));
    }

    /// <summary>
    /// An unknown named collection remains fixture-free and cannot resolve a required constructor argument.
    /// </summary>
    [Fact]
    public void MissingNamedDefinitionRejectsRequiredFixture()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(UnknownCollectionSubject));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Null(metadata.TestCollection.CollectionDefinition);
        Assert.Contains(errors, error => error.Contains("Resource resource", StringComparison.Ordinal));
    }

    /// <summary>
    /// An inherited type-based attribute can resolve a definition outside the metadata's assembly inventory.
    /// </summary>
    [Fact]
    public void ExternalTypeBasedCollectionSuppliesFixture()
    {
        // Arrange
        // WHY: A library-owned metadata inventory isolates the external-definition fallback from named lookup.
        var assembly = CreateAssembly(typeof(ProviderFixtureContract).Assembly);
        var collection = new CollectionPerClassTestCollectionFactory(assembly).Get(typeof(ExternalCollectionSubject));
        var metadata = new XunitTestClass(typeof(ExternalCollectionSubject), collection);

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.NotSame(assembly.Assembly, collection.CollectionDefinition!.Assembly);
        Assert.Equal(typeof(CollectionFixtureDefinition), collection.CollectionDefinition);
        Assert.Empty(errors);
    }
}
