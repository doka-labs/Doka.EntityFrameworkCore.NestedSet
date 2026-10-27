namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Recognizes known relational character limits and validates their exact Unicode representation.</summary>
internal static class NestedSetIdentityLength
{
    /// <summary>Captures native identity-count facts while the selected adapter and mapping services are available.</summary>
    /// <param name="hierarchy">The exact configured hierarchy whose physical identities are finalized.</param>
    /// <param name="descriptor">The validated structural role descriptors.</param>
    /// <param name="provider">The selected adapter owning native mapping semantics.</param>
    /// <param name="typeMappings">The service resolving mappings before EF initializes runtime metadata.</param>
    internal static void Capture(
        IConventionEntityType hierarchy,
        NestedSetModelDescriptor descriptor,
        NestedSetProviderCapabilities provider,
        ITypeMappingSource typeMappings
    )
    {
        // WHY: Primitive facts survive compiled-model generation without a provider object, delegate or global
        // resolver. Determine them after native store facets settle, alongside the existing collation capture.
        hierarchy.SetAnnotation(
            NestedSetAnnotationNames.ScopeUnicodeScalarLength,
            ResolvePolicy(descriptor.Scope?.Resolve(hierarchy), provider, typeMappings));
        hierarchy.SetAnnotation(
            NestedSetAnnotationNames.TreeIdUnicodeScalarLength,
            ResolvePolicy(descriptor.TreeId.Resolve(hierarchy), provider, typeMappings));
    }

    /// <summary>Reads one captured native policy without reconstructing the model or resolving a live provider.</summary>
    /// <param name="hierarchy">The finalized hierarchy, including runtime and compiled models.</param>
    /// <param name="annotation">The exact Scope or TreeId policy annotation.</param>
    /// <returns>The immutable policy selected by the native model finalizer.</returns>
    /// <exception cref="InvalidOperationException">The model lacks valid native identity-count capture.</exception>
    internal static bool CapturedPolicy(
        IReadOnlyEntityType hierarchy,
        string annotation
    )
    {
        if (hierarchy.FindAnnotation(annotation)?.Value is bool policy)
        {
            return policy;
        }

        throw new InvalidOperationException(
            $"The nested-set identity length policy for '{hierarchy.Name}' is missing or invalid. "
            + "Regenerate the model after calling UseNestedSets() inside the matching native provider callback.");
    }

    /// <summary>Asks the selected adapter about the effective mapping, retaining conservative custom-map behavior.</summary>
    private static bool ResolvePolicy(
        IReadOnlyProperty? property,
        NestedSetProviderCapabilities provider,
        ITypeMappingSource typeMappings
    )
    {
        if (property is null)
        {
            return false;
        }

        var mapping = property.FindTypeMapping()
            ?? (property is IProperty mapped ? typeMappings.FindMapping(mapped) : null);

        if (mapping is not RelationalTypeMapping relational)
        {
            throw new InvalidOperationException(
                $"The nested-set identity mapping for '{property.Name}' is unavailable during native policy capture.");
        }

        return provider.UsesUnicodeScalarLength(relational);
    }

    /// <summary>Counts Unicode scalar values and rejects input requiring lossy surrogate replacement.</summary>
    /// <param name="value">The stored string after applying any configured value converter.</param>
    /// <param name="role">The identity role used by the argument error.</param>
    /// <returns>The scalar count without allocating, normalizing or changing the original value.</returns>
    internal static int CountUnicodeScalars(
        string value,
        string role
    )
    {
        // WHY: The framework scans the complete surrogate range without a retained predicate or initialization.
        // Allocation budgets run without coverage instrumentation, which can change the helper's generated code.
        var firstSurrogate = value
            .AsSpan()
            .IndexOfAnyInRange('\ud800', '\udfff');

        // WHY: Most identities are ASCII/BMP. The vectorized search avoids a scalar loop for that common case.
        if (firstSurrogate < 0)
        {
            return value.Length;
        }

        var count = firstSurrogate;

        for (var index = firstSurrogate; index < value.Length; index++)
        {
            var character = value[index];

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw InvalidSurrogate(role);
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                throw InvalidSurrogate(role);
            }

            count++;
        }

        return count;
    }

    /// <summary>Creates a role-specific error without exposing the identity's potentially sensitive contents.</summary>
    private static ArgumentException InvalidSurrogate(
        string role
    ) => new($"{role} contains invalid UTF-16 surrogate data.", role);
}
