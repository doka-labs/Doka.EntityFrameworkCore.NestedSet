using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Registers nested-set services in Entity Framework Core's internal service provider.</summary>
internal sealed class NestedSetOptionsExtension : IDbContextOptionsExtension
{
    /// <summary>Gets the immutable adapter selected by the native provider callback.</summary>
    internal NestedSetProviderCapabilities Provider { get; }

    /// <summary>Provides the service-provider identity for this stateless extension.</summary>
    private readonly DbContextOptionsExtensionInfo _info;

    /// <summary>Creates the immutable nested-set options value.</summary>
    /// <param name="provider">The selected adapter from its typed native provider module.</param>
    internal NestedSetOptionsExtension(
        NestedSetProviderCapabilities provider
    )
    {
        Provider = provider;
        _info = new ExtensionInfo(this);
    }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info;

    /// <inheritdoc />
    IDbContextOptionsExtension IDbContextOptionsExtension.ApplyDefaults(
        IDbContextOptions options
    ) => this;

    /// <inheritdoc />
    void IDbContextOptionsExtension.ApplyServices(
        IServiceCollection services
    )
    {
        var installed = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(NestedSetProviderCapabilities) && !descriptor.IsKeyedService);

        // WHY: An externally assembled EF graph must not silently keep a different adapter through TryAdd.
        // Only the fixed immutable provider instance can prove ownership before scoped context services exist.
        if (installed is not null
            && (installed.ImplementationInstance is not NestedSetProviderCapabilities selected
                || !ReferenceEquals(selected.GetType(), Provider.GetType())))
        {
            throw new InvalidOperationException(
                "The EF service collection already contains a conflicting NestedSet provider adapter.");
        }

        var binding = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(INestedSetProviderBinding) && !descriptor.IsKeyedService);

        if (binding is not null
            && !ReferenceEquals(binding, NestedSetProviderRegistration.BindingService))
        {
            throw new InvalidOperationException(
                "The EF service collection already contains a conflicting NestedSet provider binding.");
        }

        // WHY: Convention-set plugins are multi-registration services. TryAddEnumerable composes with providers
        // and other extensions while guarding against duplicate registration in an externally managed provider.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConventionSetPlugin, NestedSetConventionSetPlugin>());

        // WHY: The selected adapter owns its native plugins; unrelated provider translators never enter this graph.
        services.TryAddSingleton(Provider);
        services.TryAdd(NestedSetProviderRegistration.BindingService);
        Provider.AddServices(services);

        // WHY: The interceptor is stateless and must be present even when the application does not use the optional
        // NestedSetDbContext base class. Context-local coordination remains in a weak table owned by the save wrapper.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor, NestedSetSaveGuardInterceptor>());

        // WHY: EF passes each save's exact write set to IDatabase after every SavingChanges callback and before the
        // first command. Validating managed insertions there needs no per-command interception. Replace works in
        // either extension order: it overrides an earlier provider registration and a later TryAdd keeps it.
        var registered = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(IDatabase)
            && !descriptor.IsKeyedService);

        if (registered is not null
            && registered.ImplementationType != typeof(RelationalDatabase)
            && registered.ImplementationType != typeof(NestedSetRelationalDatabase))
        {
            // WHY: Replacing a provider-specific database service would silently discard its save behavior.
            throw new NotSupportedException(
                "UseNestedSets() requires the provider's standard relational IDatabase service.");
        }

        services.Replace(ServiceDescriptor.Scoped<IDatabase, NestedSetRelationalDatabase>());
    }

    /// <inheritdoc />
    void IDbContextOptionsExtension.Validate(
        IDbContextOptions options
    )
    {
        Provider.ValidateOptions(options);
        var external = options.FindExtension<CoreOptionsExtension>()
            ?.InternalServiceProvider;

        if (external is not null)
        {
            var selected = external.GetService<NestedSetProviderCapabilities>();

            // WHY: Supplied compiled models bypass conventions. Validate the selected service before either
            // a model is used or a native provider operation can observe an incorrectly assembled external graph.
            if (selected is null
                || !ReferenceEquals(selected.GetType(), Provider.GetType()))
            {
                throw new InvalidOperationException(
                    "The external EF service provider must contain the selected NestedSet adapter services.");
            }
        }
    }

    /// <summary>Describes the extension's stable internal service-provider identity.</summary>
    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        /// <summary>Creates metadata for one nested-set options value.</summary>
        /// <param name="extension">The options extension represented by this metadata.</param>
        internal ExtensionInfo(
            IDbContextOptionsExtension extension
        ) : base(extension) { }

        /// <inheritdoc />
        public override bool IsDatabaseProvider => false;

        /// <inheritdoc />
        public override string LogFragment =>
            "using NestedSets " + ((NestedSetOptionsExtension)Extension).Provider.TelemetryName + " ";

        /// <inheritdoc />
        public override int GetServiceProviderHashCode() =>
            RuntimeHelpers.GetHashCode(((NestedSetOptionsExtension)Extension).Provider.GetType());

        /// <inheritdoc />
        public override void PopulateDebugInfo(
            IDictionary<string, string> debugInfo
        ) => debugInfo["Doka:NestedSet"] = GetServiceProviderHashCode()
            .ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public override bool ShouldUseSameServiceProvider(
            DbContextOptionsExtensionInfo other
        ) => other is ExtensionInfo info
            && ReferenceEquals(
                ((NestedSetOptionsExtension)Extension).Provider.GetType(),
                ((NestedSetOptionsExtension)info.Extension).Provider.GetType());
    }
}
