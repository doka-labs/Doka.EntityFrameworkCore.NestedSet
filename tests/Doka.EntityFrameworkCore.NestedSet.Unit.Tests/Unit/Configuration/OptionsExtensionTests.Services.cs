using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class OptionsExtensionTests
{
    /// <summary>A manually assembled graph may repeat exactly the selected immutable adapter.</summary>
    [Fact]
    public void Resolve_ExternalServiceGraph_AcceptsTheSameAdapter()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<EnabledContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets())
            .Options;

        using var provider = NestedSetTestAdapters.BuildServices(options);
        var isolated = new DbContextOptionsBuilder<EnabledContext>(options)
            .UseInternalServiceProvider(provider)
            .Options;

        using var context = new EnabledContext(isolated);

        // Act
        var actual = NestedSetProviderCapabilities.Resolve(context);
        var persistence = context.GetService<IDatabase>();

        // Assert
        Assert.Same(NestedSetTestAdapters.ForEngine("Sqlite"), actual);
        Assert.IsType<NestedSetRelationalDatabase>(persistence);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Applying an adapter cannot preserve a conflicting or opaque preexisting service descriptor.</summary>
    /// <param name="factory">Whether ownership is hidden behind a factory rather than another actual adapter.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyServices_AdapterServices_RejectConflictingOrOpaqueRegistration(
        bool factory
    )
    {
        // Arrange
        var services = new ServiceCollection();
        var attempts = 0;

        if (factory)
        {
            services.AddSingleton<NestedSetProviderCapabilities>(_ =>
            {
                attempts++;

                return NestedSetTestAdapters.ForEngine("Sqlite");
            });
        }
        else
        {
            services.AddSingleton(NestedSetTestAdapters.ForEngine("MySql"));
        }

        IDbContextOptionsExtension extension = new NestedSetOptionsExtension(NestedSetTestAdapters.ForEngine("Sqlite"));

        // Act
        var error = Record.Exception(() => extension.ApplyServices(services));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("conflicting NestedSet provider", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, attempts);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(NestedSetProviderCapabilities));
    }

    /// <summary>Model use rejects a missing or wrong external adapter even when conventions are bypassed.</summary>
    /// <param name="missing">Whether the external graph lacks the adapter rather than holding a different one.</param>
    /// <param name="suppliedModel">Whether an already initialized runtime model bypasses model conventions.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FinalizeModel_ExternalAdapterMismatch_IsRejectedBeforeModelOrIo(
        bool missing,
        bool suppliedModel
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ProbeOptions(boundary);
        using var modelServices = OwnedModelTestServices<InitializationProbeContext>.Create(options);
        using var modelContext = new InitializationProbeContext(modelServices.Options);
        var model = suppliedModel
            ? modelContext.Model
            : null;

        using var provider = NestedSetTestAdapters.BuildServices(
            options,
            services =>
            {
                services.RemoveAll<NestedSetProviderCapabilities>();

                if (!missing)
                {
                    services.AddSingleton(NestedSetTestAdapters.ForEngine("MySql"));
                }
            });

        var builder =
            new DbContextOptionsBuilder<InitializationProbeContext>(options).UseInternalServiceProvider(provider);

        if (model is not null)
        {
            builder.UseModel(model);
        }

        using var modelBuilds = InitializationProbeContext.ObserveModelCreation();

        // Act
        var error = Record.Exception(() =>
        {
            using var context = new InitializationProbeContext(builder.Options);
            _ = context.Model;
        });

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("external EF service provider", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, modelBuilds.Count);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>
    /// Native-only translator plugins are registered by MySql and absent from all other adapter graphs.
    /// </summary>
    /// <param name="engine">The selected actual adapter.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    public void UseNestedSets_MySqlAdapter_RegistersOnlyItsNativeHierarchyTranslator(
        string engine
    )
    {
        // Arrange
        var services = new ServiceCollection();
        IDbContextOptionsExtension extension = new NestedSetOptionsExtension(NestedSetTestAdapters.ForEngine(engine));

        // Act
        extension.ApplyServices(services);
        var translators = services
            .Where(descriptor => descriptor.ServiceType == typeof(IMethodCallTranslatorPlugin))
            .ToArray();

        // Assert
        if (engine == "MySql")
        {
            var translator = Assert.Single(translators);

            Assert.Equal(
                typeof(MySqlNestedSetOptionsBuilderExtensions).Assembly,
                translator.ImplementationType!.Assembly);
        }
        else
        {
            Assert.Empty(translators);
        }
    }
}
