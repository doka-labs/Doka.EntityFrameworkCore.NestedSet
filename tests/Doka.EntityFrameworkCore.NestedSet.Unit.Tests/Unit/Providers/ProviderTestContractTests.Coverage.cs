namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderTestContractTests
{
    /// <summary>Shared facts confined to one engine or the Doka family belong in their provider executable.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedSingleExecutableFactsAreRejected(
        bool family
    )
    {
        // Arrange
        var excluded = family
            ? new[] { "PostgreSql", "SqlServer", "Sqlite" }
            : new[] { "MySql", "MariaDb", "PostgreSql", "SqlServer" };

        var method = CreateProbe([NeutralFact(excluded)])
            .GetMethod("Probe0")!;

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("confined to one provider executable", StringComparison.Ordinal));
    }

    /// <summary>Shared applicability spanning two executables remains valid with explicit prerequisites.</summary>
    [Fact]
    public void SharedCrossExecutableFactRemainsAccepted()
    {
        // Arrange
        var method = CreateProbe([NeutralFact(["SqlServer", "Sqlite"])])
            .GetMethod("Probe0")!;

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Even explained exclusions may not erase every engine from a shared fact.</summary>
    [Fact]
    public void SharedEmptyFactCoverageIsRejected()
    {
        // Arrange
        var method = CreateProbe(
            [
                NeutralFact(
                [
                    "MySql",
                    "MariaDb",
                    "PostgreSql",
                    "SqlServer",
                    "Sqlite",
                ]),
            ])
            .GetMethod("Probe0")!;

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("effective engine coverage is empty", StringComparison.Ordinal));
    }

    /// <summary>Individually broad method and inline exclusions may jointly leave only one executable.</summary>
    [Fact]
    public void SharedMethodAndInlineExclusionsAreComposed()
    {
        // Arrange
        var method = CreateProbe([NeutralTheory(["MySql", "MariaDb"]), NeutralInline([17], ["SqlServer", "Sqlite"])])
            .GetMethod("Probe0")!;

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("one provider executable 'PostgreSql'", StringComparison.Ordinal));
    }

    /// <summary>Single-project rows cannot disguise ownership by collectively spanning several projects.</summary>
    [Fact]
    public void EverySharedInlineVariantRequiresCrossExecutableCoverage()
    {
        // Arrange
        var method = CreateProbe(
            [
                NeutralTheory(),
                NeutralInline([17], ["MySql", "MariaDb", "SqlServer", "Sqlite"]),
                NeutralInline([18], ["MySql", "MariaDb", "PostgreSql", "Sqlite"])
            ])
            .GetMethod("Probe0")!;

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Equal(2, errors.Length);
        Assert.All(
            errors,
            error => Assert.Contains("confined to one provider executable", error, StringComparison.Ordinal));
    }

    /// <summary>Method, member, and row exclusions compose before deciding which executable owns a variant.</summary>
    [Fact]
    public async Task SharedMethodMemberAndRowExclusionsAreComposed()
    {
        // Arrange
        var method = CreateProbe(
            [
                NeutralTheory(["MySql"]),
                SharedMember(nameof(RawSources.ValidConditionalRows), ["MariaDb", "SqlServer"])
            ])
            .GetMethod("Probe0")!;

        // Act
        var errors = await ProviderTestContract.FindSharedDataErrorsAsync(method);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("one provider executable 'PostgreSql'", StringComparison.Ordinal));
    }

    /// <summary>Member data confined to the Doka family belongs in its provider executable.</summary>
    [Fact]
    public async Task SharedMemberFamilyOnlyCoverageIsRejected()
    {
        // Arrange
        var method = CreateProbe([NeutralTheory(), SharedMember(nameof(RawSources.FamilyConditionalRows))])
            .GetMethod("Probe0")!;

        // Act
        var errors = await ProviderTestContract.FindSharedDataErrorsAsync(method);

        // Assert
        Assert.Contains(errors, error => error.Contains("one provider executable 'MySql'", StringComparison.Ordinal));
    }

    /// <summary>Combined member and row exclusions may not erase every engine from a shared variant.</summary>
    [Fact]
    public async Task SharedMemberEmptyEffectiveCoverageIsRejected()
    {
        // Arrange
        var method = CreateProbe(
            [
                NeutralTheory(["MySql", "MariaDb", "PostgreSql"]),
                SharedMember(nameof(RawSources.ValidConditionalRows), ["SqlServer"])
            ])
            .GetMethod("Probe0")!;

        // Act
        var errors = await ProviderTestContract.FindSharedDataErrorsAsync(method);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("effective engine coverage is empty", StringComparison.Ordinal));
    }

    /// <summary>Ordinary shared member data also retains the unfiltered empty-source guard.</summary>
    [Fact]
    public async Task SharedOrdinaryEmptyMemberSourceIsRejected()
    {
        // Arrange
        var method = CreateProbe([NeutralTheory(), NeutralMember(nameof(RawSources.EmptyRows))])
            .GetMethod("Probe0")!;

        // Act
        var errors = await ProviderTestContract.FindSharedDataErrorsAsync(method);

        // Assert
        Assert.Contains(errors, error => error.Contains("is empty", StringComparison.Ordinal));
    }
}
