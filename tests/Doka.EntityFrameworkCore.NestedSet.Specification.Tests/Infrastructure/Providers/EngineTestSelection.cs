namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Reads immutable engine ownership from the concrete suite's injected provider fixture.</summary>
internal static class EngineTestSelection
{
    private static readonly ConditionalWeakTable<Type, EngineIdentity> s_engines = new();

    /// <summary>Resolves the engine without constructing a database resource or reading ambient test state.</summary>
    internal static string ResolveEngine(
        Type concreteSuite
    )
    {
        var constructors = concreteSuite.GetConstructors();

        if (constructors.Length != 1)
        {
            throw new InvalidOperationException(
                $"Suite '{concreteSuite.FullName}' must expose exactly one public constructor.");
        }

        var fixtures = constructors[0]
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ProviderFixture<,>))
            .ToArray();

        if (fixtures.Length == 0)
        {
            throw new InvalidOperationException(
                $"Suite '{concreteSuite.FullName}' must inject a concrete provider fixture.");
        }

        // WHY: Collections can register both MySQL engines; only the actual constructor selects this suite's engine.
        var engines = fixtures
            .Select(type => s_engines.GetValue(type.GetGenericArguments()[1], ReadIdentity).Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (engines.Length != 1)
        {
            throw new InvalidOperationException(
                $"Suite '{concreteSuite.FullName}' injects contradictory provider fixtures.");
        }

        var engine = engines[0];

        if (!ProviderEngineOwnership.Includes(
                engine,
                concreteSuite.Assembly.GetName()
                    .Name!))
        {
            throw new InvalidOperationException(
                $"Suite '{concreteSuite.FullName}' injects engine '{engine}' from a foreign provider project.");
        }

        return engine;
    }

    /// <summary>Validates explicit exclusions before deciding whether an engine receives a method or row.</summary>
    internal static bool AppliesToEngine(
        string engine,
        IReadOnlyCollection<string> excludedEngines,
        string? reason
    )
    {
        if (!ProviderEngineOwnership.IsKnown(engine))
        {
            throw new InvalidOperationException($"Unknown integration test provider '{engine}'.");
        }

        ArgumentNullException.ThrowIfNull(excludedEngines);

        if (excludedEngines.Count > 0
            && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Engine exclusions require a nonempty reason.");
        }

        foreach (var excluded in excludedEngines)
        {
            if (!ProviderEngineOwnership.IsKnown(excluded))
            {
                throw new InvalidOperationException($"Unknown excluded integration test provider '{excluded}'.");
            }
        }

        return !excludedEngines.Contains(engine, StringComparer.Ordinal);
    }

    /// <summary>Closes the marker's static contract once without activating the resource fixture.</summary>
    private static EngineIdentity ReadIdentity(
        Type marker
    )
    {
        var reader =
            typeof(EngineTestSelection).GetMethod(nameof(ReadEngineName), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(marker);

        var name = (string)reader.Invoke(null, BindingFlags.DoNotWrapExceptions, null, null, null)!;

        return new EngineIdentity(name);
    }

    /// <summary>Uses the compiler-checked engine marker rather than an arbitrary reflected property getter.</summary>
    private static string ReadEngineName<TEngine>()
        where TEngine : struct, IProviderEngine => TEngine.Name;

    /// <summary>Keeps a resolved marker name independent of fixture initialization and disposal.</summary>
    private sealed class EngineIdentity
    {
        /// <summary>Captures the immutable marker value.</summary>
        internal EngineIdentity(
            string name
        )
        {
            Name = name;
        }

        /// <summary>Gets the engine declared by the fixture's closed marker.</summary>
        internal string Name { get; }
    }
}
