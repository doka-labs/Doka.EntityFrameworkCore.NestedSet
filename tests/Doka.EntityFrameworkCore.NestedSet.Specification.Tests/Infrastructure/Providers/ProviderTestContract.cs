namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Audits raw test declarations independently of discovery's engine exclusions.</summary>
internal static class ProviderTestContract
{
    /// <summary>Gets every engine owned by an executable test assembly.</summary>
    internal static string[] OwnedEngines(
        string assemblyName
    ) => ProviderEngineOwnership
        .OwnedBy(assemblyName)
        .Select(engine => engine.Name)
        .ToArray();

    /// <summary>Identifies suite declarations without counting fixture helpers or inherited tests twice.</summary>
    internal static bool HasDeclaredTestCases(
        Type type
    ) => DeclaredMethods(type)
        .Any(IsTestMethod);

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
    internal static async Task<IReadOnlyCollection<string>> FindSharedDataErrorsAsync(
        MethodInfo method
    )
    {
        var errors = new List<string>();
        var methodExclusions = MethodExclusions(method);
        var hasRows = method.CustomAttributes.Any(attribute =>
            attribute.AttributeType == typeof(InlineDataAttribute)
            || attribute.AttributeType == typeof(EngineInlineDataAttribute));

        await using var tracker = new DisposalTracker();

        foreach (var member in method.GetCustomAttributes<MemberDataAttributeBase>())
        {
            var rows = await ReadRawMemberRowsAsync(member, method, tracker);
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

    /// <summary>Audits effective provider-local declarations against the concrete leaf's executable owner.</summary>
    internal static async Task<IReadOnlyCollection<string>> FindDeclaredOwnershipErrorsAsync(
        Type suite,
        string assemblyName
    )
    {
        // WHY: Inherited local family bodies run on both leaves; shared-library declarations have a separate audit.
        var methods = LocalMethods(suite)
            .Where(IsTestMethod)
            .ToArray();

        var errors = new List<string>();

        if (methods.Length > 0)
        {
            ValidateFixtureOwner(errors, suite, assemblyName);
        }

        await using var tracker = new DisposalTracker();

        foreach (var method in methods)
        {
            errors.AddRange(FindNeutralAnnotationErrors(method));
            var hasRows = method.CustomAttributes.Any(attribute =>
                attribute.AttributeType == typeof(InlineDataAttribute)
                || attribute.AttributeType == typeof(EngineInlineDataAttribute));

            foreach (var member in method.GetCustomAttributes<MemberDataAttributeBase>())
            {
                // WHY: Raw source validation cannot be bypassed by discovery exclusions or a genuinely empty factory.
                var rows = await ReadRawMemberRowsAsync(member, method, tracker);
                AddEmptySourceError(errors, method, member, rows.Count);
                var index = 0;

                foreach (var row in rows)
                {
                    if (row is EngineTheoryDataRow conditional)
                    {
                        var identity = $"{Identity(method)}: member row {index}";
                        errors.AddRange(ExclusionErrors(conditional.ExcludedEngines, conditional.Reason, identity));
                    }

                    index++;
                }

                hasRows |= rows.Count > 0;
            }

            if (method.CustomAttributes.Any(attribute =>
                    typeof(TheoryAttribute).IsAssignableFrom(attribute.AttributeType))
                && !hasRows)
            {
                errors.Add($"{Identity(method)}: provider theory has no raw data rows.");
            }
        }

        return errors;
    }

    /// <summary>Validates exact fixture metadata independently of exclusions and provider-looking payloads.</summary>
    private static void ValidateFixtureOwner(
        List<string> errors,
        Type suite,
        string assemblyName
    )
    {
        if (!typeof(ProviderTest).IsAssignableFrom(suite))
        {
            errors.Add($"{suite.FullName}: ordinary provider-local tests must derive from ProviderTest.");
        }

        errors.AddRange(ProviderFixtureContract.FindErrors(suite));

        try
        {
            var engine = EngineTestSelection.ResolveEngine(suite);

            // WHY: The caller's audit owner can differ from the suite assembly already validated by ResolveEngine.
            if (!ProviderEngineOwnership.Includes(engine, assemblyName))
            {
                errors.Add($"{suite.FullName}: provider '{engine}' is foreign to '{assemblyName}'.");
            }
        }
        catch (InvalidOperationException error)
        {
            errors.Add($"{suite.FullName}: {error.Message}");
        }
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

    /// <summary>Matches public inherited local methods while retaining the concrete leaf as ReflectedType.</summary>
    private static MethodInfo[] LocalMethods(
        Type type
    ) => type
        .GetMethods(
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.FlattenHierarchy)
        .Where(method => method.DeclaringType!.Assembly == type.Assembly)
        .ToArray();

    /// <summary>Gets only declarations belonging to this exact suite.</summary>
    private static MethodInfo[] DeclaredMethods(
        Type type
    ) => type.GetMethods(
        BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly);

    /// <summary>Recognizes facts and theories without constructing potentially invalid annotations.</summary>
    private static bool IsTestMethod(
        MethodInfo method
    ) => method.CustomAttributes.Any(attribute => typeof(IFactAttribute).IsAssignableFrom(attribute.AttributeType));

    /// <summary>Reads every source row without applying current-engine discovery selection.</summary>
    private static ValueTask<IReadOnlyCollection<ITheoryDataRow>> ReadRawMemberRowsAsync(
        MemberDataAttributeBase member,
        MethodInfo method,
        DisposalTracker tracker
    )
    {
        // WHY: xUnit supplies the concrete ReflectedType for an unset MemberType; explicit sources stay intact.
        member.MemberType ??= method.ReflectedType;

        return member switch
        {
            EngineMemberDataAttribute conditional => conditional.GetSourceData(method, tracker),
            _ => member.GetData(method, tracker),
        };
    }

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

    /// <summary>Extracts method exclusions from raw metadata before constructing custom annotations.</summary>
    private static string?[] MethodExclusions(
        MethodInfo method
    ) => method
        .CustomAttributes
        .Where(attribute => typeof(IFactAttribute).IsAssignableFrom(attribute.AttributeType))
        .SelectMany(AttributeExclusions)
        .ToArray();

    /// <summary>Reads declared exclusions independently of discovery's filtered attribute objects.</summary>
    private static string?[] AttributeExclusions(
        CustomAttributeData attribute
    )
    {
        var values = attribute.NamedArguments.FirstOrDefault(argument =>
                argument.MemberName == nameof(EngineFactAttribute.ExcludedEngines))
            .TypedValue.Value as IReadOnlyCollection<CustomAttributeTypedArgument>;

        return values
                ?.Select(value => value.Value as string)
                .ToArray()
            ?? [];
    }

    /// <summary>Gets member exclusions which apply before any row-specific conditions.</summary>
    private static string[] MemberExclusions(
        MemberDataAttributeBase member
    ) => member is EngineMemberDataAttribute engine ? engine.ExcludedEngines : [];

    /// <summary>Gets the additional conditions carried by one normalized source variant.</summary>
    private static string[] RowExclusions(
        ITheoryDataRow row
    ) => row is EngineTheoryDataRow engine ? engine.ExcludedEngines : [];

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
            var projectName = projects[0].Split('.')[^2];

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

    /// <summary>Identifies the exact declaration in a guard failure.</summary>
    private static string Identity(
        MethodInfo method
    ) => $"{method.DeclaringType!.FullName}.{method.Name}";
}
