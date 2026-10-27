namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies fixture-owned shared discovery, explicit exclusions, and normal xUnit enumeration.</summary>
public sealed partial class EngineTestDiscoveryTests
{
    /// <summary>Every ordinary shared variant is inherited by all five engines without provider opt-in.</summary>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MySql", false)]
    [InlineData("MariaDb", true)]
    [InlineData("MariaDb", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("SqlServer", true)]
    [InlineData("SqlServer", false)]
    [InlineData("Sqlite", true)]
    [InlineData("Sqlite", false)]
    public async Task OrdinaryVariantsAutomaticallyCoverEveryEngine(
        string engine,
        bool preEnumerate
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.Ordinary));
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new TheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Equal(preEnumerate ? 2 : 1, cases.Count);
        Assert.All(cases, testCase => Assert.Null(testCase.SkipReason));
        Assert.Equal(engine, EngineTestSelection.ResolveEngine(method.TestClass.Class));
        Assert.Equal(typeof(DiscoveryProbe), method.Method.DeclaringType);
        Assert.Equal(method.TestClass.Class, method.Method.ReflectedType);

        if (!preEnumerate)
        {
            Assert.IsType<XunitDelayEnumeratedTheoryTestCase>(Assert.Single(cases));
        }
    }

    /// <summary>Ordinary inherited member data also covers all engines without custom filtering annotations.</summary>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public async Task OrdinaryMemberVariantsAutomaticallyCoverEveryEngine(
        string engine
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.OrdinaryMember));
        var options = CreateDiscoveryOptions(true);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new TheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Equal(2, cases.Count);
        Assert.All(cases, testCase => Assert.Null(testCase.SkipReason));
        Assert.IsType<MemberDataAttribute>(Assert.Single(method.DataAttributes));
    }

    /// <summary>Discovery reads fixture ownership from metadata without constructing the resource.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DiscoveryDoesNotConstructProviderResources(
        bool preEnumerate
    )
    {
        // Arrange
        var types = new[] { typeof(ProviderFixture<DiscoveryOnlyResource, SqlServerEngine>) };
        var method = CreateTestMethod("SqlServer", nameof(DiscoveryProbe.ConditionalInline), types);
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Single(cases);
        Assert.All(cases, testCase => Assert.Null(testCase.SkipReason));
    }

    /// <summary>Fact exclusions use the concrete injected fixture even when the method is base-declared.</summary>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    public async Task FactExclusionUsesTheConcreteFixture(
        string engine,
        bool expected
    )
    {
        // Arrange
        var reflected = CreateTestMethod(engine, nameof(DiscoveryProbe.ExcludedFact));
        var method = new XunitTestMethod(
            reflected.TestClass,
            typeof(DiscoveryProbe).GetMethod(nameof(DiscoveryProbe.ExcludedFact))!,
            []);

        var options = CreateDiscoveryOptions(true);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new EngineFactDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Equal(expected ? 1 : 0, cases.Count);
        Assert.All(cases, testCase => Assert.Null(testCase.SkipReason));
    }
}
