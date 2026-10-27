namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderTestContractTests
{
    /// <summary>Ordinary shared annotations are valid because the concrete fixture supplies engine coverage.</summary>
    [Fact]
    public void SharedOrdinaryAnnotationsAreAccepted()
    {
        // Arrange
        var theory = new CustomAttributeBuilder(
            typeof(TheoryAttribute).GetConstructor([typeof(string), typeof(int)])!,
            [string.Empty, -1]);

        var inline = new CustomAttributeBuilder(
            typeof(InlineDataAttribute).GetConstructor([typeof(object[])])!,
            [new object[] { 17 }]);

        var method = CreateProbe([theory, inline])
            .GetMethod("Probe0")!;

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A historical inline engine row cannot override the engine owned by the concrete fixture.</summary>
    [Fact]
    public void SharedEngineArgumentIsRejected()
    {
        // Arrange
        var method = CreateParameterProbe("engine", true);

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("must read Engine from the fixture", error, StringComparison.Ordinal);
    }

    /// <summary>A deliberately foreign engine payload remains a valid provider-independent scenario argument.</summary>
    [Fact]
    public void SharedForeignEnginePayloadArgumentIsAccepted()
    {
        // Arrange
        var method = CreateParameterProbe("foreignEngine", true);

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A non-test helper may still accept an engine to operate on an explicitly selected resource.</summary>
    [Fact]
    public void HelperEngineArgumentIsAccepted()
    {
        // Arrange
        var method = CreateParameterProbe("engine", false);

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Unsupported fact and data annotations cannot silently bypass fixture-owned shared coverage.</summary>
    /// <param name="data">Whether the unsupported annotation supplies data rather than a fact.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedUnsupportedAnnotationsAreRejected(
        bool data
    )
    {
        // Arrange
        var attributes = data
            ? new[] { NeutralTheory(), NeutralInline(["PostgreSql"]), UnsupportedData() }
            : new[] { UnsupportedFact() };

        var method = CreateProbe(attributes)
            .GetMethod("Probe0")!;

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("bypasses fixture-owned coverage", error, StringComparison.Ordinal);
        Assert.Contains(
            data ? nameof(UnsupportedDataAttribute) : nameof(UnsupportedFactAttribute),
            error,
            StringComparison.Ordinal);
    }

    /// <summary>Direct and inherited local annotations use the same raw bypass guard as shared contracts.</summary>
    /// <param name="data">Whether the unsupported annotation supplies data rather than a fact.</param>
    /// <param name="inherited">Whether the annotation is declared on the local abstract family body.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LocalUnsupportedAnnotationsAreRejected(
        bool data,
        bool inherited
    )
    {
        // Arrange
        var attributes = data
            ? new[] { NeutralTheory(), NeutralInline(["PostgreSql"]), UnsupportedData() }
            : new[] { UnsupportedFact() };

        var suite = CreateNeutralProbe(attributes, parameterName: data ? "payload" : null, inherited: inherited);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("bypasses fixture-owned coverage", error, StringComparison.Ordinal);
        Assert.Contains(
            data ? nameof(UnsupportedDataAttribute) : nameof(UnsupportedFactAttribute),
            error,
            StringComparison.Ordinal);
    }

    /// <summary>Every explicit shared engine exclusion has a known engine and a meaningful reason.</summary>
    [Theory]
    [InlineData("Sqlite", "This assertion requires a server execution plan.", false)]
    [InlineData("Postgres", "This assertion requires a server execution plan.", true)]
    [InlineData("Sqlite", "", true)]
    [InlineData("Sqlite", " ", true)]
    public void SharedExclusionsAreValidated(
        string engine,
        string reason,
        bool invalid
    )
    {
        // Arrange
        var attribute = new CustomAttributeBuilder(
            typeof(EngineFactAttribute).GetConstructor([typeof(string), typeof(int)])!,
            [string.Empty, -1],
            [
                typeof(EngineFactAttribute).GetProperty(nameof(EngineFactAttribute.ExcludedEngines))!,
                typeof(EngineFactAttribute).GetProperty(nameof(EngineFactAttribute.Reason))!
            ],
            [new[] { engine }, reason]);

        var method = CreateProbe([attribute])
            .GetMethod("Probe0")!;

        // Act
        var errors = ProviderTestContract
            .FindSharedAnnotationErrors(method)
            .ToArray();

        // Assert
        Assert.Equal(invalid, errors.Length > 0);
    }

    /// <summary>A conditional member variant cannot hide an unknown exclusion or a missing reason.</summary>
    [Theory]
    [InlineData(nameof(RawSources.ValidConditionalRows), false)]
    [InlineData(nameof(RawSources.UnknownConditionalRows), true)]
    [InlineData(nameof(RawSources.UnexplainedConditionalRows), true)]
    public async Task SharedMemberVariantExclusionsAreValidated(
        string memberName,
        bool invalid
    )
    {
        // Arrange
        var method = CreateProbe([SharedMember(memberName)]).GetMethod("Probe0")!;

        // Act
        var errors = await ProviderTestContract.FindSharedDataErrorsAsync(method);

        // Assert
        Assert.Equal(invalid, errors.Count > 0);
    }
}
