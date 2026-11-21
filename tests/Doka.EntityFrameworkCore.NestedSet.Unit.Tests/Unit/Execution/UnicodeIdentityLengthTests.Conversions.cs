namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Checks the converted stored string, including the converter-owned prefix.</summary>
    /// <param name="engine">The character-count provider.</param>
    /// <param name="scopeRole">Whether Scope carries the converted value.</param>
    /// <param name="overflow">Whether the converter's prefix takes the stored value past eight scalars.</param>
    [Theory]
    [InlineData("MySql", false, false)]
    [InlineData("MySql", false, true)]
    [InlineData("MySql", true, false)]
    [InlineData("MySql", true, true)]
    [InlineData("MariaDb", false, false)]
    [InlineData("MariaDb", false, true)]
    [InlineData("MariaDb", true, false)]
    [InlineData("MariaDb", true, true)]
    [InlineData("PostgreSql", false, false)]
    [InlineData("PostgreSql", false, true)]
    [InlineData("PostgreSql", true, false)]
    [InlineData("PostgreSql", true, true)]
    public void Construct_ConvertedString_ChecksActualProviderRepresentation(
        string engine,
        bool scopeRole,
        bool overflow
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Converted)!;
        var input = new ConvertedIdentity(
            "\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606"
            + (overflow
                ? "x"
                : string.Empty));

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<ConvertedIdentity, ConvertedIdentity>(
            entityType,
            scopeRole
                ? input
                : new ConvertedIdentity("scope"),
            scopeRole
                ? new ConvertedIdentity("tree")
                : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        if (overflow)
        {
            Assert.Equal(
                scopeRole
                    ? "Scope"
                    : "TreeId",
                Assert.IsType<ArgumentException>(error)
                    .ParamName);
        }
        else
        {
            Assert.Null(error);
        }

        AssertNoDatabaseAccess(context);
    }

    /// <summary>Retains binary byte limits even when the application identity itself is a Unicode string.</summary>
    /// <param name="engine">The provider finalizing the binary mapping.</param>
    /// <param name="scopeRole">Whether Scope carries the converted binary identity.</param>
    /// <param name="overflow">Whether UTF-8 conversion produces nine rather than eight bytes.</param>
    [Theory]
    [MemberData(nameof(BinaryBoundaries))]
    public void Construct_ConvertedBinary_RetainsByteBoundary(
        string engine,
        bool scopeRole,
        bool overflow
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Binary)!;
        var input = "\U0001f600\U0001f601"
            + (overflow
                ? "x"
                : string.Empty);

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<string, string>(
            entityType,
            scopeRole
                ? input
                : "scope",
            scopeRole
                ? "tree"
                : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        if (overflow)
        {
            Assert.Equal(
                scopeRole
                    ? "Scope"
                    : "TreeId",
                Assert.IsType<ArgumentException>(error)
                    .ParamName);
        }
        else
        {
            Assert.Null(error);
        }

        AssertNoDatabaseAccess(context);
    }

    /// <summary>Supplies both binary identity roles and both byte boundaries on each supported provider.</summary>
    public static TheoryData<string, bool, bool> BinaryBoundaries()
    {
        var data = new TheoryData<string, bool, bool>();

        foreach (var engine in new[]
            {
                "MySql",
                "MariaDb",
                "PostgreSql",
                "SqlServer",
                "Sqlite",
            })
        {
            foreach (var scope in new[] { false, true })
            {
                data.Add(engine, scope, false);
                data.Add(engine, scope, true);
            }
        }

        return data;
    }

    /// <summary>Rejects malformed UTF-16 after conversion to the provider string.</summary>
    /// <param name="engine">The provider supplying the known character mapping.</param>
    /// <param name="scopeRole">Whether Scope contains the malformed converted identity.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public void Construct_ConvertedString_RejectsMalformedProviderRepresentation(
        string engine,
        bool scopeRole
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Converted)!;
        var input = new ConvertedIdentity("a\ud800");

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<ConvertedIdentity, ConvertedIdentity>(
            entityType,
            scopeRole
                ? input
                : new ConvertedIdentity("scope"),
            scopeRole
                ? new ConvertedIdentity("tree")
                : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        Assert.Equal(
            scopeRole
                ? "Scope"
                : "TreeId",
            Assert.IsType<ArgumentException>(error)
                .ParamName);

        AssertNoDatabaseAccess(context);
    }
}
