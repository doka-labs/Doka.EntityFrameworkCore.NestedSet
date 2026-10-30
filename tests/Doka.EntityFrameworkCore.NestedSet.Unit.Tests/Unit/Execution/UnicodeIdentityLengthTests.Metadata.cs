using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Storage;
using NpgsqlTypes;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Runtime metadata retains only primitive facts selected by the actual native adapter.</summary>
    /// <param name="engine">The database provider whose native mapping semantics are captured.</param>
    /// <param name="expected">Whether its bounded string identities use Unicode scalar counts.</param>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("Sqlite", false)]
    [InlineData("SqlServer", false)]
    public void RuntimeModelRetainsPrimitiveNativeIdentityPolicies(string engine, bool expected)
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var entity = context.Model.FindEntityType(UnicodeContext.Raw)!;

        // Act
        var mapping = NestedSetTreeRegistryMapping.For(entity);

        // Assert
        Assert.Equal(expected, Assert.IsType<bool>(entity.FindAnnotation(
            NestedSetAnnotationNames.ScopeUnicodeScalarLength)!.Value));
        Assert.Equal(expected, Assert.IsType<bool>(entity.FindAnnotation(
            NestedSetAnnotationNames.TreeIdUnicodeScalarLength)!.Value));
        Assert.Equal(expected, mapping.ScopeUsesUnicodeScalarLength);
        Assert.Equal(expected, mapping.TreeIdUsesUnicodeScalarLength);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Missing or malformed old model facts require regeneration rather than changing a length guard.</summary>
    /// <param name="scopeRole">Whether the missing or malformed fact belongs to Scope.</param>
    /// <param name="invalid">The invalid non-Boolean value, or null for an absent annotation.</param>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "true")]
    [InlineData(true, "false")]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void InvalidCapturedIdentityPolicyRequiresRegeneration(bool scopeRole, object? invalid)
    {
        // Arrange
        var entity = new ModelBuilder().Entity<UnicodePolicyMetadataProbe>().Metadata;
        var annotation = scopeRole
            ? NestedSetAnnotationNames.ScopeUnicodeScalarLength
            : NestedSetAnnotationNames.TreeIdUnicodeScalarLength;

        if (invalid is not null)
        {
            entity.SetAnnotation(annotation, invalid);
        }

        // Act
        var error = Record.Exception(() => NestedSetIdentityLength.CapturedPolicy(entity, annotation));

        // Assert
        var rejected = Assert.IsType<InvalidOperationException>(error);

        Assert.Contains("missing or invalid", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("Regenerate the model", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("matching native provider callback", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>Both Boolean decisions survive reading without a provider instance or service graph.</summary>
    /// <param name="scopeRole">Whether the fact belongs to Scope or TreeId.</param>
    /// <param name="expected">The exact captured Boolean, including a conservative false decision.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CapturedIdentityPolicyKeepsBothBooleanDecisions(bool scopeRole, bool expected)
    {
        // Arrange
        var entity = new ModelBuilder().Entity<UnicodePolicyMetadataProbe>().Metadata;
        var annotation = scopeRole
            ? NestedSetAnnotationNames.ScopeUnicodeScalarLength
            : NestedSetAnnotationNames.TreeIdUnicodeScalarLength;

        entity.SetAnnotation(annotation, expected);

        // Act
        var observed = NestedSetIdentityLength.CapturedPolicy(entity, annotation);

        // Assert
        Assert.Equal(expected, observed);
    }

    /// <summary>A namesake assembly cannot authorize native semantics using matching assembly and type names.</summary>
    /// <param name="engine">The actual configured owner whose concrete mapping identity is imitated.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    public void NamesakeProviderAssemblyCannotAuthorizeCharacterPolicy(string engine)
    {
        // Arrange
        using var context = new UnicodeContext(engine);
        var original = context.Model.FindEntityType(UnicodeContext.Raw)!.FindProperty("TreeId")!
            .GetRelationalTypeMapping();
        var foreign = CreateNamedCharacterMapping(original.GetType().Assembly.GetName().Name!,
            original.GetType().FullName!);

        // Act
        var authorized = NestedSetProviderCapabilities.Resolve(context).UsesUnicodeScalarLength(foreign);

        // Assert
        Assert.False(authorized);
        Assert.Equal(original.GetType().FullName, foreign.GetType().FullName);
        Assert.Equal(original.GetType().Assembly.GetName().Name, foreign.GetType().Assembly.GetName().Name);
        Assert.NotSame(original.GetType().Assembly, foreign.GetType().Assembly);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>A derived native mapping keeps the conservative policy rather than inheriting authorization.</summary>
    [Fact]
    public void NativeMappingSubclassDoesNotAuthorizeCharacterPolicy()
    {
        // Arrange
        using var context = new UnicodeContext("PostgreSql");
        var original = context.Model.FindEntityType(UnicodeContext.Raw)!.FindProperty("TreeId")!
            .GetRelationalTypeMapping();
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NestedSet.DerivedUnicodeProbe"),
            AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("MappingProbe")
            .DefineType("DerivedNativeCharacterMapping", TypeAttributes.Public | TypeAttributes.Sealed, original.GetType());
        var generator = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes)
            .GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldstr, "varchar(8)");
        generator.Emit(OpCodes.Ldc_I4, (int)NpgsqlDbType.Varchar);
        generator.Emit(OpCodes.Call, original.GetType().GetConstructor([typeof(string), typeof(NpgsqlDbType)])!);
        generator.Emit(OpCodes.Ret);
        var mapping = (RelationalTypeMapping)Activator.CreateInstance(type.CreateType()!)!;

        // Act
        var authorized = NestedSetProviderCapabilities.Resolve(context).UsesUnicodeScalarLength(mapping);

        // Assert
        Assert.False(authorized);
        Assert.Equal(original.GetType(), mapping.GetType().BaseType);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Builds an adversarial ordinary mapping with caller-selected owner and type names.</summary>
    private static RelationalTypeMapping CreateNamedCharacterMapping(string assemblyName, string typeName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("MappingProbe")
            .DefineType(typeName, TypeAttributes.Public | TypeAttributes.Sealed, typeof(StringTypeMapping));
        var generator = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes)
            .GetILGenerator();
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

        return (RelationalTypeMapping)Activator.CreateInstance(type.CreateType()!)!;
    }

    /// <summary>Supplies isolated mutable metadata for captured-policy compatibility checks.</summary>
    private sealed class UnicodePolicyMetadataProbe
    {
        /// <summary>Gets or sets the ordinary convention key.</summary>
        public int Id { get; set; }
    }
}
