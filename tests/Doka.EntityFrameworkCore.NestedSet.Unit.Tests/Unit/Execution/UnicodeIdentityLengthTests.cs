namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies mapped identity lengths without creating a connection or changing identity contents.</summary>
public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Accepts the native character boundary and rejects the first scalar beyond it for either role.</summary>
    /// <param name="engine">The provider whose character mapping owns the length semantics.</param>
    /// <param name="scopeRole">Whether Scope rather than TreeId carries the tested value.</param>
    /// <param name="value">Exactly eight Unicode scalar values, including independent combining marks.</param>
    /// <param name="overflow">Whether one more scalar is appended.</param>
    [Theory]
    [MemberData(nameof(CharacterBoundaries))]
    public void KnownCharacterMappingUsesScalarBoundaryBeforeDatabaseAccess(
        string engine,
        bool scopeRole,
        string value,
        bool overflow
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Raw)!;
        var input = overflow ? value + "x" : value;
        NestedSetTreeLockRequest<string, string>? request = null;

        // Act
        var error = Record.Exception(() => request = new NestedSetTreeLockRequest<string, string>(
            entityType,
            scopeRole ? input : "scope",
            scopeRole ? "tree" : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        if (overflow)
        {
            var argument = Assert.IsType<ArgumentException>(error);
            Assert.Equal(scopeRole ? "Scope" : "TreeId", argument.ParamName);
            Assert.StartsWith($"{argument.ParamName} exceeds the mapped maximum length of 8.", argument.Message);
        }
        else
        {
            Assert.Null(error);
            Assert.NotNull(request);
            Assert.Same(input, scopeRole ? request.Scope : request.TreeId);
        }

        AssertNoDatabaseAccess(context);
    }

    /// <summary>Supplies both identity roles and character boundaries on each known provider.</summary>
    public static TheoryData<string, bool, string, bool> CharacterBoundaries()
    {
        var data = new TheoryData<string, bool, string, bool>();
        string[] values =
        [
            "abcdefgh",
            "\u00e4\u03a9\u4e2d\u00f1\u0416\u00e9\u3042\u20ac",
            "\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606\U0001f607",
            "ab\U0001f600c\U0001f601def",
            "e\u0301e\u0301e\u0301e\u0301",
        ];

        foreach (var engine in new[] { "MySql", "MariaDb", "PostgreSql" })
        {
            foreach (var scope in new[] { false, true })
            {
                foreach (var value in values)
                {
                    data.Add(engine, scope, value, false);
                    data.Add(engine, scope, value, true);
                }
            }
        }

        return data;
    }

    /// <summary>Rejects malformed UTF-16 before replacement characters can alias stored identities.</summary>
    /// <param name="engine">The provider whose known character mapping requires scalar validation.</param>
    /// <param name="scopeRole">Whether the malformed value belongs to Scope.</param>
    /// <param name="shape">The unpaired or incorrectly ordered surrogate sequence to construct locally.</param>
    [Theory]
    [MemberData(nameof(MalformedCharacters))]
    public void KnownCharacterMappingRejectsMalformedSurrogatesBeforeDatabaseAccess(
        string engine,
        bool scopeRole,
        string shape
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Raw)!;
        var value = shape switch
        {
            "high" => "\ud800",
            "low" => "\udc00",
            "high-ascii" => "\ud800a",
            "high-high" => "\ud800\ud800",
            "low-high" => "\udc00\ud800",
            "ascii-high" => "a\ud800",
            "valid-high" => "\U0001f600\ud800",
            "valid-low" => "\U0001f600\udc00",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<string, string>(
            entityType,
            scopeRole ? value : "scope",
            scopeRole ? "tree" : value,
            NestedSetTreeLockMode.Existing));

        // Assert
        Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Supplies ASCII labels so discovery never needs to transport lone surrogates.</summary>
    public static TheoryData<string, bool, string> MalformedCharacters()
    {
        var data = new TheoryData<string, bool, string>();

        foreach (var engine in new[] { "MySql", "MariaDb", "PostgreSql" })
        {
            foreach (var scope in new[] { false, true })
            {
                foreach (var shape in new[]
                         {
                             "high", "low", "high-ascii", "high-high",
                             "low-high", "ascii-high", "valid-high", "valid-low",
                         })
                {
                    data.Add(engine, scope, shape);
                }
            }
        }

        return data;
    }

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
    public void ConvertedStringChecksActualProviderRepresentation(
        string engine,
        bool scopeRole,
        bool overflow
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Converted)!;
        var input = new ConvertedIdentity("\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606"
            + (overflow ? "x" : string.Empty));

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<ConvertedIdentity, ConvertedIdentity>(
            entityType,
            scopeRole ? input : new ConvertedIdentity("scope"),
            scopeRole ? new ConvertedIdentity("tree") : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        if (overflow)
        {
            Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
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
    public void ConvertedBinaryRetainsByteBoundary(
        string engine,
        bool scopeRole,
        bool overflow
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Binary)!;
        var input = "\U0001f600\U0001f601" + (overflow ? "x" : string.Empty);

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<string, string>(
            entityType,
            scopeRole ? input : "scope",
            scopeRole ? "tree" : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        if (overflow)
        {
            Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
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

        foreach (var engine in new[] { "MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite" })
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
    public void ConvertedStringRejectsMalformedProviderRepresentation(
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
            scopeRole ? input : new ConvertedIdentity("scope"),
            scopeRole ? new ConvertedIdentity("tree") : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Preserves the existing UTF-16 boundary on other providers.</summary>
    /// <param name="engine">The conservative provider.</param>
    /// <param name="scopeRole">Whether Scope carries the boundary value.</param>
    /// <param name="overflow">Whether five supplementary scalars exceed eight UTF-16 code units.</param>
    [Theory]
    [InlineData("SqlServer", false, false)]
    [InlineData("SqlServer", false, true)]
    [InlineData("SqlServer", true, false)]
    [InlineData("SqlServer", true, true)]
    [InlineData("Sqlite", false, false)]
    [InlineData("Sqlite", false, true)]
    [InlineData("Sqlite", true, false)]
    [InlineData("Sqlite", true, true)]
    public void OtherProvidersRetainUtf16Boundary(
        string engine,
        bool scopeRole,
        bool overflow
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Raw)!;
        var input = "\U0001f600\U0001f601\U0001f602\U0001f603" + (overflow ? "\U0001f604" : string.Empty);

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<string, string>(
            entityType,
            scopeRole ? input : "scope",
            scopeRole ? "tree" : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        if (overflow)
        {
            Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
        }
        else
        {
            Assert.Null(error);
        }

        AssertNoDatabaseAccess(context);
    }

    /// <summary>Allows a supplementary TreeId without inventing or validating a persisted Scope.</summary>
    /// <param name="engine">The known character-count provider.</param>
    /// <param name="overflow">Whether one scalar exceeds the mapped TreeId boundary.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public void ScopelessCharacterMappingRetainsExactTreeIdentity(
        string engine,
        bool overflow
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Scopeless)!;
        var input = "\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606\U0001f607"
            + (overflow ? "x" : string.Empty);

        NestedSetTreeLockRequest<string, NestedSetNoScope>? request = null;

        // Act
        var error = Record.Exception(() => request = new NestedSetTreeLockRequest<string, NestedSetNoScope>(
            entityType,
            default,
            input,
            NestedSetTreeLockMode.New));

        // Assert
        if (overflow)
        {
            Assert.Equal("TreeId", Assert.IsType<ArgumentException>(error).ParamName);
            Assert.Null(request);
        }
        else
        {
            Assert.Null(error);
            Assert.NotNull(request);
            Assert.Same(input, request.TreeId);
            Assert.Null(((INestedSetTreeLockRequest)request).ScopeValue);
            Assert.Null(request.Mapping.SourceScope);
        }

        AssertNoDatabaseAccess(context);
    }

    /// <summary>Rejects a malformed scopeless TreeId before a registry reservation can issue a command.</summary>
    /// <param name="engine">The known character-count provider.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    public void ScopelessCharacterMappingRejectsMalformedTreeIdentity(
        string engine
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Scopeless)!;

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<string, NestedSetNoScope>(
            entityType,
            default,
            "a\ud800",
            NestedSetTreeLockMode.New));

        // Assert
        Assert.Equal("TreeId", Assert.IsType<ArgumentException>(error).ParamName);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Proves that model-only validation never opened or changed the database.</summary>
    private static void AssertNoDatabaseAccess(
        DbContext context
    )
    {
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
