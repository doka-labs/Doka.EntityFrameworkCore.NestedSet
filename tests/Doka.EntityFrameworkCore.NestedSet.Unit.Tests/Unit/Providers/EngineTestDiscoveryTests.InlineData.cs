namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class EngineTestDiscoveryTests
{
    /// <summary>Excluded methods vanish before either enumeration strategy invokes their faulty factory.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MethodExclusionDoesNotInvokeDataFactories(
        bool preEnumerate
    )
    {
        // Arrange
        var method = CreateTestMethod("Sqlite", nameof(DiscoveryProbe.ExcludedTheory));
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Empty(cases);
    }

    /// <summary>Explicit inline exclusions retain every original argument and omit only the named engine.</summary>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    public async Task InlineExclusionsPreserveArguments(
        string engine,
        bool expected
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.ConditionalInline));
        var attribute = Assert.IsType<EngineInlineDataAttribute>(Assert.Single(method.DataAttributes));
        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetData(method.Method, tracker);

        // Assert
        if (expected)
        {
            Assert.Equal<object?>([17], Assert.Single(rows).GetData());
        }
        else
        {
            Assert.Empty(rows);
        }
    }

    /// <summary>Invalid exclusions fail even when their first entry would exclude the current engine.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ExclusionsRequireAReason(
        string? reason
    )
    {
        // Arrange
        var excluded = new[] { "Sqlite" };

        // Act
        var error = Record.Exception(() => EngineTestSelection.AppliesToEngine("Sqlite", excluded, reason));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("require a nonempty reason", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Every excluded engine is validated before the applicable-row decision.</summary>
    [Fact]
    public void UnknownExcludedEngineIsRejected()
    {
        // Arrange
        var excluded = new[] { "Sqlite", "Postgres" };

        // Act
        var error = Record.Exception(() => EngineTestSelection.AppliesToEngine("Sqlite", excluded, "A real reason."));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(
            "Unknown excluded integration test provider 'Postgres'",
            error.Message,
            StringComparison.Ordinal);
    }
}
