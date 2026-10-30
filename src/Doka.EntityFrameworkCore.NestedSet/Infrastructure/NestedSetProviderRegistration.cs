namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Registers one adapter through the native provider's public options callback.</summary>
internal static class NestedSetProviderRegistration
{
    /// <summary>The single context-scoped factory descriptor, containing no adapter or context references.</summary>
    internal static readonly ServiceDescriptor BindingService = ServiceDescriptor.Scoped(Bind);

    /// <summary>Installs immutable common options and rejects a conflicting adapter selection.</summary>
    /// <param name="optionsBuilder">The shared options builder exposed by the native callback.</param>
    /// <param name="provider">The stateless adapter belonging to the referenced native provider.</param>
    internal static void Register(
        DbContextOptionsBuilder optionsBuilder,
        NestedSetProviderCapabilities provider
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(provider);
        var existing = optionsBuilder.Options.FindExtension<NestedSetOptionsExtension>();

        if (existing is not null)
        {
            if (!ReferenceEquals(existing.Provider.GetType(), provider.GetType()))
            {
                throw new InvalidOperationException("Only one NestedSet provider adapter can be selected per context.");
            }

            return;
        }

        // WHY: Validate the common insertion seam before contexts or caller entities can be mutated.
        // Native-provider validation waits for completed options because this callback runs during provider setup.
        NestedSetInsertionContract.Validate();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            new NestedSetOptionsExtension(provider));
    }

    /// <summary>Validates one completed context scope and returns its existing immutable adapter.</summary>
    /// <param name="services">The initialized EF context scope containing its actual options.</param>
    /// <returns>The selected adapter itself, with no separately allocated binding object.</returns>
    private static INestedSetProviderBinding Bind(
        IServiceProvider services
    )
    {
        var options = services.GetRequiredService<IDbContextOptions>();
        var selected = options.FindExtension<NestedSetOptionsExtension>()
            ?? throw new InvalidOperationException(
                "Install the matching NestedSet provider package and call UseNestedSets() "
                + "inside the native database-provider callback.");

        var provider = services.GetRequiredService<NestedSetProviderCapabilities>();

        if (!ReferenceEquals(provider.GetType(), selected.Provider.GetType()))
        {
            throw new InvalidOperationException(
                "The context's NestedSet options and EF adapter service must have the same exact owner.");
        }

        provider.ValidateOptions(options);
        var native = services.GetRequiredService<IDatabaseProvider>();

        // WHY: Native provider selection is fixed when EF initializes this scope. Check its name once here,
        // without DatabaseFacade recursion, and retain all owner checks before model or compiled-model use.
        if (!native.IsConfigured(options)
            || !string.Equals(native.Name, provider.ProviderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The selected NestedSet adapter does not match the context's database provider.");
        }

        return provider;
    }
}
