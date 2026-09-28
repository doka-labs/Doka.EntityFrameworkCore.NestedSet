namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks the complete model-compatibility collection registration set without activating resources.</summary>
internal static class ProviderCollectionFixtureContract
{
    /// <summary>Requires exactly one model database fixture for each engine owned by the executable.</summary>
    /// <param name="definition">The collection definition whose inherited fixture registrations are inspected.</param>
    /// <param name="assemblyName">The executable test assembly whose engines own this collection.</param>
    /// <returns>Missing and unexpected fixture registrations, or an empty list when the complete set matches.</returns>
    internal static IReadOnlyList<string> FindErrors(
        Type definition,
        string assemblyName
    )
    {
        ArgumentNullException.ThrowIfNull(definition);
        var expected = ProviderEngineOwnership
            .OwnedBy(assemblyName)
            .Select(engine => typeof(ProviderFixture<,>).MakeGenericType(
                typeof(ModelCompatibilityDatabase),
                engine.MarkerType))
            .ToHashSet();

        // WHY: xUnit eagerly initializes all registered fixtures when a non-statically-skipped case exists.
        // Filtering registrations by resource would hide unexpected owners which are still initialized.
        var registered = definition
            .GetInterfaces()
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollectionFixture<>))
            .Select(type => type.GetGenericArguments()[0])
            .ToHashSet();

        var errors = new List<string>();

        foreach (var missing in expected
                     .Except(registered)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            errors.Add($"{definition.FullName}: missing collection fixture '{missing}'.");
        }

        foreach (var unexpected in registered
                     .Except(expected)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            errors.Add($"{definition.FullName}: unexpected collection fixture '{unexpected}'.");
        }

        return errors;
    }
}
