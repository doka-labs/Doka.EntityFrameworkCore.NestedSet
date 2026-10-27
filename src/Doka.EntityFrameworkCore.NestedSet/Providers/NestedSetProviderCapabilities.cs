namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Owns the selected adapter's immutable identity and consumed relational behavior.</summary>
internal abstract partial class NestedSetProviderCapabilities : INestedSetProviderBinding
{
    /// <inheritdoc />
    NestedSetProviderCapabilities INestedSetProviderBinding.Provider => this;

    /// <summary>Gets the exact EF provider name accepted by this adapter.</summary>
    internal abstract string ProviderName { get; }

    /// <summary>Gets the actual native provider assembly referenced by this adapter.</summary>
    internal abstract Assembly ProviderAssembly { get; }

    /// <summary>Gets the bounded provider family used in library telemetry.</summary>
    internal abstract string TelemetryName { get; }

    /// <summary>Adds adapter-owned EF plugins to the same internal service graph as the common coordinator.</summary>
    /// <param name="services">The native provider's service collection.</param>
    internal virtual void AddServices(
        IServiceCollection services
    ) { }

    /// <summary>Resolves the actual context-owned adapter without a global provider registry or discovery.</summary>
    /// <param name="context">The context whose selected adapter and database provider must agree.</param>
    /// <returns>The immutable adapter from the context's EF service graph.</returns>
    /// <exception cref="InvalidOperationException">The matching adapter has not been registered.</exception>
    internal static NestedSetProviderCapabilities Resolve(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        // WHY: EF caches this validated reference in the context scope. Re-reading ProviderName allocates
        // provider-enumeration and assembly-name objects; repeated options lookup can also rebuild type metadata
        // after GC. The binding factory returns the existing adapter, without a context wrapper or global cache.
        var binding = context
                .GetInfrastructure()
                .GetService<INestedSetProviderBinding>()
            ?? throw new InvalidOperationException(
                "Install the matching NestedSet provider package and call UseNestedSets() "
                + "inside the native database-provider callback.");

        return binding.Provider;
    }

    /// <summary>Rejects absent or foreign native options before model use, including supplied compiled models.</summary>
    /// <param name="options">The fully configured options, after all provider callbacks have completed.</param>
    internal void ValidateOptions(
        IDbContextOptions options
    )
    {
        var found = false;

        foreach (var extension in options.Extensions)
        {
            if (!extension.Info.IsDatabaseProvider)
            {
                continue;
            }

            // WHY: Equal assembly names do not identify the same provider in distinct load contexts.
            // The adapter's typed dependency is the authority; no process-wide assembly lookup is involved.
            if (!ReferenceEquals(extension.GetType().Assembly, ProviderAssembly))
            {
                throw new InvalidOperationException(
                    "The selected NestedSet adapter must reference the context's actual native provider assembly.");
            }

            found = true;
        }

        if (!found)
        {
            throw new InvalidOperationException(
                "Configure the matching native database provider before using NestedSet services.");
        }
    }
}
