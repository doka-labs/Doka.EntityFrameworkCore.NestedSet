namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class ProviderTestContract
{
    /// <summary>Identifies suite declarations without counting fixture helpers or inherited tests twice.</summary>
    internal static bool HasDeclaredTestCases(
        Type type
    ) => DeclaredMethods(type)
        .Any(IsTestMethod);

    /// <summary>Requires a wrapper only when at least one declared scenario applies to its engine.</summary>
    internal static bool RequiresSharedWrapper(
        Type suite,
        string engine
    )
    {
        if (!ProviderEngineOwnership.IsKnown(engine))
        {
            throw new InvalidOperationException($"Unknown integration test provider '{engine}'.");
        }

        // WHY: A fully excluded family must not be inherited merely to satisfy the coverage guard;
        // IDE metadata discovery would still expose its unsupported methods before xUnit filters them.
        return DeclaredMethods(suite)
            .Where(IsTestMethod)
            .Any(method => !MethodExclusions(method)
                .Contains(engine, StringComparer.Ordinal));
    }

    /// <summary>Includes local abstract-base declarations as seen by each concrete fixture-owned leaf.</summary>
    internal static bool HasLocalTestCases(
        Type type
    ) => LocalMethods(type)
        .Any(IsTestMethod);

    /// <summary>Rejects shared provider opt-in and validates composed fact and inline-variant applicability.</summary>
    internal static IEnumerable<string> FindSharedAnnotationErrors(
        MethodInfo method
    )
    {
        foreach (var error in FindNeutralAnnotationErrors(method))
        {
            yield return error;
        }

        var methodExclusions = MethodExclusions(method);
        var facts = method
            .CustomAttributes
            .Where(attribute => typeof(IFactAttribute).IsAssignableFrom(attribute.AttributeType))
            .ToArray();

        if (facts.Any(attribute => !typeof(TheoryAttribute).IsAssignableFrom(attribute.AttributeType)))
        {
            foreach (var error in CoverageErrors(methodExclusions, Identity(method)))
            {
                yield return error;
            }
        }

        foreach (var inline in method.CustomAttributes.Where(attribute =>
            attribute.AttributeType == typeof(InlineDataAttribute)
            || attribute.AttributeType == typeof(EngineInlineDataAttribute)))
        {
            foreach (var error in CoverageErrors(
                methodExclusions.Concat(AttributeExclusions(inline)),
                $"{Identity(method)}: inline row"))
            {
                yield return error;
            }
        }
    }

    /// <summary>Validates every raw member variant with method, member, and row exclusions composed.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    internal static async Task<IReadOnlyCollection<string>> FindSharedDataErrorsAsync(
        MethodInfo method,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var methodExclusions = MethodExclusions(method);
        var hasRows = method.CustomAttributes.Any(attribute =>
            attribute.AttributeType == typeof(InlineDataAttribute)
            || attribute.AttributeType == typeof(EngineInlineDataAttribute));

        await using var tracker = new DisposalTracker();

        foreach (var member in method.GetCustomAttributes<MemberDataAttributeBase>())
        {
            var rows = await ReadRawMemberRowsAsync(member, method, tracker, cancellationToken: cancellationToken);
            AddEmptySourceError(errors, method, member, rows.Count);
            hasRows |= rows.Count > 0;
            var exclusions = methodExclusions
                .Concat(MemberExclusions(member))
                .ToArray();

            var index = 0;

            foreach (var row in rows)
            {
                var identity = $"{Identity(method)}: member row {index}";
                var rowExclusions = RowExclusions(row);

                if (row is EngineTheoryDataRow conditional)
                {
                    errors.AddRange(ExclusionErrors(conditional.ExcludedEngines, conditional.Reason, identity));
                }

                errors.AddRange(CoverageErrors(exclusions.Concat(rowExclusions), identity));
                index++;
            }
        }

        if (method.CustomAttributes.Any(attribute => typeof(TheoryAttribute).IsAssignableFrom(attribute.AttributeType))
            && !hasRows)
        {
            errors.Add($"{Identity(method)}: shared theory has no raw data rows.");
        }

        return errors;
    }

    /// <summary>Checks neutral annotations without imposing shared-library placement on local behavior.</summary>
    private static IEnumerable<string> FindNeutralAnnotationErrors(
        MethodInfo method
    )
    {
        // WHY: A scenario's provider-looking payload is harmless; only an engine selector can override the fixture.
        if (IsTestMethod(method)
            && method
                .GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(string) && parameter.Name == "engine"))
        {
            yield return $"{Identity(method)}: tests must read Engine from the fixture, not an engine argument.";
        }

        foreach (var attribute in method.CustomAttributes)
        {
            var type = attribute.AttributeType;

            if (!typeof(IFactAttribute).IsAssignableFrom(type)
                && !typeof(IDataAttribute).IsAssignableFrom(type))
            {
                continue;
            }

            if (type == typeof(FactAttribute)
                || type == typeof(TheoryAttribute)
                || type == typeof(InlineDataAttribute)
                || type == typeof(MemberDataAttribute))
            {
                continue;
            }

            if (type != typeof(EngineFactAttribute)
                && type != typeof(EngineTheoryAttribute)
                && type != typeof(EngineInlineDataAttribute)
                && type != typeof(EngineMemberDataAttribute))
            {
                yield return $"{Identity(method)}: {type.Name} bypasses fixture-owned coverage.";

                continue;
            }

            var reason = attribute.NamedArguments.FirstOrDefault(argument =>
                    argument.MemberName == nameof(EngineFactAttribute.Reason))
                .TypedValue.Value as string;

            foreach (var error in ExclusionErrors(AttributeExclusions(attribute), reason, Identity(method)))
            {
                yield return error;
            }
        }
    }
}
