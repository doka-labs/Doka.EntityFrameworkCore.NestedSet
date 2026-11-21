namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Audits raw test declarations independently of discovery's engine exclusions.</summary>
internal static partial class ProviderTestContract
{
    /// <summary>Gets every engine owned by an executable test assembly.</summary>
    internal static string[] OwnedEngines(
        string assemblyName
    ) => ProviderEngineOwnership
        .OwnedBy(assemblyName)
        .Select(engine => engine.Name)
        .ToArray();

    /// <summary>Reports an empty raw factory even when discovery would exclude its method.</summary>
    private static void AddEmptySourceError(
        List<string> errors,
        MethodInfo method,
        MemberDataAttributeBase member,
        int count
    )
    {
        if (count == 0)
        {
            errors.Add($"{Identity(method)}: member source '{member.MemberName}' is empty.");
        }
    }

    /// <summary>Requires every effective shared variant to span at least two executable owners.</summary>
    private static IEnumerable<string> CoverageErrors(
        IEnumerable<string?> exclusions,
        string identity
    )
    {
        var excluded = exclusions.ToHashSet(StringComparer.Ordinal);
        var projects = ProviderEngineOwnership
            .Engines
            .Where(engine => !excluded.Contains(engine.Name))
            .Select(engine => engine.AssemblyName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // WHY: Two engines in the MySql executable are still one owner; provider-specific bodies belong there.
        if (projects.Length == 1)
        {
            var projectName = projects[0]
                .Split('.')[^2];

            yield return $"{identity}: effective coverage is confined to one provider executable '{projectName}'; "
                + "declare this behavior in that provider project.";
        }
        else if (projects.Length == 0)
        {
            yield return $"{identity}: effective engine coverage is empty.";
        }
    }

    /// <summary>Reports misspelled exclusions or exclusions without a concrete explanation.</summary>
    private static IEnumerable<string> ExclusionErrors(
        string?[] engines,
        string? reason,
        string identity
    )
    {
        if (engines.Length > 0
            && string.IsNullOrWhiteSpace(reason))
        {
            yield return $"{identity}: engine exclusions require a nonempty reason.";
        }

        foreach (var engine in engines)
        {
            if (!ProviderEngineOwnership.IsKnown(engine))
            {
                yield return $"{identity}: unknown excluded provider '{engine}'.";
            }
        }
    }
}
