using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Retains the concrete provider mapping when EF reconstructs its stored-size clone.</summary>
    /// <param name="engine">The provider whose runtime mapping is cloned.</param>
    /// <param name="fixedWidth">Whether the provider's fixed-width character mapping is exercised.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public void ConcreteRuntimeMappingCloneRetainsCharacterPolicy(
        string engine,
        bool fixedWidth
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(fixedWidth ? UnicodeContext.Fixed : UnicodeContext.Raw)!;
        var original = entityType.FindProperty("TreeId")!.GetRelationalTypeMapping();
        var clone = original.WithStoreTypeAndSize(original.StoreType, original.Size);

        // Act
        var scalarLength = NestedSetProviderCapabilities.Resolve(context).UsesUnicodeScalarLength(clone);

        // Assert
        Assert.True(scalarLength);
        Assert.Equal(original.GetType(), clone.GetType());
        Assert.True(NestedSetTreeRegistryMapping.For(entityType).TreeIdUsesUnicodeScalarLength);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Keeps unrelated store families conservative even when carried by a known mapping.</summary>
    /// <param name="engine">The provider supplying a real character mapping.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    public void KnownConcreteMappingDoesNotAuthorizeAnUnknownStoreFamily(
        string engine
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var original = context.Model.FindEntityType(UnicodeContext.Raw)!.FindProperty("TreeId")!
            .GetRelationalTypeMapping();

        var clone = original.WithStoreTypeAndSize("text", 8);

        // Act
        var scalarLength = NestedSetProviderCapabilities.Resolve(context).UsesUnicodeScalarLength(clone);

        // Assert
        Assert.False(scalarLength);
        Assert.Equal(original.GetType(), clone.GetType());
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Keeps custom mappings conservative even when they advertise the same varchar spelling.</summary>
    /// <param name="engine">The provider finalizing the custom source mapping.</param>
    /// <param name="scopeRole">Whether Scope carries the value that exceeds eight UTF-16 units.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public void CustomVarcharMappingRetainsConservativeGuard(
        string engine,
        bool scopeRole
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Custom)!;
        const string input = "\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604";

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<string, string>(
            entityType,
            scopeRole ? input : "scope",
            scopeRole ? "tree" : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
        Assert.IsType<StringTypeMapping>(entityType.FindProperty(scopeRole ? "Scope" : "TreeId")!
            .GetRelationalTypeMapping());
        Assert.False(NestedSetTreeRegistryMapping.For(entityType).ScopeUsesUnicodeScalarLength);
        Assert.False(NestedSetTreeRegistryMapping.For(entityType).TreeIdUsesUnicodeScalarLength);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Still accepts the exact conservative limit for a custom mapping without changing either role.</summary>
    /// <param name="engine">The provider finalizing the custom source mapping.</param>
    /// <param name="scopeRole">Whether Scope carries the eight-code-unit boundary value.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public void CustomVarcharMappingAcceptsExactConservativeBoundary(
        string engine,
        bool scopeRole
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Custom)!;
        const string input = "\U0001f600\U0001f601\U0001f602\U0001f603";

        // Act
        var request = new NestedSetTreeLockRequest<string, string>(
            entityType,
            scopeRole ? input : "scope",
            scopeRole ? "tree" : input,
            NestedSetTreeLockMode.Existing);

        // Assert
        Assert.Same(input, scopeRole ? request.Scope : request.TreeId);
        Assert.False(request.Mapping.ScopeUsesUnicodeScalarLength);
        Assert.False(request.Mapping.TreeIdUsesUnicodeScalarLength);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Does not trust a third-party mapping that happens to reuse a provider's full CLR type name.</summary>
    [Fact]
    public void ProviderTypeNameFromAnotherAssemblyDoesNotAuthorizeCharacterPolicy()
    {
        // Arrange
        using var context = new UnicodeContext("MySql");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("NestedSet.UnicodeMappingProbe"),
            AssemblyBuilderAccess.RunAndCollect);

        var module = assembly.DefineDynamicModule("MappingProbe");
        var type = module.DefineType(
            "Doka.EntityFrameworkCore.MySql.MySqlStringTypeMapping",
            TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(StringTypeMapping));

        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var generator = constructor.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldstr, "varchar(8)");
        generator.Emit(OpCodes.Ldc_I4, (int)DbType.String);
        generator.Emit(OpCodes.Newobj, typeof(DbType?).GetConstructor([typeof(DbType)])!);
        generator.Emit(OpCodes.Ldc_I4_1);
        generator.Emit(OpCodes.Ldc_I4_8);
        generator.Emit(OpCodes.Newobj, typeof(int?).GetConstructor([typeof(int)])!);
        generator.Emit(OpCodes.Call, typeof(StringTypeMapping).GetConstructor(
            [typeof(string), typeof(DbType?), typeof(bool), typeof(int?)])!);
        generator.Emit(OpCodes.Ret);
        var mapping = (RelationalTypeMapping)Activator.CreateInstance(type.CreateType()!)!;

        // Act
        var scalarLength = NestedSetProviderCapabilities.Resolve(context).UsesUnicodeScalarLength(mapping);

        // Assert
        Assert.False(scalarLength);
        Assert.Equal("Doka.EntityFrameworkCore.MySql.MySqlStringTypeMapping", mapping.GetType().FullName);
        Assert.Equal("NestedSet.UnicodeMappingProbe", mapping.GetType().Assembly.GetName().Name);
    }

    /// <summary>Counts fixed-width supplementary characters without changing their contents.</summary>
    /// <param name="engine">The provider whose char mapping is finalized.</param>
    /// <param name="scopeRole">Whether Scope rather than TreeId carries the fixed-width value.</param>
    /// <param name="overflow">Whether one scalar exceeds the configured fixed width.</param>
    [Theory]
    [InlineData("MySql", false, false)]
    [InlineData("MySql", false, true)]
    [InlineData("MySql", true, false)]
    [InlineData("MySql", true, true)]
    [InlineData("MariaDb", false, false)]
    [InlineData("MariaDb", true, true)]
    [InlineData("PostgreSql", false, false)]
    [InlineData("PostgreSql", false, true)]
    [InlineData("PostgreSql", true, false)]
    [InlineData("PostgreSql", true, true)]
    public void FixedWidthCharacterMappingUsesScalarBoundary(
        string engine,
        bool scopeRole,
        bool overflow
    )
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entityType = context.Model.FindEntityType(UnicodeContext.Fixed)!;
        var input = "\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606\U0001f607"
            + (overflow ? "x" : string.Empty);

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
}
