using Doka.EntityFrameworkCore.NestedSet.Benchmarks;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Executes actual managed-save benchmark workloads with blocking semantic oracles only.</summary>
public sealed class ManagedSaveMeasurementTests
{
    /// <summary>Gets complete and native graph capture and both exact recovery shapes.</summary>
    public static IEnumerable<object[]> ComplexCases => ComplexGraphSnapshotBenchmarks.Cases
        .Select(value => new object[] { value.Operation, value.Rows });

    /// <summary>Gets every original tracked population and selected candidate width.</summary>
    public static IEnumerable<object[]> PlanCases => GuardPlanCaptureBenchmarks.Cases
        .Select(value => new object[] { value.Nodes, value.Candidates });

    /// <summary>Gets unchanged scalar, hierarchy, populated navigation and prepared-plan guards.</summary>
    public static IEnumerable<object[]> ProbeCases => GuardProbeBenchmarks.Cases
        .Select(value => new object[] { value.Operation, value.Nodes });

    /// <summary>Gets every full FK, native/supplemental recovery and membership-journal case.</summary>
    public static IEnumerable<object[]> RelationshipCases => RelationshipSnapshotBenchmarks.Cases
        .Select(value => new object[]
        {
            value.Operation, value.Nodes, value.AllEntries, value.WarmNativeMaps, value.Hierarchy,
        });

    /// <summary>Gets each real prepared import wave, including both completed native-snapshot profiles.</summary>
    public static IEnumerable<object[]> ImportCases => PreparedImportGuardBenchmarks.Cases
        .Select(value => new object[] { value.Nodes, value.Offset, value.CompleteSnapshot });

    /// <summary>Gets every actual completion producer and completed-wave cardinality.</summary>
    public static IEnumerable<object[]> CompletionCases
    {
        get
        {
            foreach (var shape in new[] { "array", "list", "values", "projection", "flattened" })
            {
                foreach (var rows in new[] { 0, 1, 64, 1_024 })
                {
                    yield return new object[] { shape, rows };
                }
            }
        }
    }

    /// <summary>Retains graph object identity, originals, modification flags and native topology.</summary>
    /// <param name="operation">The real production operation.</param>
    /// <param name="rows">The exact graph width.</param>
    [Theory]
    [MemberData(nameof(ComplexCases))]
    public void ComplexGraphPreservesItsIndependentPreimage(string operation, int rows)
    {
        // Arrange
        var benchmark = new ComplexGraphSnapshotBenchmarks { Case = new(operation, rows) };
        benchmark.Setup();

        try
        {
            // Act
            var result = benchmark.Execute();

            // Assert
            Assert.NotNull(result);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Retains the exact authorized candidate set independently from unrelated entries.</summary>
    /// <param name="nodes">The total native tracker population.</param>
    /// <param name="candidates">The selected planned writes.</param>
    [Theory]
    [MemberData(nameof(PlanCases))]
    public void CapturedPlanPreservesCandidateAuthorization(int nodes, int candidates)
    {
        // Arrange
        var benchmark = new GuardPlanCaptureBenchmarks { Case = new(nodes, candidates) };
        benchmark.Setup();

        try
        {
            // Act
            var result = benchmark.Capture();

            // Assert
            Assert.NotNull(result);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Retains every unchanged guard result and exact native entry population.</summary>
    /// <param name="operation">The native guard path.</param>
    /// <param name="nodes">The tracked population or independent parent count.</param>
    [Theory]
    [MemberData(nameof(ProbeCases))]
    public void GuardProbePreservesUnchangedState(string operation, int nodes)
    {
        // Arrange
        var benchmark = new GuardProbeBenchmarks { Case = new(operation, nodes) };
        benchmark.Setup();

        try
        {
            // Act
            var changed = benchmark.Probe();

            // Assert
            Assert.False(changed);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks complete native/CLR recovery, selected-entry flags and ownership for every former profile.</summary>
    /// <param name="operation">The actual capture, recovery or completion operation.</param>
    /// <param name="nodes">The original owner population.</param>
    /// <param name="allEntries">Whether all update entries are selected.</param>
    /// <param name="warmNativeMaps">Whether native maps were previously initialized.</param>
    /// <param name="hierarchy">Whether skipped writes are Added hierarchy entries.</param>
    [Theory]
    [MemberData(nameof(RelationshipCases))]
    public void RelationshipOperationPreservesNativeAndClrContracts(
        string operation,
        int nodes,
        bool allEntries,
        bool warmNativeMaps,
        bool hierarchy
    )
    {
        // Arrange
        var benchmark = new RelationshipSnapshotBenchmarks
        {
            Case = new(operation, nodes, allEntries, warmNativeMaps, hierarchy),
        };

        benchmark.Setup();

        try
        {
            // Act
            var result = benchmark.Execute();

            // Assert
            if (operation == "SkippedWrite")
            {
                Assert.Null(result);
            }
            else
            {
                Assert.NotNull(result);
            }
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Retains complete import authorization while native tracking remains bounded to one wave.</summary>
    /// <param name="nodes">The complete validated input count.</param>
    /// <param name="offset">The start of this native wave.</param>
    /// <param name="completeSnapshot">Whether all inputs are already native reference-set members.</param>
    [Theory]
    [MemberData(nameof(ImportCases))]
    public void PreparedImportPreservesEveryBoundedWave(int nodes, int offset, bool completeSnapshot)
    {
        // Arrange
        var benchmark = new PreparedImportGuardBenchmarks { Case = new(nodes, offset, completeSnapshot) };
        benchmark.Setup();

        try
        {
            // Act
            var changed = benchmark.Probe();

            // Assert
            Assert.False(changed);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Retains original input identities without introducing relationship work for any producer.</summary>
    /// <param name="shape">The actual completion producer.</param>
    /// <param name="rows">The completed wave size.</param>
    [Theory]
    [MemberData(nameof(CompletionCases))]
    public void CompletionPreservesEveryProducer(string shape, int rows)
    {
        // Arrange
        var benchmark = new RelationshipCompletionBenchmarks { Shape = shape, Rows = rows };
        benchmark.Setup();

        // Act
        benchmark.Complete();

        // Assert
        benchmark.Cleanup();
    }

    /// <summary>Retains exactly one ordinary FK row or four same-table peers across tracker sizes.</summary>
    /// <param name="rows">The complete unrelated tracker population.</param>
    /// <param name="sharing">Whether selected entries share a physical row.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(128, false)]
    [InlineData(2048, false)]
    [InlineData(1, true)]
    [InlineData(128, true)]
    [InlineData(2048, true)]
    public void SharingCapturePreservesOnlyPhysicalRowPeers(int rows, bool sharing)
    {
        // Arrange
        var benchmark = new SharingCaptureBenchmarks { Rows = rows, Sharing = sharing };
        benchmark.Setup();
        benchmark.PrepareIteration();

        try
        {
            // Act
            var count = benchmark.Capture();

            // Assert
            Assert.Equal(sharing ? 4 : 1, count);
            benchmark.ValidateIteration();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Proves that the sharing oracle rejects a missing measured operation instead of passing vacuously.</summary>
    [Fact]
    public void SharingOracleRejectsMissingClosureDiscovery()
    {
        // Arrange
        var benchmark = new SharingCaptureBenchmarks { Rows = 1, Sharing = true };
        benchmark.Setup();
        benchmark.PrepareIteration();

        try
        {
            // Act
            var error = Record.Exception(benchmark.ValidateIteration);

            // Assert
            Assert.IsType<InvalidOperationException>(error);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Rejects invalid import wave bounds before acquiring context or provider ownership.</summary>
    /// <param name="nodes">The invalid complete population.</param>
    /// <param name="offset">The invalid active-wave start.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    public void PreparedImportRejectsInvalidWave(int nodes, int offset)
    {
        // Arrange
        var benchmark = new PreparedImportGuardBenchmarks { Case = new(nodes, offset, false) };

        // Act
        var error = Record.Exception(benchmark.Setup);

        // Assert
        Assert.IsType<ArgumentException>(error);
    }
}
