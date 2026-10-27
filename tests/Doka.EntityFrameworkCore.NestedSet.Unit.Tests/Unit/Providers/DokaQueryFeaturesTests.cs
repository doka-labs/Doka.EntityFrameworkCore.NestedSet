using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies direct Doka strategy selection through the selected native adapter.</summary>
public sealed partial class DokaQueryFeaturesTests
{
    /// <summary>The actual configured Doka family selects its qualified strategy without database I/O.</summary>
    /// <param name="engine">The configured engine sharing Doka's actual native adapter.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task ActualConfiguredProviderChoosesItsTypedStrategyWithoutIo(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionGuardContext>(
                ModelCompatibilityDatabase.Options<CompositionGuardContext>(engine,
                    "Server=127.0.0.1;Database=feature_contract;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        await using var provider = NestedSetTestAdapters.BuildServices(options);
        var isolated = new DbContextOptionsBuilder<CompositionGuardContext>(options)
            .UseInternalServiceProvider(provider).Options;

        await using var context = new CompositionGuardContext(isolated);
        var expected = engine == "MySql"
            ? NestedSetSubtreeQueryStrategy.RelatedFirstUnion
            : NestedSetSubtreeQueryStrategy.SelectedKeyUnion;

        // Act
        var capability = NestedSetProviderCapabilities.Resolve(context);
        var strategy = capability.ResolveSubtreeQueryStrategy(context);
        var completeMatches = capability.RequiresCompletedAncestorMatches(context);

        // Assert
        Assert.Same(NestedSetTestAdapters.ForEngine(engine), capability);
        Assert.Equal(expected, strategy);
        Assert.Equal(engine == "MySql", completeMatches);
        Assert.Same(typeof(MySqlDbContextOptionsBuilder).Assembly, capability.ProviderAssembly);
        Assert.NotNull(capability.CompletedSourceMethod);
        Assert.NotNull(capability.OrderedJoinMethod);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Portable adapters expose no foreign Doka marker and keep their own structural range strategy.</summary>
    /// <param name="engine">The actual non-Doka provider whose complete service graph is constructed.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task OtherAdaptersDoNotExposeDokaQueryPrimitives(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionGuardContext>(
                ModelCompatibilityDatabase.Options<CompositionGuardContext>(engine, MetadataConnection(engine)))
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        await using var provider = NestedSetTestAdapters.BuildServices(options);
        var isolated = new DbContextOptionsBuilder<CompositionGuardContext>(options)
            .UseInternalServiceProvider(provider).Options;

        await using var context = new CompositionGuardContext(isolated);

        // Act
        var capability = NestedSetProviderCapabilities.Resolve(context);
        var strategy = capability.ResolveSubtreeQueryStrategy(context);

        // Assert
        Assert.Same(NestedSetTestAdapters.ForEngine(engine), capability);
        Assert.Equal(NestedSetSubtreeQueryStrategy.RangeJoin, strategy);
        Assert.Null(capability.CompletedSourceMethod);
        Assert.Null(capability.OrderedJoinMethod);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Provides inert connection text valid for the requested provider's metadata initialization.</summary>
    private static string MetadataConnection(string engine) => engine switch
    {
        "Sqlite" => "Data Source=:memory:",
        "PostgreSql" => "Host=127.0.0.1;Database=feature_contract;Username=unused;Pooling=false",
        "SqlServer" => "Server=127.0.0.1;Database=feature_contract;User ID=unused;Password=unused;TrustServerCertificate=true;Pooling=false",
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
}
