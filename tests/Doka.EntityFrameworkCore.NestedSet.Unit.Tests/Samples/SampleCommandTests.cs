namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Rejects ambiguous console invocations before samples construct a database configuration.</summary>
public sealed class SampleCommandTests
{
    /// <summary>The default invocation selects Doka with MariaDB and every documented scenario.</summary>
    [Fact]
    public void DefaultInvocationSelectsMariaDbAndAllScenarios()
    {
        // Arrange
        string[] args = [];
        string[] scenarios = ["create", "move"];

        // Act
        var command = SampleCommand.Parse(args, scenarios);

        // Assert
        Assert.Equal(SampleProvider.MariaDb, command.Provider);
        Assert.Equal("all", command.Scenario);
        Assert.False(command.Inspect);
        Assert.False(command.Reset);
        Assert.False(command.Details);
        Assert.False(command.Help);
    }

    /// <summary>Documented provider names are case-insensitive without accepting arbitrary providers.</summary>
    /// <param name="name">The console provider value.</param>
    /// <param name="expected">The resolved provider route.</param>
    [Theory]
    [InlineData("mariadb", "MariaDb")]
    [InlineData("MariaDB", "MariaDb")]
    [InlineData("mysql", "MySql")]
    [InlineData("MySQL", "MySql")]
    [InlineData("sqlite", "Sqlite")]
    [InlineData("SQLite", "Sqlite")]
    public void DocumentedProviderIsSelected(
        string name,
        string expected
    )
    {
        // Arrange
        string[] args = ["--provider", name];
        string[] scenarios = ["create", "move"];

        // Act
        var command = SampleCommand.Parse(args, scenarios);

        // Assert
        Assert.Equal(expected, command.Provider.ToString());
    }

    /// <summary>Each named scenario and the explicit all selection retain reset and detail flags.</summary>
    /// <param name="scenario">The selected documented scenario.</param>
    [Theory]
    [InlineData("create")]
    [InlineData("move")]
    [InlineData("all")]
    public void DocumentedScenarioRetainsExecutionFlags(
        string scenario
    )
    {
        // Arrange
        string[] args = ["--scenario", scenario, "--reset", "--details"];
        string[] scenarios = ["create", "move"];

        // Act
        var command = SampleCommand.Parse(args, scenarios);

        // Assert
        Assert.Equal(scenario, command.Scenario);
        Assert.True(command.Reset);
        Assert.True(command.Details);
        Assert.False(command.Inspect);
    }

    /// <summary>Inspection accepts provider and detail selection without requesting a mutation.</summary>
    [Fact]
    public void InspectionRetainsProviderAndDetails()
    {
        // Arrange
        string[] args = ["--provider", "sqlite", "--inspect", "--details"];
        string[] scenarios = ["create"];

        // Act
        var command = SampleCommand.Parse(args, scenarios);

        // Assert
        Assert.True(command.Inspect);
        Assert.True(command.Details);
        Assert.False(command.Reset);
        Assert.Equal(SampleProvider.Sqlite, command.Provider);
    }

    /// <summary>Help is parsed without requiring a provider or scenario value.</summary>
    [Fact]
    public void HelpDoesNotRequireExecutionOptions()
    {
        // Arrange
        string[] args = ["--help"];
        string[] scenarios = ["create"];

        // Act
        var command = SampleCommand.Parse(args, scenarios);

        // Assert
        Assert.True(command.Help);
        Assert.False(command.Reset);
        Assert.False(command.Inspect);
    }

    /// <summary>Invalid commands fail before an endpoint or destructive reset can be selected.</summary>
    /// <param name="args">The invalid independent invocation.</param>
    /// <param name="message">The diagnostic identifying the command defect.</param>
    [Theory]
    [MemberData(nameof(InvalidInvocations))]
    public void InvalidInvocationIsRejected(
        string[] args,
        string message
    )
    {
        // Arrange
        string[] scenarios = ["create", "move"];

        // Act
        var error = Record.Exception(() => SampleCommand.Parse(args, scenarios));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Supplies separate parser failures without combining several Arrange/Act cycles in one test.</summary>
    public static TheoryData<string[], string> InvalidInvocations =>
        new()
        {
            { ["--unknown"], "Unknown option" },
            { ["move"], "Unknown option" },
            { ["--provider"], "require a value" },
            { ["--provider", ""], "require a value" },
            { ["--provider", " "], "require a value" },
            { ["--provider", "--reset"], "require a value" },
            { ["--scenario"], "require a value" },
            { ["--scenario", "--inspect"], "require a value" },
            { ["--provider", "postgresql"], "Provider must be" },
            { ["--scenario", "missing"], "Unknown scenario" },
            { ["--scenario", "Move"], "Unknown scenario" },
            { ["--reset", "--reset"], "more than once" },
            { ["--inspect", "--inspect"], "more than once" },
            { ["--details", "--details"], "more than once" },
            { ["--help", "--help"], "more than once" },
            { ["--provider", "mysql", "--provider", "sqlite"], "more than once" },
            { ["--scenario", "create", "--scenario", "move"], "more than once" },
            { ["--inspect", "--reset"], "cannot be combined" },
            { ["--reset", "--inspect"], "cannot be combined" },
            { ["--inspect", "--scenario", "create"], "cannot be combined" },
            { ["--scenario", "all", "--inspect"], "cannot be combined" },
        };
}
