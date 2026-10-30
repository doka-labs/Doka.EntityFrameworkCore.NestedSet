namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class EngineTestDiscoveryTests
{
    /// <summary>Member-data variants apply explicit exclusions while default rows run on every engine.</summary>
    [Theory]
    [InlineData("MySql", 3)]
    [InlineData("MariaDb", 3)]
    [InlineData("PostgreSql", 2)]
    [InlineData("SqlServer", 3)]
    [InlineData("Sqlite", 2)]
    public async Task MemberVariantsUseTheFixtureEngine(
        string engine,
        int expectedCount
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.Member));
        var attribute = Assert.IsType<EngineMemberDataAttribute>(Assert.Single(method.DataAttributes));
        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetData(method.Method, tracker);

        // Assert
        Assert.Equal(expectedCount, rows.Count);
        Assert.Contains(rows, row => Equals(row.GetData()[0], 43));
        Assert.Equal(method.Method.ReflectedType, attribute.MemberType);
        Assert.Equal(engine != "Sqlite", rows.Any(row => Equals(row.GetData()[0], 17)));
        Assert.Equal(engine != "PostgreSql", rows.Any(row => Equals(row.GetData()[0], 29)));
    }

    /// <summary>An explicit member source remains authoritative over the inherited suite's default source.</summary>
    [Fact]
    public async Task ExplicitMemberTypeIsPreserved()
    {
        // Arrange
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Member));
        var attribute = new EngineMemberDataAttribute(nameof(DiscoveryProbe.Rows))
        {
            MemberType = typeof(ExplicitDataSource),
        };

        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetData(method.Method, tracker);

        // Assert
        Assert.Equal(typeof(ExplicitDataSource), attribute.MemberType);
        Assert.Equal<object?>([53], Assert.Single(rows).GetData());
    }

    /// <summary>Raw member rows retain their engine exclusions for the provider contract guard.</summary>
    [Fact]
    public async Task SourceInspectionPreservesExcludedRows()
    {
        // Arrange
        var method = CreateTestMethod("Sqlite", nameof(DiscoveryProbe.Member));
        var attribute = Assert.IsType<EngineMemberDataAttribute>(Assert.Single(method.DataAttributes));
        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetSourceData(method.Method, tracker);

        // Assert
        Assert.Equal(3, rows.Count);
        var first = Assert.IsType<EngineTheoryDataRow>(rows.First());
        Assert.Equal(["Sqlite"], first.ExcludedEngines);
        Assert.Equal("SQLite has no server-backed variant in this metadata control.", first.Reason);
        Assert.Equal<object?>([17], first.Data);
    }

    /// <summary>Row normalization retains exclusions, xUnit settings, and merged attribute traits.</summary>
    [Fact]
    public async Task MemberNormalizationPreservesMergedRowMetadata()
    {
        // Arrange
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Member));
        var attribute = new EngineMemberDataAttribute(nameof(ExplicitDataSource.MetadataRows))
        {
            MemberType = typeof(ExplicitDataSource),
            Label = "attribute-label",
            Traits = ["attribute", "attribute-value"],
        };

        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetSourceData(method.Method, tracker);

        // Assert
        var row = Assert.IsType<EngineTheoryDataRow>(Assert.Single(rows));
        Assert.Equal<object?>([61], row.Data);
        Assert.Equal(["SqlServer"], row.ExcludedEngines);
        Assert.Equal("This synthetic variant requires no SQL Server execution.", row.Reason);
        Assert.Equal("row-label", row.Label);
        Assert.Equal("row-display", row.TestDisplayName);
        Assert.Equal("row-skip", row.Skip);
        Assert.Equal(73, row.Timeout);
        Assert.True(row.Explicit);
        Assert.True(row.DisableParallelization);
        Assert.NotNull(row.Traits);
        Assert.Contains("row-value", row.Traits["row"]);
        Assert.Contains("attribute-value", row.Traits["attribute"]);
    }

    /// <summary>Filtered member discovery enumerates its source once in the phase chosen by xUnit.</summary>
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 0)]
    public async Task DiscoveryDoesNotEnumerateMemberFactoriesTwice(
        bool preEnumerate,
        bool delayedSource,
        int expectedCalls
    )
    {
        // Arrange
        DiscoveryProbe.SourceCalls = 0;
        var name = delayedSource ? nameof(DiscoveryProbe.DelayedMember) : nameof(DiscoveryProbe.CountedMember);
        var method = CreateTestMethod("MySql", name);
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Equal(expectedCalls, DiscoveryProbe.SourceCalls);
        Assert.Equal(expectedCalls == 1 ? 2 : 1, cases.Count);

        if (expectedCalls == 0)
        {
            Assert.IsType<XunitDelayEnumeratedTheoryTestCase>(Assert.Single(cases));
        }
    }

    /// <summary>A delayed source yields its arguments when requested during execution.</summary>
    [Fact]
    public async Task DelayedMemberDataEnumeratesOnceWhenRequested()
    {
        // Arrange
        DiscoveryProbe.SourceCalls = 0;
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.DelayedMember));
        var attribute = Assert.IsType<EngineMemberDataAttribute>(Assert.Single(method.DataAttributes));
        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetData(method.Method, tracker);

        // Assert
        Assert.Equal(1, DiscoveryProbe.SourceCalls);
        Assert.Equal(2, rows.Count);
        Assert.False(attribute.SupportsDiscoveryEnumeration());
    }

    /// <summary>Empty and accidentally over-filtered sources remain visible failures in both runner modes.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task EmptyOrOverFilteredSourceRemainsAFailure(
        bool preEnumerate,
        bool overFiltered
    )
    {
        // Arrange
        var name = overFiltered ? nameof(DiscoveryProbe.OverFiltered) : nameof(DiscoveryProbe.Empty);
        var method = CreateTestMethod("Sqlite", name);
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.IsType<EngineTheoryAttribute>(Assert.Single(method.FactAttributes));

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.False(attribute.SkipTestWithoutData);
        var testCase = Assert.Single(cases);
        Assert.Null(testCase.SkipReason);

        if (preEnumerate)
        {
            var failure = Assert.IsType<ExecutionErrorTestCase>(testCase);
            Assert.Contains("No data found", failure.ErrorMessage, StringComparison.Ordinal);
        }
        else
        {
            var delayed = Assert.IsType<XunitDelayEnumeratedTheoryTestCase>(testCase);
            Assert.False(delayed.SkipTestWithoutData);
        }
    }

    /// <summary>Invalid row metadata is rejected even when a valid member-level exclusion omits its engine.</summary>
    [Fact]
    public async Task WholeMemberExclusionDoesNotHideInvalidRows()
    {
        // Arrange
        var method = CreateTestMethod("Sqlite", nameof(DiscoveryProbe.Member));
        var attribute = new EngineMemberDataAttribute(nameof(ExplicitDataSource.InvalidRows))
        {
            MemberType = typeof(ExplicitDataSource),
            ExcludedEngines = ["Sqlite"],
            Reason = "The member-level metadata control excludes SQLite.",
        };

        await using var tracker = new DisposalTracker();

        // Act
        var error = await Record.ExceptionAsync(async () => await attribute.GetData(method.Method, tracker));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Unknown excluded integration test provider", error.Message, StringComparison.Ordinal);
    }
}
