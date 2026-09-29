using Xunit.Runner.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies fixture-owned shared discovery, explicit exclusions, and normal xUnit enumeration.</summary>
public sealed class EngineTestDiscoveryTests
{
    /// <summary>Every ordinary shared variant is inherited by all five engines without provider opt-in.</summary>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MySql", false)]
    [InlineData("MariaDb", true)]
    [InlineData("MariaDb", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("SqlServer", true)]
    [InlineData("SqlServer", false)]
    [InlineData("Sqlite", true)]
    [InlineData("Sqlite", false)]
    public async Task OrdinaryVariantsAutomaticallyCoverEveryEngine(
        string engine,
        bool preEnumerate
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.Ordinary));
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new TheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Equal(preEnumerate ? 2 : 1, cases.Count);
        Assert.All(cases, testCase => Assert.Null(testCase.SkipReason));
        Assert.Equal(engine, EngineTestSelection.ResolveEngine(method.TestClass.Class));
        Assert.Equal(typeof(DiscoveryProbe), method.Method.DeclaringType);
        Assert.Equal(method.TestClass.Class, method.Method.ReflectedType);

        if (!preEnumerate)
        {
            Assert.IsType<XunitDelayEnumeratedTheoryTestCase>(Assert.Single(cases));
        }
    }

    /// <summary>Ordinary inherited member data also covers all engines without custom filtering annotations.</summary>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public async Task OrdinaryMemberVariantsAutomaticallyCoverEveryEngine(
        string engine
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.OrdinaryMember));
        var options = CreateDiscoveryOptions(true);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new TheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Equal(2, cases.Count);
        Assert.All(cases, testCase => Assert.Null(testCase.SkipReason));
        Assert.IsType<MemberDataAttribute>(Assert.Single(method.DataAttributes));
    }

    /// <summary>Discovery reads fixture ownership from metadata without constructing the resource.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DiscoveryDoesNotConstructProviderResources(
        bool preEnumerate
    )
    {
        // Arrange
        var types = new[] { typeof(ProviderFixture<DiscoveryOnlyResource, SqlServerEngine>) };
        var method = CreateTestMethod("SqlServer", nameof(DiscoveryProbe.ConditionalInline), types);
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Single(cases);
        Assert.All(cases, testCase => Assert.Null(testCase.SkipReason));
    }

    /// <summary>Fact exclusions use the concrete injected fixture even when the method is base-declared.</summary>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    public async Task FactExclusionUsesTheConcreteFixture(
        string engine,
        bool expected
    )
    {
        // Arrange
        var reflected = CreateTestMethod(engine, nameof(DiscoveryProbe.ExcludedFact));
        var method = new XunitTestMethod(
            reflected.TestClass,
            typeof(DiscoveryProbe).GetMethod(nameof(DiscoveryProbe.ExcludedFact))!,
            []);

        var options = CreateDiscoveryOptions(true);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new EngineFactDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Equal(expected ? 1 : 0, cases.Count);
        Assert.All(cases, testCase => Assert.Null(testCase.SkipReason));
    }

    /// <summary>Excluded methods vanish before either enumeration strategy invokes their faulty factory.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MethodExclusionDoesNotInvokeDataFactories(
        bool preEnumerate
    )
    {
        // Arrange
        var method = CreateTestMethod("Sqlite", nameof(DiscoveryProbe.ExcludedTheory));
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Empty(cases);
    }

    /// <summary>Default marker ownership never depends on which other engine fixtures a collection registers.</summary>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void ConstructorSelectsEngineFromAMultiEngineCollection(
        string engine
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.Ordinary));
        var collection = new XunitTestCollection(
            method.TestClass.TestCollection.TestAssembly,
            typeof(MultiEngineCollectionDefinition),
            false,
            "Both MySQL engines");

        var testClass = new XunitTestClass(method.TestClass.Class, collection);

        // Act
        var resolved = EngineTestSelection.ResolveEngine(testClass.Class);

        // Assert
        Assert.Equal(engine, resolved);
        Assert.Contains(typeof(ProviderFixture<HarmlessResource, MySqlEngine>), collection.CollectionFixtureTypes);
        Assert.Contains(typeof(ProviderFixture<HarmlessResource, MariaDbEngine>), collection.CollectionFixtureTypes);
    }

    /// <summary>A concrete suite must inject its exact closed provider fixture rather than an interface view.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingConcreteFixtureIsRejected(
        bool interfaceView
    )
    {
        // Arrange
        var types = interfaceView ? [typeof(IProviderFixture)] : Type.EmptyTypes;
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Ordinary), types);

        // Act
        var error = Record.Exception(() => EngineTestSelection.ResolveEngine(method.TestClass.Class));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("inject a concrete provider fixture", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Two different constructor-selected engines cannot silently choose one fixture.</summary>
    [Fact]
    public void ContradictoryConstructorFixturesAreRejected()
    {
        // Arrange
        var types = new[]
        {
            typeof(ProviderFixture<HarmlessResource, MySqlEngine>),
            typeof(ProviderFixture<HarmlessResource, MariaDbEngine>),
        };

        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Ordinary), types);

        // Act
        var error = Record.Exception(() => EngineTestSelection.ResolveEngine(method.TestClass.Class));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("contradictory provider fixtures", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An engine fixture from another provider executable is a visible configuration failure.</summary>
    [Fact]
    public void ForeignConstructorFixtureIsRejected()
    {
        // Arrange
        var types = new[] { typeof(ProviderFixture<HarmlessResource, MySqlEngine>) };
        var method = CreateTestMethod("Sqlite", nameof(DiscoveryProbe.Ordinary), types);

        // Act
        var error = Record.Exception(() => EngineTestSelection.ResolveEngine(method.TestClass.Class));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("foreign provider project", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A marker typo cannot disappear through ownership filtering.</summary>
    [Fact]
    public void UnknownFixtureMarkerIsRejected()
    {
        // Arrange
        var types = new[] { typeof(ProviderFixture<HarmlessResource, UnknownEngine>) };
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Ordinary), types);

        // Act
        var error = Record.Exception(() => EngineTestSelection.ResolveEngine(method.TestClass.Class));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Unknown integration test provider", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Explicit inline exclusions retain every original argument and omit only the named engine.</summary>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    public async Task InlineExclusionsPreserveArguments(
        string engine,
        bool expected
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.ConditionalInline));
        var attribute = Assert.IsType<EngineInlineDataAttribute>(Assert.Single(method.DataAttributes));
        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetData(method.Method, tracker);

        // Assert
        if (expected)
        {
            Assert.Equal<object?>([17], Assert.Single(rows).GetData());
        }
        else
        {
            Assert.Empty(rows);
        }
    }

    /// <summary>Invalid exclusions fail even when their first entry would exclude the current engine.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ExclusionsRequireAReason(
        string? reason
    )
    {
        // Arrange
        var excluded = new[] { "Sqlite" };

        // Act
        var error = Record.Exception(() => EngineTestSelection.AppliesToEngine("Sqlite", excluded, reason));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("require a nonempty reason", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Every excluded engine is validated before the applicable-row decision.</summary>
    [Fact]
    public void UnknownExcludedEngineIsRejected()
    {
        // Arrange
        var excluded = new[] { "Sqlite", "Postgres" };

        // Act
        var error = Record.Exception(() => EngineTestSelection.AppliesToEngine("Sqlite", excluded, "A real reason."));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(
            "Unknown excluded integration test provider 'Postgres'",
            error.Message,
            StringComparison.Ordinal);
    }

    /// <summary>Member-data variants apply explicit exclusions while default rows run on every engine.</summary>
    [Theory]
    [InlineData("MySql", 3)]
    [InlineData("MariaDb", 3)]
    [InlineData("PostgreSql", 2)]
    [InlineData("SqlServer", 3)]
    [InlineData("Sqlite", 2)]
    public async Task MemberVariantsUseTheFixtureEngine(
        string engine,
        int expectedCount
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.Member));
        var attribute = Assert.IsType<EngineMemberDataAttribute>(Assert.Single(method.DataAttributes));
        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetData(method.Method, tracker);

        // Assert
        Assert.Equal(expectedCount, rows.Count);
        Assert.Contains(rows, row => Equals(row.GetData()[0], 43));
        Assert.Equal(method.Method.ReflectedType, attribute.MemberType);
        Assert.Equal(engine != "Sqlite", rows.Any(row => Equals(row.GetData()[0], 17)));
        Assert.Equal(engine != "PostgreSql", rows.Any(row => Equals(row.GetData()[0], 29)));
    }

    /// <summary>An explicit member source remains authoritative over the inherited suite's default source.</summary>
    [Fact]
    public async Task ExplicitMemberTypeIsPreserved()
    {
        // Arrange
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Member));
        var attribute = new EngineMemberDataAttribute(nameof(DiscoveryProbe.Rows))
        {
            MemberType = typeof(ExplicitDataSource),
        };

        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetData(method.Method, tracker);

        // Assert
        Assert.Equal(typeof(ExplicitDataSource), attribute.MemberType);
        Assert.Equal<object?>([53], Assert.Single(rows).GetData());
    }

    /// <summary>Raw member rows retain their engine exclusions for the provider contract guard.</summary>
    [Fact]
    public async Task SourceInspectionPreservesExcludedRows()
    {
        // Arrange
        var method = CreateTestMethod("Sqlite", nameof(DiscoveryProbe.Member));
        var attribute = Assert.IsType<EngineMemberDataAttribute>(Assert.Single(method.DataAttributes));
        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetSourceData(method.Method, tracker);

        // Assert
        Assert.Equal(3, rows.Count);
        var first = Assert.IsType<EngineTheoryDataRow>(rows.First());
        Assert.Equal(["Sqlite"], first.ExcludedEngines);
        Assert.Equal("SQLite has no server-backed variant in this metadata control.", first.Reason);
        Assert.Equal<object?>([17], first.Data);
    }

    /// <summary>Row normalization retains exclusions, xUnit settings, and merged attribute traits.</summary>
    [Fact]
    public async Task MemberNormalizationPreservesMergedRowMetadata()
    {
        // Arrange
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.Member));
        var attribute = new EngineMemberDataAttribute(nameof(ExplicitDataSource.MetadataRows))
        {
            MemberType = typeof(ExplicitDataSource),
            Label = "attribute-label",
            Traits = ["attribute", "attribute-value"],
        };

        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetSourceData(method.Method, tracker);

        // Assert
        var row = Assert.IsType<EngineTheoryDataRow>(Assert.Single(rows));
        Assert.Equal<object?>([61], row.Data);
        Assert.Equal(["SqlServer"], row.ExcludedEngines);
        Assert.Equal("This synthetic variant requires no SQL Server execution.", row.Reason);
        Assert.Equal("row-label", row.Label);
        Assert.Equal("row-display", row.TestDisplayName);
        Assert.Equal("row-skip", row.Skip);
        Assert.Equal(73, row.Timeout);
        Assert.True(row.Explicit);
        Assert.True(row.DisableParallelization);
        Assert.NotNull(row.Traits);
        Assert.Contains("row-value", row.Traits["row"]);
        Assert.Contains("attribute-value", row.Traits["attribute"]);
    }

    /// <summary>Filtered member discovery enumerates its source once in the phase chosen by xUnit.</summary>
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 0)]
    public async Task DiscoveryDoesNotEnumerateMemberFactoriesTwice(
        bool preEnumerate,
        bool delayedSource,
        int expectedCalls
    )
    {
        // Arrange
        DiscoveryProbe.SourceCalls = 0;
        var name = delayedSource ? nameof(DiscoveryProbe.DelayedMember) : nameof(DiscoveryProbe.CountedMember);
        var method = CreateTestMethod("MySql", name);
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.Equal(expectedCalls, DiscoveryProbe.SourceCalls);
        Assert.Equal(expectedCalls == 1 ? 2 : 1, cases.Count);

        if (expectedCalls == 0)
        {
            Assert.IsType<XunitDelayEnumeratedTheoryTestCase>(Assert.Single(cases));
        }
    }

    /// <summary>A delayed source yields its arguments when requested during execution.</summary>
    [Fact]
    public async Task DelayedMemberDataEnumeratesOnceWhenRequested()
    {
        // Arrange
        DiscoveryProbe.SourceCalls = 0;
        var method = CreateTestMethod("MySql", nameof(DiscoveryProbe.DelayedMember));
        var attribute = Assert.IsType<EngineMemberDataAttribute>(Assert.Single(method.DataAttributes));
        await using var tracker = new DisposalTracker();

        // Act
        var rows = await attribute.GetData(method.Method, tracker);

        // Assert
        Assert.Equal(1, DiscoveryProbe.SourceCalls);
        Assert.Equal(2, rows.Count);
        Assert.False(attribute.SupportsDiscoveryEnumeration());
    }

    /// <summary>Empty and accidentally over-filtered sources remain visible failures in both runner modes.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task EmptyOrOverFilteredSourceRemainsAFailure(
        bool preEnumerate,
        bool overFiltered
    )
    {
        // Arrange
        var name = overFiltered ? nameof(DiscoveryProbe.OverFiltered) : nameof(DiscoveryProbe.Empty);
        var method = CreateTestMethod("Sqlite", name);
        var options = CreateDiscoveryOptions(preEnumerate);
        var attribute = Assert.IsType<EngineTheoryAttribute>(Assert.Single(method.FactAttributes));

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.False(attribute.SkipTestWithoutData);
        var testCase = Assert.Single(cases);
        Assert.Null(testCase.SkipReason);

        if (preEnumerate)
        {
            var failure = Assert.IsType<ExecutionErrorTestCase>(testCase);
            Assert.Contains("No data found", failure.ErrorMessage, StringComparison.Ordinal);
        }
        else
        {
            var delayed = Assert.IsType<XunitDelayEnumeratedTheoryTestCase>(testCase);
            Assert.False(delayed.SkipTestWithoutData);
        }
    }

    /// <summary>Invalid row metadata is rejected even when a valid member-level exclusion omits its engine.</summary>
    [Fact]
    public async Task WholeMemberExclusionDoesNotHideInvalidRows()
    {
        // Arrange
        var method = CreateTestMethod("Sqlite", nameof(DiscoveryProbe.Member));
        var attribute = new EngineMemberDataAttribute(nameof(ExplicitDataSource.InvalidRows))
        {
            MemberType = typeof(ExplicitDataSource),
            ExcludedEngines = ["Sqlite"],
            Reason = "The member-level metadata control excludes SQLite.",
        };

        await using var tracker = new DisposalTracker();

        // Act
        var error = await Record.ExceptionAsync(async () => await attribute.GetData(method.Method, tracker));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Unknown excluded integration test provider", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Concurrent initialization preserves separate resources and immutable engine identities.</summary>
    [Fact]
    public async Task ProviderFixtureInitializationKeepsEnginesAndResourcesIsolated()
    {
        // Arrange
        await using var first = new ProviderFixture<HarmlessResource, MySqlEngine>();
        await using var second = new ProviderFixture<HarmlessResource, MariaDbEngine>();

        // Act
        await Task.WhenAll(
            first
                .InitializeAsync()
                .AsTask(),
            second
                .InitializeAsync()
                .AsTask());

        // Assert
        Assert.Equal("MySql", first.Engine);
        Assert.Equal("MariaDb", second.Engine);
        Assert.NotSame(first.Value, second.Value);
        Assert.Equal(1, first.Value.Initializations);
        Assert.Equal(1, second.Value.Initializations);
    }

    /// <summary>Disposing one fixture preserves its engine identity and leaves the peer resource alive.</summary>
    [Fact]
    public async Task ProviderFixtureDisposalKeepsOwnershipAndEngineIdentity()
    {
        // Arrange
        var first = new ProviderFixture<HarmlessResource, MySqlEngine>();
        await using var second = new ProviderFixture<HarmlessResource, MariaDbEngine>();
        var firstResource = first.Value;
        var secondResource = second.Value;

        // Act
        await first.DisposeAsync();

        // Assert
        Assert.Equal("MySql", first.Engine);
        Assert.Equal("MariaDb", second.Engine);
        Assert.Same(firstResource, first.Value);
        Assert.Same(secondResource, second.Value);
        Assert.Equal(1, firstResource.Disposals);
        Assert.Equal(0, secondResource.Disposals);
    }

    /// <summary>Creates a fixture-taking metadata owner with a real provider assembly identity.</summary>
    private static XunitTestMethod CreateTestMethod(
        string engine,
        string methodName,
        Type[]? fixtureTypes = null
    )
    {
        var project = engine == "MariaDb" ? "MySql" : engine;
        var name = new AssemblyName($"Doka.EntityFrameworkCore.NestedSet.{project}.Tests");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name.Name!);
        var builder = module.DefineType("InheritedEngineProbe", TypeAttributes.Public, typeof(DiscoveryProbe));
        fixtureTypes ??= [GetFixtureType(engine)];

        foreach (var fixture in fixtureTypes)
        {
            builder.AddInterfaceImplementation(typeof(IClassFixture<>).MakeGenericType(fixture));
        }

        var constructor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, fixtureTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(
            OpCodes.Call,
            typeof(DiscoveryProbe).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null)!);
        il.Emit(OpCodes.Ret);
        var type = builder.CreateType();

        var testAssembly = new XunitTestAssembly(assembly, null, assemblyName: name.FullName);
        var collection = new XunitTestCollection(testAssembly, null, false, "Fixture-owned engine probe");
        var testClass = new XunitTestClass(type, collection);

        return new XunitTestMethod(testClass, type.GetMethod(methodName)!, []);
    }

    /// <summary>Uses only the known closed fixture markers, independently of the current Unit assembly.</summary>
    private static Type GetFixtureType(
        string engine
    ) => engine switch
    {
        "MySql" => typeof(ProviderFixture<HarmlessResource, MySqlEngine>),
        "MariaDb" => typeof(ProviderFixture<HarmlessResource, MariaDbEngine>),
        "PostgreSql" => typeof(ProviderFixture<HarmlessResource, PostgreSqlEngine>),
        "SqlServer" => typeof(ProviderFixture<HarmlessResource, SqlServerEngine>),
        "Sqlite" => typeof(ProviderFixture<HarmlessResource, SqliteEngine>),
        _ => throw new InvalidOperationException($"Unknown metadata probe engine '{engine}'."),
    };

    /// <summary>Selects xUnit's documented theory-enumeration option without altering ambient test context.</summary>
    private static ITestFrameworkDiscoveryOptions CreateDiscoveryOptions(
        bool preEnumerate
    )
    {
        var options = TestFrameworkOptions.ForDiscovery(
            new TestAssemblyConfiguration());

        options.SetValue(TestOptionsNames.Discovery.PreEnumerateTheories, preEnumerate);

        return options;
    }

    /// <summary>Supplies two registrations while the concrete constructor selects its own engine.</summary>
    public sealed class MultiEngineCollectionDefinition :
        ICollectionFixture<ProviderFixture<HarmlessResource, MySqlEngine>>,
        ICollectionFixture<ProviderFixture<HarmlessResource, MariaDbEngine>> { }

    /// <summary>Exercises resource lifetimes without creating a database or container.</summary>
    public sealed class HarmlessResource : IAsyncLifetime
    {
        /// <summary>Gets the number of forwarded initialization calls.</summary>
        public int Initializations { get; private set; }

        /// <summary>Gets the number of forwarded disposal calls.</summary>
        public int Disposals { get; private set; }

        /// <inheritdoc />
        public ValueTask InitializeAsync()
        {
            Initializations++;

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Disposals++;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Rejects resource construction so metadata-only discovery cannot silently activate fixtures.</summary>
    public sealed class DiscoveryOnlyResource
    {
        /// <summary>Fails if discovery crosses the fixture initialization boundary.</summary>
        public DiscoveryOnlyResource()
        {
            throw new InvalidOperationException("Discovery constructed the resource fixture.");
        }
    }

    /// <summary>Supplies a misspelled marker to prove unknown engines remain visible failures.</summary>
    public readonly struct UnknownEngine : IProviderEngine
    {
        /// <inheritdoc />
        public static string Name => "MySQL";
    }

    /// <summary>Keeps deliberately failing metadata controls abstract and outside normal Unit discovery.</summary>
    public abstract class DiscoveryProbe
    {
        private static readonly AsyncLocal<SourceCounter?> s_sourceCalls = new();

        /// <summary>Gets or sets the factory call count local to the current asynchronous test flow.</summary>
        public static int SourceCalls
        {
            get => s_sourceCalls.Value?.Value ?? 0;
            set => s_sourceCalls.Value = new SourceCounter(value);
        }

        /// <summary>Provides one default variant and two independently excluded variants.</summary>
        public static IEnumerable<ITheoryDataRow> Rows =>
        [
            new EngineTheoryDataRow(
                [17],
                ["Sqlite"],
                "SQLite has no server-backed variant in this metadata control."),
            new EngineTheoryDataRow(
                [29],
                ["PostgreSql"],
                "PostgreSQL is excluded only from the second synthetic variant."),
            new TheoryDataRow(43),
        ];

        /// <summary>Provides a source that must never run for its explicitly excluded method.</summary>
        public static IEnumerable<ITheoryDataRow> FaultyRows =>
            throw new InvalidOperationException("An excluded method enumerated its member-data factory.");

        /// <summary>Provides truly empty data as a visible failure control.</summary>
        public static IEnumerable<ITheoryDataRow> EmptyRows => [];

        /// <summary>Provides ordinary member variants without any engine annotations.</summary>
        public static IEnumerable<ITheoryDataRow> OrdinaryRows => [new TheoryDataRow(17), new TheoryDataRow(29)];

        /// <summary>Provides nonempty data that row filtering accidentally empties for SQLite.</summary>
        public static IEnumerable<ITheoryDataRow> OverFilteredRows =>
        [
            new EngineTheoryDataRow([17], ["Sqlite"], "This row-only exclusion must not hide the whole theory."),
        ];

        /// <summary>Provides ordinary variants while recording the exact number of source invocations.</summary>
        public static IEnumerable<ITheoryDataRow> CountingRows
        {
            get
            {
                // WHY: Mutate the flow-local holder so async factory calls remain observable after awaiting discovery.
                s_sourceCalls.Value!.Value++;

                return [new TheoryDataRow(17), new TheoryDataRow(29)];
            }
        }

        /// <summary>Represents ordinary shared cases whose variants need no provider selection metadata.</summary>
        [Theory]
        [InlineData(17)]
        [InlineData(29)]
        public void Ordinary(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Represents ordinary inherited static member data with no provider selection contract.</summary>
        [Theory]
        [MemberData(nameof(OrdinaryRows))]
        public void OrdinaryMember(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Represents a shared fact unavailable only on an explicitly named engine.</summary>
        [EngineFact(ExcludedEngines = ["Sqlite"], Reason = "The synthetic fact requires a server engine.")]
        public void ExcludedFact() => RequireMetadataOnly(0);

        /// <summary>Represents a method exclusion that must precede data factory enumeration.</summary>
        [EngineTheory(ExcludedEngines = ["Sqlite"], Reason = "The synthetic theory requires a server engine.")]
        [EngineMemberData(nameof(FaultyRows))]
        public void ExcludedTheory(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Represents an independently excluded inline variant with no engine argument.</summary>
        [EngineTheory]
        [EngineInlineData(17, ExcludedEngines = ["Sqlite"], Reason = "The variant requires a server engine.")]
        public void ConditionalInline(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Represents inherited member data with default and explicitly excluded variants.</summary>
        [EngineTheory]
        [EngineMemberData(nameof(Rows))]
        public void Member(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Represents discoverable member data that must be enumerated exactly once.</summary>
        [EngineTheory]
        [EngineMemberData(nameof(CountingRows))]
        public void CountedMember(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Represents member data that xUnit must reserve for execution.</summary>
        [EngineTheory]
        [EngineMemberData(nameof(CountingRows), DisableDiscoveryEnumeration = true)]
        public void DelayedMember(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Represents a genuinely empty source whose discovery must retain a visible failure.</summary>
        [EngineTheory]
        [EngineMemberData(nameof(EmptyRows))]
        public void Empty(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Represents accidental over-filtering without a corresponding whole-method exclusion.</summary>
        [EngineTheory]
        [EngineMemberData(nameof(OverFilteredRows))]
        public void OverFiltered(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Rejects accidental direct execution of these deliberately metadata-only subjects.</summary>
        private static void RequireMetadataOnly(
            int value
        ) => throw new InvalidOperationException($"Metadata control executed with value {value}.");

        /// <summary>Isolates parallel test counters while keeping asynchronous child calls observable.</summary>
        private sealed class SourceCounter
        {
            /// <summary>Starts one test-flow counter.</summary>
            internal SourceCounter(
                int value
            )
            {
                Value = value;
            }

            /// <summary>Gets or sets the number of factory calls observed by this test flow.</summary>
            internal int Value { get; set; }
        }
    }

    /// <summary>Provides independent sources to verify explicit MemberType and xUnit metadata normalization.</summary>
    public static class ExplicitDataSource
    {
        /// <summary>Supplies a distinguishable payload under the inherited member's name.</summary>
        public static IEnumerable<ITheoryDataRow> Rows => [new TheoryDataRow(53)];

        /// <summary>Supplies every customizable row setting for the normalization regression.</summary>
        public static IEnumerable<ITheoryDataRow> MetadataRows =>
        [
            new EngineTheoryDataRow([61], ["SqlServer"], "This synthetic variant requires no SQL Server execution.")
            {
                Label = "row-label",
                TestDisplayName = "row-display",
                Skip = "row-skip",
                Timeout = 73,
                Explicit = true,
                DisableParallelization = true,
                Traits = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["row"] = ["row-value"],
                },
            },
        ];

        /// <summary>Supplies a malformed exclusion that must survive all filtering guards as a visible error.</summary>
        public static IEnumerable<ITheoryDataRow> InvalidRows =>
        [
            new EngineTheoryDataRow([17], ["Postgres"], "A valid reason cannot repair an unknown engine name."),
        ];
    }
}
