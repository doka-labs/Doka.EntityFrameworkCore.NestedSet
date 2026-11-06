using System.Runtime.InteropServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderFixtureContractTests
{
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
