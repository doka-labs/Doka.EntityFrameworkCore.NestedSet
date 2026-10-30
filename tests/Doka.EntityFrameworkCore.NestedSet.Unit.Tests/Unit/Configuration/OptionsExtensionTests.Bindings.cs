using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class OptionsExtensionTests
{
    /// <summary>An unknown binding cannot bypass scope validation, even when it returns the correct adapter.</summary>
    /// <param name="registration">The conflicting descriptor's ownership or lifetime shape.</param>
    [Theory]
    [InlineData("foreign-adapter")]
    [InlineData("opaque-factory")]
    [InlineData("unvalidated-singleton")]
    public void AdapterServicesRejectUnvalidatedBindingWithoutChangingTheGraph(string registration)
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine("Sqlite");
        IServiceCollection services = new ServiceCollection();
        var attempts = 0;
        var descriptor = registration switch
        {
            "foreign-adapter" => ServiceDescriptor.Singleton<INestedSetProviderBinding>(
                NestedSetTestAdapters.ForEngine("MySql")),
            "opaque-factory" => ServiceDescriptor.Scoped<INestedSetProviderBinding>(_ =>
            {
                attempts++;

                return adapter;
            }),
            "unvalidated-singleton" => ServiceDescriptor.Singleton<INestedSetProviderBinding>(adapter),
            _ => throw new ArgumentOutOfRangeException(nameof(registration)),
        };

        services.Add(descriptor);
        IDbContextOptionsExtension extension = new NestedSetOptionsExtension(adapter);

        // Act
        var error = Record.Exception(() => extension.ApplyServices(services));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("conflicting NestedSet provider binding", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, attempts);
        Assert.Same(descriptor, Assert.Single(services));
        Assert.DoesNotContain(services, candidate => candidate.ServiceType == typeof(NestedSetProviderCapabilities));
    }

    /// <summary>Repeated service registration preserves one validated scoped factory and one immutable adapter.</summary>
    [Fact]
    public void RepeatedAdapterServicesPreserveTheScopedBindingDescriptor()
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine("Sqlite");
        var services = new ServiceCollection();
        IDbContextOptionsExtension extension = new NestedSetOptionsExtension(adapter);

        // Act
        extension.ApplyServices(services);
        extension.ApplyServices(services);

        // Assert
        var binding = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(INestedSetProviderBinding));
        var provider = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(NestedSetProviderCapabilities));

        Assert.Same(NestedSetProviderRegistration.BindingService, binding);
        Assert.Equal(ServiceLifetime.Scoped, binding.Lifetime);
        Assert.NotNull(binding.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
        Assert.Same(adapter, provider.ImplementationInstance);
    }

    /// <summary>Every real context scope validates native ownership once and returns its existing adapter without a wrapper.</summary>
    /// <param name="engine">The actual configured native provider whose ownership is checked.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    public void ScopedBindingValidatesOncePerScopeAndReusesTheImmutableAdapter(string engine)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<DbContext>(engine, Connection(engine));
        var adapter = NestedSetTestAdapters.ForEngine(engine);
        using var nativeServices = NestedSetTestAdapters.BuildServices(options);
        var native = new ObservedDatabaseProvider(nativeServices.GetRequiredService<IDatabaseProvider>());
        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: Supply completed context options to real DI scopes without constructing a context or touching I/O.
        // The observer delegates to the actual native provider; only validation frequency is measured here.
        services.AddScoped<IDbContextOptions>(_ => options);
        services.Replace(ServiceDescriptor.Singleton<IDatabaseProvider>(native));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        // Act
        var first = firstScope.ServiceProvider.GetRequiredService<INestedSetProviderBinding>();
        var repeated = firstScope.ServiceProvider.GetRequiredService<INestedSetProviderBinding>();
        var second = secondScope.ServiceProvider.GetRequiredService<INestedSetProviderBinding>();

        // Assert
        Assert.Same(adapter, first);
        Assert.Same(first, repeated);
        Assert.Same(adapter, second);
        Assert.Same(adapter, first.Provider);
        Assert.Same(adapter, second.Provider);
        Assert.Equal(2, native.NameReads);
        Assert.Equal(2, native.ConfigurationChecks);
    }

    /// <summary>Observes service validation while preserving the real provider's configured behavior.</summary>
    private sealed class ObservedDatabaseProvider : IDatabaseProvider
    {
        private readonly IDatabaseProvider _inner;
        private int _nameReads;
        private int _configurationChecks;

        /// <summary>Wraps the native singleton from an actual provider service graph.</summary>
        internal ObservedDatabaseProvider(IDatabaseProvider inner) => _inner = inner;

        /// <summary>Gets how often the binding inspected native provider identity.</summary>
        internal int NameReads => Volatile.Read(ref _nameReads);

        /// <summary>Gets how often the binding checked native options ownership.</summary>
        internal int ConfigurationChecks => Volatile.Read(ref _configurationChecks);

        /// <inheritdoc />
        public string Name
        {
            get
            {
                Interlocked.Increment(ref _nameReads);

                return _inner.Name;
            }
        }

        /// <inheritdoc />
        public string? Version => _inner.Version;

        /// <inheritdoc />
        public bool IsConfigured(IDbContextOptions options)
        {
            Interlocked.Increment(ref _configurationChecks);

            return _inner.IsConfigured(options);
        }
    }
}
