namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class ProviderTestContract
{
    /// <summary>Audits effective provider-local declarations against the concrete leaf's executable owner.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    internal static async Task<IReadOnlyCollection<string>> FindDeclaredOwnershipErrorsAsync(
        Type suite,
        string assemblyName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

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
                var rows = await ReadRawMemberRowsAsync(member, method, tracker, cancellationToken: cancellationToken);
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
}
