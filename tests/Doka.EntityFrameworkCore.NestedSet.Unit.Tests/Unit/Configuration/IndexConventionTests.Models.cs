namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class IndexConventionTests
{
    /// <summary>Maps a distinct real-provider model for each configuration without connecting to a database.</summary>
    private sealed class ConventionContext : DbContext, ITestModelVariant
    {
        /// <summary>Supplies the mapping scenario after the convention has been registered.</summary>
        private readonly Action<ModelBuilder> _configure;

        /// <summary>Creates one independently cached model whose provider conventions remain active.</summary>
        internal ConventionContext(
            string engine,
            Action<ModelBuilder> configure
        ) : base(Options(engine))
        {
            _configure = configure;
        }

        /// <summary>Gets the independent cache identity for this test's model configuration.</summary>
        internal Guid Identity { get; } = Guid.NewGuid();

        /// <inheritdoc />
        object ITestModelVariant.ModelVariant => Identity;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => _configure(modelBuilder);

        /// <summary>Uses each supported provider's public options API with a fixed server version.</summary>
        private static DbContextOptions<ConventionContext> Options(
            string engine
        )
        {
            var options = new DbContextOptionsBuilder<ConventionContext>().ConfigureTestWarnings();
            options.ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>();

            // WHY: Repeated options registration must not duplicate finalizing conventions in generated models.
            options.UseNestedSets();
            options.UseNestedSets();

            switch (engine)
            {
                case "Sqlite":
                    options.UseSqlite("Data Source=:memory:");
                    break;
                case "MySql":
                    options.UseMySql(
                        "Server=127.0.0.1;Database=Conventions;User ID=unused",
                        DatabaseTestTargets.MySql);
                    break;
                case "MariaDb":
                    options.UseMySql(
                        "Server=127.0.0.1;Database=Conventions;User ID=unused",
                        DatabaseTestTargets.MariaDb);
                    break;
                case "SqlServer":
                    options.UseSqlServer("Server=127.0.0.1;Database=Conventions;User ID=unused;Password=unused");
                    break;
                case "PostgreSql":
                    options.UseNpgsql("Host=127.0.0.1;Database=Conventions;Username=unused");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(engine));
            }

            return options.Options;
        }
    }

}
