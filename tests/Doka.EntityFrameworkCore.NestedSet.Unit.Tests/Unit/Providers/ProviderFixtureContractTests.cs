using System.Runtime.InteropServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks constructor support against xUnit's public reflection metadata without creating resources.</summary>
public sealed class ProviderFixtureContractTests
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

    /// <summary>Fixture built-ins and dependencies at parent levels resolve without invoking any constructor.</summary>
    [Theory]
    [InlineData(typeof(FixtureBuiltinSubject), null)]
    [InlineData(typeof(ParentDependentSubject), typeof(CollectionFixtureDefinition))]
    public void SupportedFixtureArgumentsAreAccepted(
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
    public void FixtureDependencyResolvesThroughAssemblyChain()
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
    public void CollectionFixtureDependencyResolvesFromAssembly()
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
    public void UnsupportedFixtureArgumentsAreRejected(
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
    public void AssemblyFixtureRejectsSameLevelDependency()
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
    public void CollectionFixtureRejectsSameLevelDependency()
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
    public void UnusedRegisteredFixtureStillHasToBeConstructible()
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
    public void StructuralAuditDoesNotInitializeResources()
    {
        // Arrange
        var metadata = CreateMetadata(typeof(ThrowingResourceSubject));

        // Act
        var errors = ProviderFixtureContract.FindErrors(metadata);

        // Assert
        Assert.Empty(errors);
    }

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

    /// <summary>Represents a harmless exact fixture type.</summary>
    public class Resource { }

    /// <summary>Represents an assignable fixture that is not registered under its base type.</summary>
    public sealed class DerivedResource : Resource { }

    /// <summary>Represents a generic fixture specialization.</summary>
    public sealed class GenericResource<T> { }

    /// <summary>Registers a class fixture on a collection definition.</summary>
    public sealed class ClassFixtureDefinition : IClassFixture<Resource> { }

    /// <summary>Registers a collection fixture on a definition.</summary>
    public sealed class CollectionFixtureDefinition : ICollectionFixture<Resource> { }

    /// <summary>Registers a partially open fixture that xUnit normalizes to its generic definition.</summary>
    public sealed class GenericCollectionDefinition<T> : ICollectionFixture<GenericResource<T>> { }

    /// <summary>Registers a named collection in the Unit executable assembly.</summary>
    [CollectionDefinition("Constructor contract named definition")]
    public sealed class NamedDefinition : ICollectionFixture<Resource> { }

    /// <summary>Registers a collection fixture which requires an assembly-level dependency.</summary>
    public sealed class DependentCollectionDefinition : ICollectionFixture<DependentResource> { }

    /// <summary>
    /// Registers peer collection fixtures which cannot provide each other's constructor dependencies.
    /// </summary>
    public sealed class SameLevelCollectionDefinition : ICollectionFixture<Resource>,
        ICollectionFixture<DependentResource> { }

    /// <summary>Provides an inherited class fixture without supplying a runnable test body.</summary>
    public abstract class FixtureBase : IClassFixture<Resource> { }

    /// <summary>Consumes the inherited exact fixture registration.</summary>
    public sealed class InheritedFixtureSubject : FixtureBase
    {
        /// <summary>Requires the same fixture type registered by its abstract base.</summary>
        public InheritedFixtureSubject(
            Resource resource
        ) { }
    }

    /// <summary>Inherits a resource registration but changes the concrete constructor's exact dependency.</summary>
    public sealed class WrongLeafFixtureSubject : FixtureBase
    {
        /// <summary>Requires a derived resource which the inherited base registration cannot provide.</summary>
        public WrongLeafFixtureSubject(
            DerivedResource resource
        ) { }
    }

    /// <summary>Requires a fixture that must come from external registration metadata.</summary>
    public sealed class ResourceSubject
    {
        /// <summary>Requires an exact resource fixture.</summary>
        public ResourceSubject(
            Resource resource
        ) { }
    }

    /// <summary>Requires a closed generic fixture.</summary>
    public sealed class GenericResourceSubject
    {
        /// <summary>Requires the closed specialization supplied through open registration.</summary>
        public GenericResourceSubject(
            GenericResource<int> resource
        ) { }
    }

    /// <summary>Requires both supported test-class built-ins.</summary>
    public sealed class BuiltinSubject
    {
        /// <summary>Consumes exact built-in interface types.</summary>
        public BuiltinSubject(
            ITestOutputHelper output,
            ITestContextAccessor accessor
        ) { }
    }

    /// <summary>Has an ordinary test-constructor default.</summary>
    public sealed class DefaultArgumentSubject
    {
        /// <summary>Allows xUnit's test-class default fallback.</summary>
        public DefaultArgumentSubject(
            Resource? resource = null
        ) { }
    }

    /// <summary>Has an optional argument without a declared default.</summary>
    public sealed class OptionalArgumentSubject
    {
        /// <summary>Allows xUnit's test-class optional fallback.</summary>
        public OptionalArgumentSubject(
            [Optional] Resource? resource
        ) { }
    }

    /// <summary>Has a params array requiring no fixture.</summary>
    public sealed class ParamsArgumentSubject
    {
        /// <summary>Allows xUnit to provide an empty test-constructor array.</summary>
        public ParamsArgumentSubject(
            params object?[] resources
        ) { }
    }

    /// <summary>Is nullable but has no default or registration.</summary>
    public sealed class NullableArgumentSubject
    {
        /// <summary>Requires an argument despite its nullable annotation.</summary>
        public NullableArgumentSubject(
            Resource? resource
        ) { }
    }

    /// <summary>Requires a fixture-only builtin in a test constructor.</summary>
    public sealed class MessageSinkSubject
    {
        /// <summary>Requires the unsupported test-class message sink.</summary>
        public MessageSinkSubject(
            IMessageSink messageSink
        ) { }
    }

    /// <summary>Requires the context rather than its supported accessor.</summary>
    public sealed class TestContextSubject
    {
        /// <summary>Requires the unsupported context interface.</summary>
        public TestContextSubject(
            ITestContext context
        ) { }
    }

    /// <summary>Is assignable to the builtin output interface but has different exact identity.</summary>
    public interface IExtendedOutput : ITestOutputHelper { }

    /// <summary>Requires a subtype of a builtin interface.</summary>
    public sealed class DerivedOutputSubject
    {
        /// <summary>Requires the unsupported subtype.</summary>
        public DerivedOutputSubject(
            IExtendedOutput output
        ) { }
    }

    /// <summary>Registers a derived fixture while requiring its base type.</summary>
    public sealed class AssignableFixtureSubject : IClassFixture<DerivedResource>
    {
        /// <summary>Requires an unregistered base fixture type.</summary>
        public AssignableFixtureSubject(
            Resource resource
        ) { }
    }

    /// <summary>Has no public instance constructor.</summary>
    public sealed class NoPublicConstructorSubject
    {
        /// <summary>Prevents the default runner from constructing the subject.</summary>
        private NoPublicConstructorSubject() { }
    }

    /// <summary>Has an ambiguous test-class constructor shape.</summary>
    public sealed class MultipleConstructorSubject
    {
        /// <summary>Defines the first public constructor.</summary>
        public MultipleConstructorSubject() { }

        /// <summary>Defines an additional public constructor.</summary>
        public MultipleConstructorSubject(
            Resource? resource
        ) { }
    }

    /// <summary>Incorrectly registers a collection fixture on a test class.</summary>
    public sealed class MisplacedCollectionFixtureSubject : ICollectionFixture<Resource> { }

    /// <summary>Provides an inherited named collection attribute.</summary>
    [Collection("Constructor contract named definition")]
    public abstract class NamedCollectionBase { }

    /// <summary>Consumes the named collection inherited from its base.</summary>
    public sealed class InheritedCollectionSubject : NamedCollectionBase
    {
        /// <summary>Requires the inherited collection's resource.</summary>
        public InheritedCollectionSubject(
            Resource resource
        ) { }
    }

    /// <summary>Decorates an enclosing type without decorating its nested subject.</summary>
    [Collection("Constructor contract named definition")]
    public static class CollectionOuter
    {
        /// <summary>Does not inherit the enclosing type's collection attribute.</summary>
        public sealed class NestedSubject
        {
            /// <summary>Requires a fixture unavailable to this nested class.</summary>
            public NestedSubject(
                Resource resource
            ) { }
        }
    }

    /// <summary>References a named collection with no definition.</summary>
    [Collection("Constructor contract missing definition")]
    public sealed class UnknownCollectionSubject
    {
        /// <summary>Requires a fixture absent from the unresolved collection.</summary>
        public UnknownCollectionSubject(
            Resource resource
        ) { }
    }

    /// <summary>Provides a type-based collection definition through inheritance.</summary>
    [Collection(typeof(CollectionFixtureDefinition))]
    public abstract class ExternalCollectionBase { }

    /// <summary>Consumes the type-based collection fixture inherited from its base.</summary>
    public sealed class ExternalCollectionSubject : ExternalCollectionBase
    {
        /// <summary>Requires the referenced definition's exact fixture.</summary>
        public ExternalCollectionSubject(
            Resource resource
        ) { }
    }

    /// <summary>Uses the exact reflection fixture built-ins.</summary>
    public sealed class BuiltinResource
    {
        /// <summary>Requires a diagnostic sink and test context accessor.</summary>
        public BuiltinResource(
            IMessageSink messageSink,
            ITestContextAccessor accessor
        ) { }
    }

    /// <summary>Registers a fixture with supported builtin dependencies.</summary>
    public sealed class FixtureBuiltinSubject : IClassFixture<BuiltinResource> { }

    /// <summary>Requires a parent-level resource fixture.</summary>
    public sealed class DependentResource
    {
        /// <summary>Consumes a fixture from a parent mapping manager.</summary>
        public DependentResource(
            Resource resource
        ) { }
    }

    /// <summary>Registers a class fixture that needs a parent resource.</summary>
    public sealed class ParentDependentSubject : IClassFixture<DependentResource> { }

    /// <summary>Requires a dependent fixture supplied by collection or assembly metadata.</summary>
    public sealed class DependencySubject
    {
        /// <summary>Requires the exact dependent fixture.</summary>
        public DependencySubject(
            DependentResource resource
        ) { }
    }

    /// <summary>Incorrectly treats a peer class fixture as a constructor dependency.</summary>
    public sealed class SameLevelDependencySubject : IClassFixture<Resource>, IClassFixture<DependentResource> { }

    /// <summary>Incorrectly requires a test output helper in a fixture constructor.</summary>
    public sealed class OutputResource
    {
        /// <summary>Requires an unsupported fixture builtin.</summary>
        public OutputResource(
            ITestOutputHelper output
        ) { }
    }

    /// <summary>Registers a fixture with an unsupported output dependency.</summary>
    public sealed class OutputFixtureSubject : IClassFixture<OutputResource> { }

    /// <summary>Has a default fallback which the reflection fixture activator does not apply.</summary>
    public sealed class DefaultResource
    {
        /// <summary>Declares a default that cannot replace a missing fixture dependency.</summary>
        public DefaultResource(
            Resource? resource = null
        ) { }
    }

    /// <summary>Registers a fixture with an unsupported default dependency.</summary>
    public sealed class DefaultFixtureSubject : IClassFixture<DefaultResource> { }

    /// <summary>Has an optional argument which fixture construction does not fill.</summary>
    public sealed class OptionalResource
    {
        /// <summary>Declares an optional marker that cannot replace a missing fixture dependency.</summary>
        public OptionalResource(
            [Optional] Resource? resource
        ) { }
    }

    /// <summary>Registers a fixture with an unsupported optional dependency.</summary>
    public sealed class OptionalFixtureSubject : IClassFixture<OptionalResource> { }

    /// <summary>Has a params array which fixture construction does not fill.</summary>
    public sealed class ParamsResource
    {
        /// <summary>Declares a params array that cannot replace a missing fixture dependency.</summary>
        public ParamsResource(
            params object?[] resources
        ) { }
    }

    /// <summary>Registers a fixture with an unsupported params dependency.</summary>
    public sealed class ParamsFixtureSubject : IClassFixture<ParamsResource> { }

    /// <summary>Defines an ambiguous fixture constructor shape.</summary>
    public sealed class MultipleConstructorResource
    {
        /// <summary>Defines the first public constructor.</summary>
        public MultipleConstructorResource() { }

        /// <summary>Defines an additional public constructor.</summary>
        public MultipleConstructorResource(
            Resource resource
        ) { }
    }

    /// <summary>Has no required constructor arguments.</summary>
    public sealed class NoArgumentSubject { }

    /// <summary>Represents a static suite which never requires instance construction.</summary>
    public static class StaticSubject { }

    /// <summary>Would fail if a metadata audit constructed fixture resources.</summary>
    public sealed class ThrowingResource
    {
        /// <summary>Provides a sentinel that makes accidental initialization observable.</summary>
        public ThrowingResource()
        {
            throw new InvalidOperationException("Metadata audit initialized a fixture.");
        }
    }

    /// <summary>Registers the throwing sentinel without requesting initialization.</summary>
    public sealed class ThrowingResourceSubject : IClassFixture<ThrowingResource> { }
}
