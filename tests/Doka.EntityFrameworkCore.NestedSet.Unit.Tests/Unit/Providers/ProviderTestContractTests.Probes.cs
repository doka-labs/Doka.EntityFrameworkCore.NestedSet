using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderTestContractTests
{
    /// <summary>Builds a concrete provider leaf without registering invalid metadata in Unit discovery.</summary>
    private static Type CreateNeutralProbe(
        CustomAttributeBuilder[] attributes,
        string project = "Sqlite",
        Type[]? markers = null,
        string? parameterName = null,
        bool inherited = false,
        bool registerFixture = true,
        bool localRows = false,
        bool emptyRows = false
    )
    {
        var name = new AssemblyName(AssemblyName(project));
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name.Name!);
        var baseConstructor = typeof(ProviderTest).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(IProviderFixture)],
            null)!;

        var declaration = module.DefineType(
            "LocalProviderBody",
            TypeAttributes.Public | TypeAttributes.Abstract,
            typeof(ProviderTest));

        var bodyConstructor = declaration.DefineConstructor(
            MethodAttributes.Family,
            CallingConventions.Standard,
            [typeof(IProviderFixture)]);

        var bodyIl = bodyConstructor.GetILGenerator();
        bodyIl.Emit(OpCodes.Ldarg_0);
        bodyIl.Emit(OpCodes.Ldarg_1);
        bodyIl.Emit(OpCodes.Call, baseConstructor);
        bodyIl.Emit(OpCodes.Ret);

        var methodParameters = parameterName is null ? Type.EmptyTypes : [typeof(string)];

        // WHY: Reflection can expose both hidden nonvirtual methods; each probe must declare only its selected shape.
        if (inherited)
        {
            var method = declaration.DefineMethod(
                "Probe",
                MethodAttributes.Public | MethodAttributes.HideBySig,
                typeof(void),
                methodParameters);

            if (parameterName is not null)
            {
                method.DefineParameter(1, ParameterAttributes.None, parameterName);
            }

            foreach (var attribute in attributes)
            {
                method.SetCustomAttribute(attribute);
            }

            method
                .GetILGenerator()
                .Emit(OpCodes.Ret);
        }

        if (localRows)
        {
            var rows = declaration.DefineMethod(
                "Rows",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(IEnumerable<object?[]>),
                Type.EmptyTypes);

            rows
                .GetILGenerator()
                .Emit(
                    OpCodes.Call,
                    typeof(RawSources).GetProperty(
                        emptyRows ? nameof(RawSources.EmptyRows) : nameof(RawSources.PayloadRows))!.GetMethod!);

            rows
                .GetILGenerator()
                .Emit(OpCodes.Ret);
        }

        var body = declaration.CreateType();
        var leaf = module.DefineType("ConcreteProviderLeaf", TypeAttributes.Public | TypeAttributes.Sealed, body);
        var fixtures = (markers ?? [typeof(SqliteEngine)])
            .Select(marker => typeof(ProviderFixture<,>).MakeGenericType(typeof(ProviderResources), marker))
            .ToArray();

        var constructor = leaf.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, fixtures);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(fixtures.Length == 0 ? OpCodes.Ldnull : OpCodes.Ldarg_1);
        il.Emit(
            OpCodes.Call,
            body.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [typeof(IProviderFixture)],
                null)!);

        il.Emit(OpCodes.Ret);

        if (registerFixture)
        {
            foreach (var fixture in fixtures)
            {
                leaf.AddInterfaceImplementation(typeof(IClassFixture<>).MakeGenericType(fixture));
            }
        }

        if (!inherited)
        {
            var direct = leaf.DefineMethod(
                "Probe",
                MethodAttributes.Public | MethodAttributes.HideBySig,
                typeof(void),
                methodParameters);

            if (parameterName is not null)
            {
                direct.DefineParameter(1, ParameterAttributes.None, parameterName);
            }

            foreach (var attribute in attributes)
            {
                direct.SetCustomAttribute(attribute);
            }

            direct
                .GetILGenerator()
                .Emit(OpCodes.Ret);
        }

        return leaf.CreateType();
    }

    /// <summary>Encodes an ordinary or explicitly excluded fixture-owned fact.</summary>
    private static CustomAttributeBuilder NeutralFact(
        string[]? excluded = null
    ) => NeutralMarker(excluded is null ? typeof(FactAttribute) : typeof(EngineFactAttribute), excluded);

    /// <summary>Encodes an ordinary or explicitly excluded fixture-owned theory.</summary>
    private static CustomAttributeBuilder NeutralTheory(
        string[]? excluded = null
    ) => NeutralMarker(excluded is null ? typeof(TheoryAttribute) : typeof(EngineTheoryAttribute), excluded);

    /// <summary>Supplies a precise rationale for conditional marker metadata in coverage-control tests.</summary>
    private static CustomAttributeBuilder NeutralMarker(
        Type type,
        string[]? excluded
    )
    {
        var constructor = type.GetConstructor([typeof(string), typeof(int)])!;

        if (excluded is null)
        {
            return new CustomAttributeBuilder(constructor, [string.Empty, -1]);
        }

        return new CustomAttributeBuilder(
            constructor,
            [string.Empty, -1],
            [
                type.GetProperty(nameof(EngineFactAttribute.ExcludedEngines))!,
                type.GetProperty(nameof(EngineFactAttribute.Reason))!,
            ],
            [excluded, "The probe requires this explicitly selected backend capability."]);
    }

    /// <summary>Encodes neutral scenario data whose strings never select the fixture engine.</summary>
    private static CustomAttributeBuilder NeutralInline(
        object?[] data,
        string[]? excluded = null
    )
    {
        var type = excluded is null ? typeof(InlineDataAttribute) : typeof(EngineInlineDataAttribute);
        var constructor = type.GetConstructor([typeof(object[])])!;

        if (excluded is null)
        {
            return new CustomAttributeBuilder(constructor, [data]);
        }

        return new CustomAttributeBuilder(
            constructor,
            [data],
            [
                type.GetProperty(nameof(EngineInlineDataAttribute.ExcludedEngines))!,
                type.GetProperty(nameof(EngineInlineDataAttribute.Reason))!,
            ],
            [excluded, "The probe requires this explicitly selected backend capability."]);
    }

    /// <summary>Preserves an external MemberType or tests xUnit's default reflected-leaf lookup.</summary>
    private static CustomAttributeBuilder NeutralMember(
        string memberName,
        bool explicitSource = true
    )
    {
        var constructor = typeof(MemberDataAttribute).GetConstructor([typeof(string), typeof(object[])])!;

        if (!explicitSource)
        {
            return new CustomAttributeBuilder(constructor, [memberName, Array.Empty<object>()]);
        }

        return new CustomAttributeBuilder(
            constructor,
            [memberName, Array.Empty<object>()],
            [typeof(MemberDataAttribute).GetProperty(nameof(MemberDataAttribute.MemberType))!],
            [typeof(RawSources)]);
    }

    /// <summary>
    /// Builds raw method metadata without registering deliberately invalid tests in the Unit executable.
    /// </summary>
    private static Type CreateProbe(
        params CustomAttributeBuilder[][] methods
    )
    {
        // WHY: Real reflection metadata tests the audit's encoding independently of xUnit's discovery filters.
        var name = new AssemblyName("LocalOwnershipProbe");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name.Name!);
        var type = module.DefineType("LocalOwnershipProbe", TypeAttributes.Public);

        for (var index = 0; index < methods.Length; index++)
        {
            var method = type.DefineMethod(
                $"Probe{index}",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);

            foreach (var attribute in methods[index])
            {
                method.SetCustomAttribute(attribute);
            }

            method
                .GetILGenerator()
                .Emit(OpCodes.Ret);
        }

        return type.CreateType();
    }

    /// <summary>Defines a real named string parameter with ordinary or absent test annotations.</summary>
    private static MethodInfo CreateParameterProbe(
        string parameterName,
        bool isTest
    )
    {
        var name = new AssemblyName("ParameterOwnershipProbe");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name.Name!);
        var type = module.DefineType("ParameterOwnershipProbe", TypeAttributes.Public);
        var method = type.DefineMethod(
            "Probe",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(string)]);

        method.DefineParameter(1, ParameterAttributes.None, parameterName);

        if (isTest)
        {
            method.SetCustomAttribute(NeutralTheory());
            method.SetCustomAttribute(NeutralInline(["Sqlite"]));
        }

        method
            .GetILGenerator()
            .Emit(OpCodes.Ret);

        return type
            .CreateType()
            .GetMethod("Probe")!;
    }

    /// <summary>Encodes a shared member source carrying engine-neutral conditional rows.</summary>
    private static CustomAttributeBuilder SharedMember(
        string memberName,
        string[]? excluded = null
    ) => new(
        typeof(EngineMemberDataAttribute).GetConstructor([typeof(string), typeof(object[])])!,
        [memberName, Array.Empty<object>()],
        [
            typeof(EngineMemberDataAttribute).GetProperty(nameof(EngineMemberDataAttribute.MemberType))!,
            typeof(EngineMemberDataAttribute).GetProperty(nameof(EngineMemberDataAttribute.ExcludedEngines))!,
            typeof(EngineMemberDataAttribute).GetProperty(nameof(EngineMemberDataAttribute.Reason))!,
        ],
        [typeof(RawSources), excluded ?? [], "The probe requires this explicitly selected backend capability."]);

    /// <summary>Gets the real provider executable identity without loading a provider assembly.</summary>
    private static string AssemblyName(
        string project
    ) => $"Doka.EntityFrameworkCore.NestedSet.{project}.Tests";

    /// <summary>Supplies complete source rows for the raw-ownership and shared-exclusion probes.</summary>
    public static class RawSources
    {
        /// <summary>Supplies no rows to verify that empty provider declarations remain errors.</summary>
        public static IEnumerable<object?[]> EmptyRows => [];

        /// <summary>Supplies a foreign-looking first value that remains ordinary scenario payload.</summary>
        public static IEnumerable<object?[]> PayloadRows => [["PostgreSql"]];

        /// <summary>Supplies a variant restricted to both engines in one Doka executable.</summary>
        public static IEnumerable<ITheoryDataRow> FamilyConditionalRows =>
        [
            new EngineTheoryDataRow(
                [17],
                ["PostgreSql", "SqlServer", "Sqlite"],
                "The probe requires a MySQL-family backend capability.")
        ];

        /// <summary>Supplies one explained, valid conditional shared row.</summary>
        public static IEnumerable<ITheoryDataRow> ValidConditionalRows =>
        [
            new EngineTheoryDataRow([17], ["Sqlite"], "The assertion requires a server execution plan.")
        ];

        /// <summary>Supplies a conditional row with an unknown excluded engine.</summary>
        public static IEnumerable<ITheoryDataRow> UnknownConditionalRows =>
        [
            new EngineTheoryDataRow([17], ["Postgres"], "The assertion requires a server execution plan.")
        ];

        /// <summary>Supplies an unexplained exclusion to exercise the nonempty reason requirement.</summary>
        public static IEnumerable<ITheoryDataRow> UnexplainedConditionalRows =>
        [
            new EngineTheoryDataRow([17], ["Sqlite"], ""),
        ];
    }

    /// <summary>Encodes an unsupported fact without registering invalid Unit test metadata.</summary>
    private static CustomAttributeBuilder UnsupportedFact() => new(
        typeof(UnsupportedFactAttribute).GetConstructor([typeof(string), typeof(int)])!,
        [string.Empty, -1]);

    /// <summary>Encodes an unsupported data source alongside a valid neutral scenario row.</summary>
    private static CustomAttributeBuilder UnsupportedData() => new(
        typeof(UnsupportedDataAttribute).GetConstructor(Type.EmptyTypes)!,
        []);

    /// <summary>Provides a custom fact marker which the raw fixture-ownership guard must reject.</summary>
    public sealed class UnsupportedFactAttribute : FactAttribute
    {
        /// <summary>Allows reflected metadata to retain the unsupported concrete marker type.</summary>
        /// <param name="sourceFilePath">The compiler-supplied source path.</param>
        /// <param name="sourceLineNumber">The compiler-supplied source line.</param>
        public UnsupportedFactAttribute(
            [CallerFilePath] string? sourceFilePath = null,
            [CallerLineNumber] int sourceLineNumber = -1
        ) : base(sourceFilePath, sourceLineNumber) { }
    }

    /// <summary>Provides custom data which the guard must reject instead of interpreting as engine ownership.</summary>
    public sealed class UnsupportedDataAttribute : DataAttribute
    {
        /// <summary>Allows reflected metadata to retain the unsupported concrete data type.</summary>
        public UnsupportedDataAttribute() { }

        /// <inheritdoc />
        public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(
            MethodInfo testMethod,
            DisposalTracker disposalTracker
        ) => ValueTask.FromResult<IReadOnlyCollection<ITheoryDataRow>>([new TheoryDataRow("PostgreSql")]);

        /// <inheritdoc />
        public override bool SupportsDiscoveryEnumeration() => true;
    }
}
