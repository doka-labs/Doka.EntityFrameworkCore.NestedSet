namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TreeRegistryTableNameTests
{
    /// <summary>Finalizes a model at each provider's documented limit, measured in that provider's unit.</summary>
    /// <param name="provider">The provider under test.</param>
    /// <param name="name">A name exactly at that provider's limit.</param>
    [Theory]
    [MemberData(nameof(FittingNames))]
    public void FinalizeModel_NameAtTheProviderLimit_IsAccepted(
        string provider,
        string name
    )
    {
        // Arrange
        using var context = CreateFor(provider, modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", name));

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        // Assert
        Assert.Equal(
            name,
            NestedSetTreeRegistryMapping.For(model.FindEntityType(typeof(FirstNode))!)
                .Store.Name);
    }

    /// <summary>Rejects a name one unit beyond the limit and reports the measured length, unit and limit.</summary>
    /// <param name="provider">The provider under test.</param>
    /// <param name="name">A name one unit beyond that provider's limit.</param>
    /// <param name="measured">The expected measured length and unit.</param>
    /// <param name="limit">The expected limit statement.</param>
    [Theory]
    [MemberData(nameof(ExceedingNames))]
    public void FinalizeModel_NameBeyondTheProviderLimit_IsRejected(
        string provider,
        string name,
        string measured,
        string limit
    )
    {
        // Arrange
        using var context = CreateFor(provider, modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", name));

        // Act
        var error = Record.Exception(() => _ = context.GetService<IDesignTimeModel>()
            .Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(measured, failure.Message, StringComparison.Ordinal);
        Assert.Contains(limit, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects SQLite's reserved table prefix before attempting DDL.</summary>
    [Fact]
    public void FinalizeModel_SqliteReservedName_IsRejectedDuringFinalization()
    {
        // Arrange
        using var context = Create(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", "SQLite_Trees"));

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.Contains(
            "reserved",
            Assert.IsType<InvalidOperationException>(error)
                .Message);
    }

    /// <summary>Rejects SQL Server's temporary-table prefix during design-time model finalization.</summary>
    [Fact]
    public void FinalizeModel_SqlServerReservedName_IsRejectedDuringFinalization()
    {
        // Arrange
        using var context = CreateSqlServer(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", "#Trees"));

        // Act
        var error = Record.Exception(() => _ = context.GetService<IDesignTimeModel>()
            .Model);

        // Assert
        Assert.Contains(
            "reserved",
            Assert.IsType<InvalidOperationException>(error)
                .Message);
    }
}
