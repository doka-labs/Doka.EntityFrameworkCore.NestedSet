using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Registers nested-set services in Entity Framework Core's internal service provider.</summary>
internal sealed class NestedSetOptionsExtension : IDbContextOptionsExtension
{
    /// <summary>The single immutable options value shared by every enabled options builder.</summary>
    internal static readonly NestedSetOptionsExtension Instance = new();

    /// <summary>Provides the service-provider identity for this stateless extension.</summary>
    private readonly DbContextOptionsExtensionInfo _info;

    /// <summary>Creates the immutable nested-set options value.</summary>
    private NestedSetOptionsExtension()
    {
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
        // WHY: Convention-set plugins are multi-registration services. TryAddEnumerable composes with providers
        // and other extensions while guarding against duplicate registration in an externally managed provider.
        services.TryAddEnumerable(
            ServiceDescriptor
                .Scoped<IConventionSetPlugin,
                    NestedSetConventionSetPlugin>());

        // WHY: Relational method translators are composable services. The internal native Scope marker is emitted
        // only for MySQL string-provider identities and must retain the provider's existing query translations.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IMethodCallTranslatorPlugin, NestedSetMySqlMethodCallTranslatorPlugin>());

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
    ) { }

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
        public override string LogFragment => "using NestedSets ";

        /// <inheritdoc />
        public override int GetServiceProviderHashCode() => 0;

        /// <inheritdoc />
        public override void PopulateDebugInfo(
            IDictionary<string, string> debugInfo
        ) => debugInfo["Doka:NestedSet"] = "1";

        /// <inheritdoc />
        public override bool ShouldUseSameServiceProvider(
            DbContextOptionsExtensionInfo other
        ) => other is ExtensionInfo;
    }
}
