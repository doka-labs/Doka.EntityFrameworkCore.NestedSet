using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that raw metadata preserves fixture ownership and effective shared engine coverage.</summary>
public sealed class ProviderTestContractTests
{
    /// <summary>The local audit leaves shared-base declarations to the separate shared coverage guard.</summary>
    [Fact]
    public async Task InheritedSharedRowsAreNotAuditedAsLocalDeclarations()
    {
        // Arrange
        var name = new AssemblyName("InheritedOwnershipProbe");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name.Name!);
        var builder = module.DefineType(
            "InheritedOwnershipProbe",
            TypeAttributes.Public,
            typeof(ProviderFixtureIsolationTests.FixtureContextProbe));

        var suite = builder.CreateType();

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("PostgreSql"));

        // Assert
        Assert.Contains(
            suite.GetMethods(),
            method => method.DeclaringType == typeof(ProviderFixtureIsolationTests.FixtureContextProbe));

        Assert.Empty(errors);
    }

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

    /// <summary>MySQL and MariaDB share one executable while the remaining projects each own one engine.</summary>
    [Theory]
    [InlineData("MySql", 2)]
    [InlineData("PostgreSql", 1)]
    [InlineData("SqlServer", 1)]
    [InlineData("Sqlite", 1)]
    public void ExecutableOwnershipIncludesEveryEngine(
        string project,
        int count
    )
    {
        // Arrange
        var assemblyName = AssemblyName(project);

        // Act
        var engines = ProviderTestContract.OwnedEngines(assemblyName);

        // Assert
        Assert.Equal(count, engines.Length);
        Assert.Contains(project, engines);
    }

    /// <summary>Ordinary local facts use the leaf's exact fixture rather than explicit provider annotations.</summary>
    [Fact]
    public async Task OrdinaryLocalFactUsesConcreteFixture()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Both Doka-family markers are accepted in the same provider executable.</summary>
    [Theory]
    [InlineData(typeof(MySqlEngine))]
    [InlineData(typeof(MariaDbEngine))]
    public async Task OrdinaryFamilyFactUsesItsLeafMarker(
        Type marker
    )
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()], project: "MySql", markers: [marker], inherited: true);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("MySql"));

        // Assert
        Assert.Empty(errors);
        Assert.True(ProviderTestContract.HasLocalTestCases(suite));
        Assert.False(ProviderTestContract.HasDeclaredTestCases(suite));
    }

    /// <summary>Provider-looking ordinary payloads are scenario data and cannot select a database.</summary>
    [Theory]
    [InlineData("provider")]
    [InlineData("foreignEngine")]
    public async Task OrdinaryLocalTheoryAllowsEngineLookingPayload(
        string parameterName
    )
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralTheory(), NeutralInline(["PostgreSql"])], parameterName: parameterName);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>An ordinary local theory cannot accept a selector that contradicts fixture ownership.</summary>
    [Fact]
    public async Task OrdinaryLocalEngineSelectorIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralTheory(), NeutralInline(["Sqlite"])], parameterName: "engine");

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("not an engine argument", StringComparison.Ordinal));
    }

    /// <summary>Raw local member data preserves an explicitly configured external source type.</summary>
    [Fact]
    public async Task OrdinaryLocalExternalMemberSourceIsPreserved()
    {
        // Arrange
        var suite = CreateNeutralProbe(
            [NeutralTheory(), NeutralMember(nameof(RawSources.PayloadRows))],
            parameterName: "payload");

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Unset MemberType resolves through the concrete leaf to its local base's public factory.</summary>
    [Fact]
    public async Task OrdinaryInheritedMemberSourceUsesReflectedLeaf()
    {
        // Arrange
        var suite = CreateNeutralProbe(
            [NeutralTheory(), NeutralMember("Rows", explicitSource: false)],
            parameterName: "payload",
            inherited: true,
            localRows: true);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Empty(errors);
        Assert.Equal(suite, suite.GetMethod("Probe")!.ReflectedType);
    }

    /// <summary>An empty ordinary factory inherited from a local base remains a visible failure.</summary>
    [Fact]
    public async Task OrdinaryInheritedEmptyMemberSourceIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe(
            [NeutralTheory(), NeutralMember("Rows", explicitSource: false)],
            parameterName: "payload",
            inherited: true,
            localRows: true,
            emptyRows: true);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("is empty", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("no raw data rows", StringComparison.Ordinal));
    }

    /// <summary>Invalid metadata inherited from a local family base cannot evade the concrete-leaf audit.</summary>
    [Fact]
    public async Task OrdinaryInheritedUnknownExclusionIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact(["Postgres"])], inherited: true);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("unknown excluded provider 'Postgres'", StringComparison.Ordinal));
    }

    /// <summary>Fixture ownership is checked even when method exclusions would hide the local declaration.</summary>
    [Fact]
    public async Task ExcludedLocalMethodCannotHideForeignFixture()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact(["SqlServer"])], markers: [typeof(SqlServerEngine)]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("foreign provider project", StringComparison.Ordinal));
    }

    /// <summary>The owning executable passed to the guard cannot disagree with the actual concrete leaf.</summary>
    [Fact]
    public async Task OrdinaryLocalFixtureRejectsForeignAuditOwner()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("PostgreSql"));

        // Assert
        Assert.Contains(errors, error => error.Contains("provider 'Sqlite' is foreign", StringComparison.Ordinal));
    }

    /// <summary>An ordinary local suite without a concrete marker cannot infer an owner from its payload.</summary>
    [Fact]
    public async Task OrdinaryLocalMissingFixtureIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()], markers: []);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("must inject a concrete provider fixture", StringComparison.Ordinal));
    }

    /// <summary>Exact closed fixture markers may not contradict one another in a single concrete leaf.</summary>
    [Fact]
    public async Task OrdinaryLocalContradictoryFixturesAreRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe(
            [NeutralFact()],
            project: "MySql",
            markers: [typeof(MySqlEngine), typeof(MariaDbEngine)]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("MySql"));

        // Assert
        Assert.Contains(errors, error => error.Contains("contradictory provider fixtures", StringComparison.Ordinal));
    }

    /// <summary>An injected fixture is insufficient when xUnit cannot resolve its exact registration.</summary>
    [Fact]
    public async Task OrdinaryLocalUnregisteredFixtureIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()], registerFixture: false);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("unresolved test constructor argument", StringComparison.Ordinal));
    }

    /// <summary>Ordinary local declarations must expose their immutable fixture engine through ProviderTest.</summary>
    [Fact]
    public async Task OrdinaryLocalMissingProviderBaseIsRejected()
    {
        // Arrange
        var suite = CreateProbe([NeutralFact()]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("must derive from ProviderTest", StringComparison.Ordinal));
    }

    /// <summary>A local ordinary theory without any raw rows remains a test failure.</summary>
    [Fact]
    public async Task OrdinaryLocalMissingRowsAreRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralTheory()]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("no raw data rows", StringComparison.Ordinal));
    }

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
