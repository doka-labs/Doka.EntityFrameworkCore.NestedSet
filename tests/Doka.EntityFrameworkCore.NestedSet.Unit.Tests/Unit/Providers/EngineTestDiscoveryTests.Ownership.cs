namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class EngineTestDiscoveryTests
{
    /// <summary>Default marker ownership never depends on which other engine fixtures a collection registers.</summary>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void ConstructorSelectsEngineFromAMultiEngineCollection(
        string engine
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.Ordinary));
        var collection = new XunitTestCollection(
            method.TestClass.TestCollection.TestAssembly,
            typeof(MultiEngineCollectionDefinition),
            false,
            "Both MySQL engines");

        var testClass = new XunitTestClass(method.TestClass.Class, collection);

        // Act
        var resolved = EngineTestSelection.ResolveEngine(testClass.Class);

        // Assert
        Assert.Equal(engine, resolved);
        Assert.Contains(typeof(ProviderFixture<HarmlessResource, MySqlEngine>), collection.CollectionFixtureTypes);
        Assert.Contains(typeof(ProviderFixture<HarmlessResource, MariaDbEngine>), collection.CollectionFixtureTypes);
    }

    /// <summary>A concrete suite must inject its exact closed provider fixture rather than an interface view.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingConcreteFixtureIsRejected(
        bool interfaceView
    )
    {
        // Arrange
        var types = interfaceView ? [typeof(IProviderFixture)] : Type.EmptyTypes;
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Ordinary), types);

        // Act
        var error = Record.Exception(() => EngineTestSelection.ResolveEngine(method.TestClass.Class));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("inject a concrete provider fixture", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Two different constructor-selected engines cannot silently choose one fixture.</summary>
    [Fact]
    public void ContradictoryConstructorFixturesAreRejected()
    {
        // Arrange
        var types = new[]
        {
            typeof(ProviderFixture<HarmlessResource, MySqlEngine>),
            typeof(ProviderFixture<HarmlessResource, MariaDbEngine>),
        };

        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Ordinary), types);

        // Act
        var error = Record.Exception(() => EngineTestSelection.ResolveEngine(method.TestClass.Class));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("contradictory provider fixtures", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An engine fixture from another provider executable is a visible configuration failure.</summary>
    [Fact]
    public void ForeignConstructorFixtureIsRejected()
    {
        // Arrange
        var types = new[] { typeof(ProviderFixture<HarmlessResource, MySqlEngine>) };
        var method = CreateTestMethod("Sqlite", nameof(DiscoveryProbe.Ordinary), types);

        // Act
        var error = Record.Exception(() => EngineTestSelection.ResolveEngine(method.TestClass.Class));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("foreign provider project", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A marker typo cannot disappear through ownership filtering.</summary>
    [Fact]
    public void UnknownFixtureMarkerIsRejected()
    {
        // Arrange
        var types = new[] { typeof(ProviderFixture<HarmlessResource, UnknownEngine>) };
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Ordinary), types);

        // Act
        var error = Record.Exception(() => EngineTestSelection.ResolveEngine(method.TestClass.Class));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Unknown integration test provider", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Concurrent initialization preserves separate resources and immutable engine identities.</summary>
    [Fact]
    public async Task ProviderFixtureInitializationKeepsEnginesAndResourcesIsolated()
    {
        // Arrange
        await using var first = new ProviderFixture<HarmlessResource, MySqlEngine>();
        await using var second = new ProviderFixture<HarmlessResource, MariaDbEngine>();

        // Act
        await Task.WhenAll(
            first
                .InitializeAsync()
                .AsTask(),
            second
                .InitializeAsync()
                .AsTask());

        // Assert
        Assert.Equal("MySql", first.Engine);
        Assert.Equal("MariaDb", second.Engine);
        Assert.NotSame(first.Value, second.Value);
        Assert.Equal(1, first.Value.Initializations);
        Assert.Equal(1, second.Value.Initializations);
    }

    /// <summary>Disposing one fixture preserves its engine identity and leaves the peer resource alive.</summary>
    [Fact]
    public async Task ProviderFixtureDisposalKeepsOwnershipAndEngineIdentity()
    {
        // Arrange
        var first = new ProviderFixture<HarmlessResource, MySqlEngine>();
        await using var second = new ProviderFixture<HarmlessResource, MariaDbEngine>();
        var firstResource = first.Value;
        var secondResource = second.Value;

        // Act
        await first.DisposeAsync();

        // Assert
        Assert.Equal("MySql", first.Engine);
        Assert.Equal("MariaDb", second.Engine);
        Assert.Same(firstResource, first.Value);
        Assert.Same(secondResource, second.Value);
        Assert.Equal(1, firstResource.Disposals);
        Assert.Equal(0, secondResource.Disposals);
    }
}
