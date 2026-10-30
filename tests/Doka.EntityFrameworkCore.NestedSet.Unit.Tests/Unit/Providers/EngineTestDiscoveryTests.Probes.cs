namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class EngineTestDiscoveryTests
{
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
    public abstract partial class DiscoveryProbe
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
