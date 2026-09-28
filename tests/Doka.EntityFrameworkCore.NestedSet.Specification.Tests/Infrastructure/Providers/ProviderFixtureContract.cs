namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Audits constructor resolution for the repository's standard reflection-based xUnit runner.</summary>
internal static class ProviderFixtureContract
{
    /// <summary>
    /// Reads the concrete suite's public xUnit metadata without creating or initializing fixture resources.
    /// </summary>
    /// <param name="suite">The concrete test class whose constructor the runner will invoke.</param>
    /// <returns>
    /// Structural registration and constructor errors, or an empty list when all dependencies resolve.
    /// </returns>
    internal static IReadOnlyList<string> FindErrors(
        Type suite
    )
    {
        ArgumentNullException.ThrowIfNull(suite);
        var assembly = new XunitTestAssembly(
            suite.Assembly,
            configFilePath: null,
            assemblyName: suite.Assembly.GetName().FullName);

        try
        {
            // WHY: The public factory follows inherited collection attributes, including external type definitions.
            var collection = new CollectionPerClassTestCollectionFactory(assembly).Get(suite);

            return FindErrors(new XunitTestClass(suite, collection));
        }
        catch (ArgumentException error)
        {
            return [$"{suite.FullName}: {error.Message}"];
        }
    }

    /// <summary>
    /// Validates the actual constructor and fixture mapping chain represented by public xUnit metadata.
    /// </summary>
    /// <param name="testClass">
    /// The concrete reflection metadata, including its resolved collection and assembly.
    /// </param>
    /// <returns>
    /// Structural errors without executing test classes, fixture constructors, or fixture lifecycle methods.
    /// </returns>
    internal static IReadOnlyList<string> FindErrors(
        IXunitTestClass testClass
    )
    {
        ArgumentNullException.ThrowIfNull(testClass);
        var errors = new List<string>();
        var collection = testClass.TestCollection;
        var assemblyScope = new FixtureScope("Assembly", collection.TestAssembly.AssemblyFixtureTypes, null, errors);
        var collectionScope = new FixtureScope("Collection", collection.CollectionFixtureTypes, assemblyScope, errors);
        var classScope = new FixtureScope("Class", testClass.ClassFixtureTypes, collectionScope, errors);

        // WHY: xUnit initializes registered fixtures even when a test constructor does not request their values.
        assemblyScope.ValidateRegisteredFixtures();
        collectionScope.ValidateRegisteredFixtures();
        classScope.ValidateRegisteredFixtures();

        var suite = testClass.Class;

        if (suite
            .GetInterfaces()
            .Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollectionFixture<>)))
        {
            errors.Add($"{suite.FullName}: ICollectionFixture<> belongs on a collection definition, not a test class.");
        }

        var constructors = testClass.Constructors;

        if (constructors is null)
        {
            // WHY: Static test classes do not need an instance constructor, but their fixture metadata still applies.
            return errors;
        }

        if (suite.IsAbstract
            || suite.ContainsGenericParameters)
        {
            errors.Add($"{suite.FullName}: the test class must be concrete and closed.");
        }

        if (constructors.Count != 1)
        {
            errors.Add($"{suite.FullName}: an instance test class must define exactly one public constructor.");

            return errors;
        }

        foreach (var parameter in constructors
                     .Single()
                     .GetParameters())
        {
            var parameterType = parameter.ParameterType;

            if (parameterType == typeof(ITestOutputHelper)
                || parameterType == typeof(ITestContextAccessor)
                || classScope.TryResolve(parameterType))
            {
                continue;
            }

            // WHY: These fallbacks belong to test-class argument creation; fixture argument creation has no fallback.
            if (parameter.HasDefaultValue
                || parameter.IsOptional
                || (parameter.GetCustomAttribute<ParamArrayAttribute>() is not null
                    && parameterType.GetElementType() is not null))
            {
                continue;
            }

            errors.Add(
                $"{suite.FullName}: unresolved test constructor argument " + $"{parameterType.Name} {parameter.Name}.");
        }

        return errors;
    }

    /// <summary>Models one exact-type fixture mapping level and its parent-only constructor dependency chain.</summary>
    private sealed class FixtureScope
    {
        private readonly string _category;
        private readonly HashSet<Type> _registrations;
        private readonly IReadOnlyList<Type> _initializedRegistrations;
        private readonly HashSet<Type> _validated = [];
        private readonly FixtureScope? _parent;
        private readonly List<string> _errors;

        /// <summary>Captures registration metadata without acquiring any fixture instances.</summary>
        internal FixtureScope(
            string category,
            IReadOnlyCollection<Type> registrations,
            FixtureScope? parent,
            List<string> errors
        )
        {
            _category = category;
            _registrations = registrations
                .Select(NormalizeRegistration)
                .ToHashSet();

            _initializedRegistrations = registrations
                .Where(type => NormalizeRegistration(type) == type)
                .Distinct()
                .ToArray();

            _parent = parent;
            _errors = errors;
        }

        /// <summary>
        /// Checks the registrations which xUnit eagerly initializes, excluding normalized partial generics.
        /// </summary>
        internal void ValidateRegisteredFixtures()
        {
            foreach (var type in _initializedRegistrations)
            {
                // WHY: Only a changed normalization defers creation; a directly registered open definition is invalid.
                ValidateFixture(type);
            }
        }

        /// <summary>Resolves an exact fixture request and validates the selected closed specialization.</summary>
        internal bool TryResolve(
            Type requested
        )
        {
            // WHY: xUnit checks the exact key and an open generic registration; assignable types never substitute.
            if (_registrations.Contains(requested)
                || (requested.IsGenericType && _registrations.Contains(requested.GetGenericTypeDefinition())))
            {
                ValidateFixture(requested);

                return true;
            }

            return _parent?.TryResolve(requested) ?? false;
        }

        /// <summary>
        /// Applies fixture-specific built-ins and parent-only resolution to one constructor signature.
        /// </summary>
        private void ValidateFixture(
            Type fixture
        )
        {
            if (!_validated.Add(fixture))
            {
                return;
            }

            if (fixture.IsAbstract
                || fixture.ContainsGenericParameters)
            {
                _errors.Add(
                    $"{_category} fixture {fixture.FullName}: " + "the requested fixture must be concrete and closed.");

                return;
            }

            var constructors = fixture.GetConstructors();

            if (constructors.Length != 1)
            {
                _errors.Add(
                    $"{_category} fixture {fixture.FullName}: "
                    + "a fixture must define exactly one public constructor.");

                return;
            }

            foreach (var parameter in constructors[0]
                         .GetParameters())
            {
                var parameterType = parameter.ParameterType;

                if (parameterType == typeof(IMessageSink)
                    || parameterType == typeof(ITestContextAccessor)
                    || (_parent?.TryResolve(parameterType) ?? false))
                {
                    continue;
                }

                // WHY: Reflection fixture activation rejects Missing.Value even for defaulted, optional,
                // or params arguments.
                _errors.Add(
                    $"{_category} fixture {fixture.FullName}: unresolved fixture constructor argument "
                    + $"{parameterType.Name} {parameter.Name}.");
            }
        }

        /// <summary>
        /// Normalizes partially open generic registrations in the same way as xUnit's mapping manager.
        /// </summary>
        private static Type NormalizeRegistration(
            Type type
        ) => type.IsGenericType && type.GenericTypeArguments.Any(argument => argument.IsGenericParameter)
            ? type.GetGenericTypeDefinition()
            : type;
    }
}
