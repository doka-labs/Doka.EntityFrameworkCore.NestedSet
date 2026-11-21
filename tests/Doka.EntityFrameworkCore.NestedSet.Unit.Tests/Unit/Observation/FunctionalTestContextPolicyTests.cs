using Microsoft.EntityFrameworkCore.Diagnostics;
using ProviderGuidFormat = Doka.EntityFrameworkCore.MySql.MySqlGuidFormat;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards immutable functional timeout options without losing provider configuration.</summary>
public sealed class FunctionalTestContextPolicyTests
{
    /// <summary>
    /// The cloned zero-timeout option retains its provider type and every unrelated relational setting.
    /// </summary>
    /// <param name="engine">The provider whose immutable options are cloned.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void ConfigureOptions_ProviderOptions_PreservesSubtypeAndUnrelatedSettings(
        string engine
    )
    {
        // Arrange
        var original = Options(engine);
        var before = RelationalOptionsExtension.Extract(original);
        var builder = new DbContextOptionsBuilder<TreeContext>(original);

        // Act
        FunctionalTestContextPolicy.ConfigureOptions(builder);
        var configured = RelationalOptionsExtension.Extract(builder.Options);
        var clone = new DbContextOptionsBuilder<TreeContext>(builder.Options).Options;
        var copied = RelationalOptionsExtension.Extract(clone);

        // Assert
        Assert.Equal(before.GetType(), configured.GetType());
        Assert.Equal(before.GetType(), copied.GetType());
        Assert.NotSame(before, configured);
        Assert.Same(before, RelationalOptionsExtension.Extract(original));
        Assert.Equal(17, before.CommandTimeout);
        Assert.Equal(29, before.MaxBatchSize);
        Assert.True(before.UseRelationalNulls);
        Assert.Equal(0, configured.CommandTimeout);
        Assert.Equal(0, copied.CommandTimeout);
        Assert.Equal(29, configured.MaxBatchSize);
        Assert.Equal(29, copied.MaxBatchSize);
        Assert.True(configured.UseRelationalNulls);
        Assert.True(copied.UseRelationalNulls);
        Assert.Equal(before.ConnectionString, configured.ConnectionString);
        Assert.Equal(before.ConnectionString, copied.ConnectionString);
    }

    /// <summary>Doka-specific GUID storage survives the timeout copy and later options-builder cloning.</summary>
    /// <param name="engine">The configured MySQL or MariaDB profile.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void ConfigureOptions_MySqlGuidFormat_SurvivesOptionsClone(
        string engine
    )
    {
        // Arrange
        var original = Options(engine);
        var builder = new DbContextOptionsBuilder<TreeContext>(original);

        // Act
        FunctionalTestContextPolicy.ConfigureOptions(builder);
        var clone = new DbContextOptionsBuilder<TreeContext>(builder.Options).Options;
        // WHY: GUID-format variants belong to this option-copy test, not EF's process-global provider cache.
        using var services = OwnedModelTestServices<TreeContext>.Create(clone);
        using var context = new TreeContext(services.Options);
        var mapping = context.Model.FindEntityType(typeof(TreeNode))!
            .FindProperty(nameof(TreeNode.TreeId))!
            .GetRelationalTypeMapping();

        // Assert
        var core = Assert.IsType<CoreOptionsExtension>(services.Options.FindExtension<CoreOptionsExtension>());
        Assert.NotNull(core.InternalServiceProvider);
        Assert.Equal(WarningBehavior.Throw, core.WarningsConfiguration.GetBehavior(
            CoreEventId.ManyServiceProvidersCreatedWarning));

        Assert.Equal("char(36)", mapping.StoreType);
        Assert.Equal(0, context.Database.GetCommandTimeout());
        Assert.Equal(17, RelationalOptionsExtension.Extract(original).CommandTimeout);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>A missing relational provider is rejected rather than creating unusable provider options.</summary>
    [Fact]
    public void ConfigureOptions_MissingProvider_RejectsWithoutMutatingOptions()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        var original = builder.Options;

        // Act
        var error = Record.Exception(() => FunctionalTestContextPolicy.ConfigureOptions(builder));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Same(original, builder.Options);
        Assert.Empty(builder.Options.Extensions);
    }

    /// <summary>
    /// Sets nondefault limits and options so a partial extension reconstruction cannot pass unnoticed.
    /// </summary>
    private static DbContextOptions<TreeContext> Options(
        string engine
    )
    {
        var builder = new DbContextOptionsBuilder<TreeContext>();

        switch (engine)
        {
            case "MySql":
            case "MariaDb":
                builder.UseMySql(
                    "Server=localhost;Database=functional_options",
                    engine == "MySql"
                        ? DatabaseTestTargets.MySql
                        : DatabaseTestTargets.MariaDb,
                    provider => provider
                        .CommandTimeout(17)
                        .MaxBatchSize(29)
                        .UseRelationalNulls()
                        .DefaultGuidFormat(ProviderGuidFormat.Char36)
                        .UseNestedSets());

                break;
            case "PostgreSql":
                builder.UseNpgsql(
                    "Host=localhost;Database=functional_options",
                    provider => provider.CommandTimeout(17).MaxBatchSize(29).UseRelationalNulls().UseNestedSets());

                break;
            case "SqlServer":
                builder.UseSqlServer(
                    "Server=localhost;Database=functional_options",
                    provider => provider.CommandTimeout(17).MaxBatchSize(29).UseRelationalNulls().UseNestedSets());

                break;
            case "Sqlite":
                builder.UseSqlite(
                    "Data Source=:memory:",
                    provider => provider.CommandTimeout(17).MaxBatchSize(29).UseRelationalNulls().UseNestedSets());

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        return builder.Options;
    }
}
