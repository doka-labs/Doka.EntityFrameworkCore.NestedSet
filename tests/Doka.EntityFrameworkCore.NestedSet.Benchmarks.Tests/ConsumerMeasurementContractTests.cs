using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Checks migrated consumer workloads without statistical execution or numeric budgets.</summary>
public sealed class ConsumerMeasurementContractTests
{
    /// <summary>Every original winner reuse and distinct-person rank shape retains its independent oracle.</summary>
    /// <param name="shape">The original scalar selection workload.</param>
    [Theory]
    [InlineData("OwnedUnique")]
    [InlineData("OwnedPriority")]
    [InlineData("Rank")]
    public void Select_PreparedPersonWorkload_PreservesCompleteEvidence(
        string shape
    )
    {
        // Arrange
        var workload = new SupervisorPersonSelectionBenchmarks { Shape = shape };
        workload.Initialize();

        // Act
        var result = workload.Select();

        // Assert
        Assert.NotNull(result);
        workload.VerifyWorkload();
    }

    /// <summary>Counter exhaustion and continuation ownership remain functional workload oracles.</summary>
    /// <param name="shape">The independently prepared mutable operation.</param>
    [Theory]
    [InlineData("Admissions")]
    [InlineData("Continuations")]
    public void Advance_PreparedBookkeeping_PreservesAdmissionAndEvidence(
        string shape
    )
    {
        // Arrange
        var workload = new SupervisorChainBookkeepingBenchmarks { Shape = shape };
        workload.Prepare();

        // Act
        var admitted = workload.Advance();

        // Assert
        Assert.Equal(shape == "Admissions" ? 10_000 : 1024, admitted);
        workload.Verify();
    }

    /// <summary>Unknown workload names cannot silently choose another measured operation.</summary>
    [Fact]
    public void Initialize_UnknownPersonShape_RejectsBeforeMeasurement()
    {
        // Arrange
        var workload = new SupervisorPersonSelectionBenchmarks { Shape = "unknown" };

        // Act
        var error = Record.Exception(workload.Initialize);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(error);
    }

    /// <summary>The original duplicate-heavy snapshot and complete empty grain retain functional validation.</summary>
    [Fact]
    public void Snapshot_ConsumerInputs_PreserveOwnersAndRequestedGrain()
    {
        // Arrange
        var snapshot = new SignalReportSnapshotBenchmarks();
        snapshot.Initialize();
        var grid = new SignalGridConstructionBenchmarks();
        grid.Initialize();

        // Act
        var request = snapshot.Snapshot();
        var cells = grid.CreateGrid();

        // Assert
        Assert.NotNull(request);
        Assert.NotNull(cells);
        snapshot.VerifyWorkload();
        grid.Verify();
    }

    /// <summary>Both completed-reader and original-fault streaming paths preserve exact ownership.</summary>
    /// <param name="failure">Whether the large sparse reader fails after its second scalar.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Materialize_PreparedStreamingGrid_PreservesResultOrFailure(
        bool failure
    )
    {
        // Arrange
        await using var workload = new SignalGridStreamingBenchmarks();
        workload.Initialize();
        workload.Prepare();

        // Act
        if (failure)
        {
            _ = await workload.SparseFailure();
        }
        else
        {
            await workload.Dense();
        }

        // Assert
        workload.Verify();
    }

    /// <summary>Cleanup releases prepared readers even when no operation consumed them.</summary>
    [Fact]
    public async Task StreamingCleanup_UnconsumedInputs_CanBeReleasedRepeatedly()
    {
        // Arrange
        await using var workload = new SignalGridStreamingBenchmarks();
        workload.Initialize();
        workload.Prepare();

        // Act
        await workload.DisposeAsync();
        await workload.DisposeAsync();

        // Assert
        Assert.Throws<InvalidOperationException>(workload.Prepare);
    }

    /// <summary>Native benchmark discovery accepts the migrated mutable and immutable workload lifecycles.</summary>
    /// <param name="type">The actual benchmark family.</param>
    [Theory]
    [InlineData(typeof(SignalReportSnapshotBenchmarks))]
    [InlineData(typeof(SignalPathConstructionBenchmarks))]
    [InlineData(typeof(SignalGridConstructionBenchmarks))]
    [InlineData(typeof(SignalGridStreamingBenchmarks))]
    [InlineData(typeof(SupervisorConstructionBenchmarks))]
    [InlineData(typeof(SupervisorPersonSelectionBenchmarks))]
    [InlineData(typeof(SupervisorChainBookkeepingBenchmarks))]
    public void StructuralErrors_MigratedConsumerFamily_HasValidNativeMetadata(
        Type type
    )
    {
        // Arrange
        var configuration = BenchmarkConfiguration.Create(new BenchmarkRunOptions());
        using var native = BenchmarkConverter.TypeToBenchmarks(type, configuration);

        // Act
        var errors = BenchmarkValidationTestSupport.StructuralErrors(native);

        // Assert
        Assert.NotEmpty(native.BenchmarksCases);
        Assert.Empty(errors);
    }

    /// <summary>The exact constructor variants build real expressions with no server acquisition.</summary>
    /// <param name="engine">The actual dialect.</param>
    /// <param name="origins">The explicit or all-origin request form.</param>
    /// <param name="fields">The original or expanded field count.</param>
    /// <param name="repeated">Whether every match deliberately repeats the same field.</param>
    [Theory]
    [MemberData(nameof(ConstructionCases))]
    public async Task Build_ConsumerMetadataVariants_RemainDeferred(
        string engine,
        int origins,
        int fields,
        bool repeated
    )
    {
        // Arrange
        await using var workload = new SupervisorConstructionBenchmarks
        {
            Engine = engine,
            Origins = origins,
            Fields = fields,
            RepeatedField = repeated,
        };

        workload.Initialize();

        // Act
        var reduced = workload.ResolutionRows();
        var candidates = workload.Candidates();

        // Assert
        Assert.IsAssignableFrom<IQueryable>(reduced);
        Assert.IsAssignableFrom<IQueryable>(candidates);
    }

    /// <summary>Undeclared construction variants cannot silently change the measured request shape.</summary>
    /// <param name="origins">The requested origin shape.</param>
    /// <param name="fields">The requested field count.</param>
    /// <param name="parameter">The invalid workload dimension.</param>
    [Theory]
    [InlineData(0, 4, "Origins")]
    [InlineData(2, 4, "Origins")]
    [InlineData(1, 0, "Fields")]
    [InlineData(1, 6, "Fields")]
    public async Task Initialize_UndeclaredConstructionVariant_RejectsBeforeProviderAcquisition(
        int origins,
        int fields,
        string parameter
    )
    {
        // Arrange
        await using var workload = new SupervisorConstructionBenchmarks
        {
            Engine = "Unknown",
            Origins = origins,
            Fields = fields,
        };

        // Act
        var error = Assert.Throws<ArgumentOutOfRangeException>(workload.Initialize);

        // Assert
        Assert.Equal(parameter, error.ParamName);
    }

    /// <summary>Retains every original dialect, origin and field variant plus the fifth-field comparison.</summary>
    /// <returns>All distinct and repeated request forms without launching a statistical measurement.</returns>
    public static IEnumerable<TheoryDataRow<string, int, int, bool>> ConstructionCases()
    {
        string[] engines = ["MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite"];
        int[] origins = [1, 16];
        int[] fields = [1, 4, 5];

        foreach (var engine in engines)
        {
            foreach (var origin in origins)
            {
                foreach (var field in fields)
                {
                    yield return new TheoryDataRow<string, int, int, bool>(engine, origin, field, false);
                    yield return new TheoryDataRow<string, int, int, bool>(engine, origin, field, true);
                }
            }
        }
    }

    /// <summary>The migrated report workload retains both metadata-owner modes and criterion shapes.</summary>
    /// <param name="engine">The actual configured dialect.</param>
    /// <param name="nearest">Whether matching selects a nearest metadata owner.</param>
    /// <param name="criteria">The four-owner baseline or added same-owner scalar criterion.</param>
    [Theory]
    [MemberData(nameof(ReportCases))]
    public async Task Build_ReportMetadataVariants_RetainActualDeferredQueries(
        string engine,
        bool nearest,
        int criteria
    )
    {
        // Arrange
        await using var workload = new SignalReportConstructionBenchmarks
        {
            Engine = engine,
            Anchors = 4,
            NearestMetadata = nearest,
            Criteria = criteria,
        };

        workload.Initialize();

        // Act
        var actual = workload.Api();
        var reference = workload.Reference();

        // Assert
        Assert.IsAssignableFrom<IQueryable>(actual);
        Assert.IsAssignableFrom<IQueryable>(reference);
    }

    /// <summary>Discovers the original ten dialect/mode cases for each criterion shape.</summary>
    /// <returns>The complete twenty-case disconnected report-construction matrix.</returns>
    public static IEnumerable<TheoryDataRow<string, bool, int>> ReportCases()
    {
        string[] engines = ["MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite"];

        foreach (var engine in engines)
        {
            yield return new TheoryDataRow<string, bool, int>(engine, false, 4);
            yield return new TheoryDataRow<string, bool, int>(engine, true, 4);
            yield return new TheoryDataRow<string, bool, int>(engine, false, 5);
            yield return new TheoryDataRow<string, bool, int>(engine, true, 5);
        }
    }

    /// <summary>Every original path-proof dialect reaches the real builder and releases its owned provider.</summary>
    /// <param name="engine">The actual dialect whose shadow-property mapping supplies the proof.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Sqlite")]
    public async Task Build_PathProof_RetainsActualConditionalExpression(
        string engine
    )
    {
        // Arrange
        await using var workload = new SignalPathConstructionBenchmarks { Engine = engine };
        workload.Initialize();

        // Act
        var proof = workload.Build();

        // Assert
        Assert.IsType<ConditionalExpression>(proof, exactMatch: false);
    }
}
