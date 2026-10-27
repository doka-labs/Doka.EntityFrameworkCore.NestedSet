using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostgresOptionsBuilder = Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class OptionsExtensionTests
{
    /// <summary>Every public native callback returns its actual builder and installs exactly its own adapter.</summary>
    /// <param name="engine">The native provider callback under test.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    public void NativeCallbackSelectsItsExactAdapter(string engine)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<DbContext>(engine, Connection(engine));

        // Act
        var extension = options.FindExtension<NestedSetOptionsExtension>();
        ((IDbContextOptionsExtension)extension!).Validate(options);

        // Assert
        Assert.NotNull(extension);
        Assert.Same(NestedSetTestAdapters.ForEngine(engine), extension.Provider);
        Assert.Same(extension.Provider.ProviderAssembly,
            Assert.Single(options.Extensions, candidate => candidate.Info.IsDatabaseProvider).GetType().Assembly);
        Assert.Single(options.Extensions.OfType<NestedSetOptionsExtension>());
    }

    /// <summary>Each native callback preserves its builder identity while selecting its exact adapter.</summary>
    /// <param name="engine">The public provider callback receiver being configured.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    public void NativeCallbackReturnsTheSameBuilder(string engine)
    {
        // Arrange
        var core = new DbContextOptionsBuilder();
        object native = engine switch
        {
            "MySql" => new MySqlDbContextOptionsBuilder(core),
            "PostgreSql" => new PostgresOptionsBuilder(core),
            "Sqlite" => new SqliteDbContextOptionsBuilder(core),
            "SqlServer" => new SqlServerDbContextOptionsBuilder(core),
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        // Act
        object returned = engine switch
        {
            "MySql" => ((MySqlDbContextOptionsBuilder)native).UseNestedSets(),
            "PostgreSql" => ((PostgresOptionsBuilder)native).UseNestedSets(),
            "Sqlite" => ((SqliteDbContextOptionsBuilder)native).UseNestedSets(),
            "SqlServer" => ((SqlServerDbContextOptionsBuilder)native).UseNestedSets(),
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        // Assert
        Assert.Same(native, returned);
        Assert.Same(NestedSetTestAdapters.ForEngine(engine),
            core.Options.FindExtension<NestedSetOptionsExtension>()!.Provider);
    }

    /// <summary>Each typed public callback rejects a null native builder at its own boundary.</summary>
    /// <param name="engine">The callback whose receiver is absent.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    public void NativeCallbackRejectsANullBuilder(string engine)
    {
        // Arrange
        Action register = engine switch
        {
            "MySql" => () => MySqlNestedSetOptionsBuilderExtensions.UseNestedSets(null!),
            "PostgreSql" => () => PostgresNestedSetOptionsBuilderExtensions.UseNestedSets(null!),
            "Sqlite" => () => SqliteNestedSetOptionsBuilderExtensions.UseNestedSets(null!),
            "SqlServer" => () => SqlServerNestedSetOptionsBuilderExtensions.UseNestedSets(null!),
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        // Act
        var error = Record.Exception(register);

        // Assert
        Assert.IsType<ArgumentNullException>(error);
    }

    /// <summary>A second adapter cannot silently replace the first callback's context contract.</summary>
    [Fact]
    public void ConflictingAdapterRegistrationIsRejected()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<EnabledContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets());

        var selected = builder.Options.FindExtension<NestedSetOptionsExtension>();

        // Act
        var error = Record.Exception(() => builder.UseSqlServer(Connection("SqlServer"),
            sqlServer => sqlServer.UseNestedSets()));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Same(selected, builder.Options.FindExtension<NestedSetOptionsExtension>());
        Assert.Same(NestedSetTestAdapters.ForEngine("Sqlite"), selected!.Provider);
    }

    /// <summary>The common options identity includes its selected adapter, not only the extension CLR type.</summary>
    [Fact]
    public void DifferentAdaptersHaveDifferentServiceProviderIdentities()
    {
        // Arrange
        var sqlite = new NestedSetOptionsExtension(NestedSetTestAdapters.ForEngine("Sqlite"));
        var mysql = new NestedSetOptionsExtension(NestedSetTestAdapters.ForEngine("MySql"));
        var repeated = new NestedSetOptionsExtension(NestedSetTestAdapters.ForEngine("Sqlite"));
        var sqliteInfo = ((IDbContextOptionsExtension)sqlite).Info;
        var mysqlInfo = ((IDbContextOptionsExtension)mysql).Info;
        var repeatedInfo = ((IDbContextOptionsExtension)repeated).Info;

        // Act
        var foreignSame = sqliteInfo.ShouldUseSameServiceProvider(mysqlInfo);
        var repeatedSame = sqliteInfo.ShouldUseSameServiceProvider(repeatedInfo);

        // Assert
        Assert.False(foreignSame);
        Assert.False(mysqlInfo.ShouldUseSameServiceProvider(sqliteInfo));
        Assert.True(repeatedSame);
        Assert.Equal(sqliteInfo.GetServiceProviderHashCode(), repeatedInfo.GetServiceProviderHashCode());
    }

    /// <summary>All selected adapters reject options that contain no actual native provider.</summary>
    /// <param name="engine">The selected adapter whose native provider is missing.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    public void AdapterRejectsMissingNativeProvider(string engine)
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine(engine);
        var options = new DbContextOptionsBuilder().Options;

        // Act
        var error = Record.Exception(() => adapter.ValidateOptions(options));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("native database provider", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An adapter cannot accept a different actual provider merely because its core options are installed.</summary>
    /// <param name="engine">The non-SQLite adapter incorrectly paired with real SQLite options.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void AdapterRejectsAnotherNativeProvider(string engine)
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine(engine);
        var options = new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options;

        // Act
        var error = Record.Exception(() => adapter.ValidateOptions(options));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("actual native provider assembly", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A manually assembled graph may repeat exactly the selected immutable adapter.</summary>
    [Fact]
    public void ExternalServiceGraphAcceptsTheSameAdapter()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<EnabledContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets()).Options;

        using var provider = NestedSetTestAdapters.BuildServices(options);
        var isolated = new DbContextOptionsBuilder<EnabledContext>(options).UseInternalServiceProvider(provider).Options;
        using var context = new EnabledContext(isolated);

        // Act
        var actual = NestedSetProviderCapabilities.Resolve(context);
        var persistence = context.GetService<IDatabase>();

        // Assert
        Assert.Same(NestedSetTestAdapters.ForEngine("Sqlite"), actual);
        Assert.IsType<NestedSetRelationalDatabase>(persistence);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Applying an adapter cannot preserve a conflicting or opaque preexisting service descriptor.</summary>
    /// <param name="factory">Whether ownership is hidden behind a factory rather than another actual adapter.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdapterServicesRejectConflictingOrOpaqueRegistration(bool factory)
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
    public void ExternalAdapterMismatchIsRejectedBeforeModelOrIo(bool missing, bool suppliedModel)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ProbeOptions(boundary);
        using var modelServices = OwnedModelTestServices<InitializationProbeContext>.Create(options);
        using var modelContext = new InitializationProbeContext(modelServices.Options);
        var model = suppliedModel ? modelContext.Model : null;
        using var provider = NestedSetTestAdapters.BuildServices(options, services =>
        {
            services.RemoveAll<NestedSetProviderCapabilities>();

            if (!missing)
            {
                services.AddSingleton(NestedSetTestAdapters.ForEngine("MySql"));
            }
        });

        var builder = new DbContextOptionsBuilder<InitializationProbeContext>(options).UseInternalServiceProvider(provider);

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

    /// <summary>Native-only translator plugins are registered by MySql and absent from all other adapter graphs.</summary>
    /// <param name="engine">The selected actual adapter.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    public void OnlyMySqlRegistersTheNativeHierarchyTranslator(string engine)
    {
        // Arrange
        var services = new ServiceCollection();
        IDbContextOptionsExtension extension = new NestedSetOptionsExtension(NestedSetTestAdapters.ForEngine(engine));

        // Act
        extension.ApplyServices(services);
        var translators = services.Where(descriptor => descriptor.ServiceType == typeof(IMethodCallTranslatorPlugin))
            .ToArray();

        // Assert
        if (engine == "MySql")
        {
            var translator = Assert.Single(translators);

            Assert.Equal(typeof(MySqlNestedSetOptionsBuilderExtensions).Assembly, translator.ImplementationType!.Assembly);
        }
        else
        {
            Assert.Empty(translators);
        }
    }

    /// <summary>Creates actual callback options with a fail-fast I/O observer.</summary>
    private static DbContextOptions<InitializationProbeContext> ProbeOptions(CompositionIoBoundary boundary)
        => new DbContextOptionsBuilder<InitializationProbeContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets())
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

    /// <summary>Supplies valid inert connection text for provider initialization without I/O.</summary>
    private static string Connection(string engine) => engine switch
    {
        "MySql" or "MariaDb" => "Server=127.0.0.1;Database=adapter_contract;User ID=unused;Pooling=false",
        "PostgreSql" => "Host=127.0.0.1;Database=adapter_contract;Username=unused;Pooling=false",
        "SqlServer" => "Server=127.0.0.1;Database=adapter_contract;User ID=unused;Password=unused;TrustServerCertificate=true;Pooling=false",
        "Sqlite" => "Data Source=:memory:",
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };

    /// <summary>Counts convention-based model creation independently of supplied-model initialization.</summary>
    private sealed class InitializationProbeContext : DbContext
    {
        /// <summary>Creates a context whose service identity is validated before its model is requested.</summary>
        internal InitializationProbeContext(DbContextOptions<InitializationProbeContext> options) : base(options) { }

        private static readonly AsyncLocal<ModelBuildObservation?> s_observation = new();

        /// <summary>Observes model callbacks even if native validation rejects the base context constructor.</summary>
        internal static ModelBuildObservation ObserveModelCreation() => new();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder) => s_observation.Value?.Record();

        /// <summary>Counts callbacks only in the current test flow and restores an enclosing observation scope.</summary>
        internal sealed class ModelBuildObservation : IDisposable
        {
            private readonly ModelBuildObservation? _previous;

            /// <summary>Starts observing before the context constructor enters EF service initialization.</summary>
            internal ModelBuildObservation()
            {
                _previous = s_observation.Value;
                s_observation.Value = this;
            }

            /// <summary>Gets callbacks made after this observation started.</summary>
            internal int Count { get; private set; }

            /// <summary>Records an actual application model callback.</summary>
            internal void Record() => Count++;

            /// <inheritdoc />
            public void Dispose() => s_observation.Value = _previous;
        }
    }
}
