namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>
/// Validates tracking benchmark operations and fixture oracles without executing statistical measurements.
/// </summary>
public sealed class TrackingMeasurementWorkloadTests
{
    /// <summary>Cleanup permits repeated disposal even when fixture setup never completed.</summary>
    /// <param name="shape">The tracking fixture owning a real context or guard.</param>
    [Theory]
    [InlineData("Forest")]
    [InlineData("Binary")]
    [InlineData("IndependentRoots")]
    [InlineData("BulkPlan")]
    [InlineData("TrackedIdentity")]
    [InlineData("Snapshot")]
    [InlineData("TypedProvider")]
    [InlineData("TypedProperty")]
    [InlineData("InsertionIdentity")]
    public void Dispose_UninitializedTrackingFixtures_IsIdempotent(
        string shape
    )
    {
        // Arrange
        IDisposable benchmark = shape switch
        {
            "Forest" => new ForestPreparationMeasurementBenchmarks(),
            "Binary" => new BinaryProviderMeasurementBenchmarks(),
            "IndependentRoots" => new IndependentIdentityCaptureMeasurementBenchmarks(),
            "BulkPlan" => new BulkPlanMeasurementBenchmarks(),
            "TrackedIdentity" => new TrackedIdentityMeasurementBenchmarks(),
            "Snapshot" => new TrackerSnapshotMeasurementBenchmarks(),
            "TypedProvider" => new TypedProviderMeasurementBenchmarks(),
            "TypedProperty" => new TypedPropertyReadMeasurementBenchmarks(),
            "InsertionIdentity" => new InsertionIdentityMeasurementBenchmarks(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var error = Record.Exception(() =>
        {
            benchmark.Dispose();
            benchmark.Dispose();
        });

        // Assert
        Assert.Null(error);
    }

    /// <summary>Both exact generic and required object-fallback dictionary routes retain their key semantics.</summary>
    /// <param name="boxed">Whether the actual EF comparer requires object fallback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainsKey_PreparedDictionary_RetainsPresentKey(
        bool boxed
    )
    {
        // Arrange
        var benchmark = new KeyComparerMeasurementBenchmarks { BoxedFallback = boxed };
        benchmark.Setup();

        // Act
        var found = benchmark.ContainsKey();

        // Assert
        Assert.True(found);
        benchmark.VerifyWorkload();
    }

    /// <summary>Every measured Unicode representation retains its independent scalar-count oracle.</summary>
    /// <param name="input">An ASCII, BMP, supplementary or mixed eight-scalar identity.</param>
    [Theory]
    [InlineData("abcdefgh")]
    [InlineData("\u00e4\u03a9\u4e2d\u00f1\u0416\u00e9\u3042\u20ac")]
    [InlineData("\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606\U0001f607")]
    [InlineData("ab\U0001f600c\U0001f601def")]
    public void CountScalars_Representations_RetainEightScalars(
        string input
    )
    {
        // Arrange
        var benchmark = new UnicodeIdentityMeasurementBenchmarks { Input = input };

        // Act
        var count = benchmark.CountScalars();

        // Assert
        Assert.Equal(8, count);
        benchmark.VerifyWorkload();
    }

    /// <summary>A semantically changed fixture is rejected independently of all timing and allocation values.</summary>
    [Fact]
    public void VerifyWorkload_ChangedUnicodeFixture_IsRejected()
    {
        // Arrange
        var benchmark = new UnicodeIdentityMeasurementBenchmarks { Input = "sevenxx" };

        // Act
        var error = Record.Exception(benchmark.VerifyWorkload);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Actual provider comparisons retain native, nullable and normalized converter semantics.</summary>
    /// <param name="role">The exact provider-value shape.</param>
    [Theory]
    [InlineData("Int")]
    [InlineData("Guid")]
    [InlineData("NullableParent")]
    [InlineData("Converted")]
    public void Compare_ProviderShapes_RetainTypedAndErasedResults(
        string role
    )
    {
        // Arrange
        var benchmark = new TypedProviderMeasurementBenchmarks { Role = role };
        benchmark.Setup();
        try
        {
            // Act
            var typed = benchmark.TypedComparison();
            var erased = benchmark.ErasedComparison();

            // Assert
            Assert.True(typed);
            Assert.True(erased);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Binary observations retain all metadata routes and the framework structural hash window.</summary>
    /// <param name="shape">The native, erased or converted dispatch route.</param>
    /// <param name="length">The independent binary input size.</param>
    [Theory]
    [InlineData("native", 0)]
    [InlineData("native", 32)]
    [InlineData("erased", 9)]
    [InlineData("erased", 65_536)]
    [InlineData("converted", 8)]
    [InlineData("converted", 1024)]
    [InlineData("converted-erased", 1)]
    [InlineData("converted-erased", 16)]
    public void Compare_BinaryShapes_RetainIndependentEqualValues(
        string shape,
        int length
    )
    {
        // Arrange
        var benchmark = new BinaryProviderMeasurementBenchmarks { Shape = shape, Length = length };
        benchmark.Setup();
        try
        {
            // Act
            var equal = benchmark.Compare();
            _ = benchmark.Hash();

            // Assert
            Assert.True(equal);
            benchmark.VerifyWorkload();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Every identity storage shape supports the same refresh, comparison and detach preparation.</summary>
    /// <param name="shape">The exact assigned, generated, converted-member or indexer identity shape.</param>
    [Theory]
    [InlineData("int")]
    [InlineData("guid")]
    [InlineData("compound")]
    [InlineData("generated")]
    [InlineData("shadow")]
    [InlineData("string")]
    [InlineData("field")]
    [InlineData("indexer")]
    [InlineData("indexer-sentinel")]
    public void Refresh_IdentityStorage_RetainsCapturedKeys(
        string shape
    )
    {
        // Arrange
        var benchmark = new InsertionIdentityMeasurementBenchmarks { Shape = shape };
        benchmark.Setup();
        try
        {
            // Act
            benchmark.Refresh();
            benchmark.PrepareDetach();
            var matches = benchmark.Matches();

            // Assert
            Assert.True(matches);
            benchmark.VerifyWorkload();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Every construction representation retains its independent reference expression.</summary>
    /// <param name="shape">The exact native, lifted or operator-free construction shape.</param>
    [Theory]
    [InlineData("Guid")]
    [InlineData("NullableGuid")]
    [InlineData("String")]
    [InlineData("Int")]
    [InlineData("NullableInt")]
    [InlineData("Long")]
    [InlineData("NullableLong")]
    [InlineData("Typed")]
    [InlineData("NullableTyped")]
    [InlineData("Object")]
    [InlineData("NullableObject")]
    public void Current_EqualityShape_MatchesIndependentReference(
        string shape
    )
    {
        // Arrange
        var benchmark = new TypedEqualityMeasurementBenchmarks { ShapeName = shape };
        benchmark.Setup();
        var expected = benchmark.Reference();

        // Act
        var actual = benchmark.Current();

        // Assert
        Assert.Equal(expected.NodeType, actual.NodeType);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.ToString(), actual.ToString());
        benchmark.VerifyWorkload();
    }

    /// <summary>The wide-plan fixture retains all nodes with either sentinel or caller-prefilled structure.</summary>
    /// <param name="prefilled">Whether detached inputs retain original non-sentinel structural values.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildPlan_WideDetachedInputs_RetainsEveryNode(
        bool prefilled
    )
    {
        // Arrange
        var benchmark = new BulkPlanMeasurementBenchmarks { Prefilled = prefilled };
        benchmark.Setup();
        try
        {
            // Act
            var plan = benchmark.BuildPlan();

            // Assert
            Assert.NotNull(plan);
            benchmark.VerifyWorkload();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>
    /// Guard traversal preserves empty, singleton and both sides of the internal candidate-block boundary.
    /// </summary>
    /// <param name="candidates">The exact candidate population prepared outside measurement.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void VerifyUnchanged_CandidateBoundaries_RetainsCapturedMembership(
        int candidates
    )
    {
        // Arrange
        var benchmark = new TrackedIdentityMeasurementBenchmarks { Candidates = candidates };
        benchmark.Setup();
        try
        {
            // Act
            benchmark.VerifyUnchanged();

            // Assert
            benchmark.VerifyWorkload();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>The structural read workload observes all six real mapped roles.</summary>
    [Fact]
    public void ReadRoles_LiveMappedValues_RetainsCompleteIndependentSum()
    {
        // Arrange
        var benchmark = new TypedPropertyReadMeasurementBenchmarks();
        benchmark.Setup();
        try
        {
            // Act
            var sum = benchmark.ReadRoles();

            // Assert
            Assert.Equal(11, sum);
            benchmark.VerifyWorkload();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>The large scalar snapshot workload retains its original tracked population and model width.</summary>
    [Fact]
    public void Capture_TrackedScalars_RetainsCompletePopulation()
    {
        // Arrange
        var benchmark = new TrackerSnapshotMeasurementBenchmarks();
        benchmark.Setup();
        try
        {
            // Act
            var snapshot = benchmark.Capture();

            // Assert
            Assert.NotNull(snapshot);
            benchmark.VerifyWorkload();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>
    /// The independent-root workload captures all distinct input rows rather than reusing one identity.
    /// </summary>
    [Fact]
    public void CaptureRoots_IndependentEntries_RetainsEveryResult()
    {
        // Arrange
        var benchmark = new IndependentIdentityCaptureMeasurementBenchmarks();
        benchmark.Setup();
        try
        {
            // Act
            var identities = benchmark.CaptureRoots();

            // Assert
            Assert.NotNull(identities);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Unrelated ordinals cannot change source/destination order in the actual private selector.</summary>
    /// <param name="ignored">The original unrelated-row population.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(2048)]
    public void Resolve_UnrelatedOrdinals_RetainsOrderedAnchors(
        int ignored
    )
    {
        // Arrange
        var benchmark = new TreeIdentityResolutionMeasurementBenchmarks { IgnoredRows = ignored };
        benchmark.Setup();

        // Act
        var resolved = benchmark.Resolve();

        // Assert
        Assert.NotEqual(resolved.Source, resolved.Target);
        benchmark.VerifyWorkload();
    }

    /// <summary>A measured public forest attempt restores detached inputs at the actual first-I/O boundary.</summary>
    [Fact]
    public async Task PrepareAndRecoverForest_ConnectionBoundary_RestoresAllInputs()
    {
        // Arrange
        var benchmark = new ForestPreparationMeasurementBenchmarks();
        benchmark.PrepareIteration();
        try
        {
            // Act
            await benchmark.PrepareAndRecoverForest();

            // Assert
            benchmark.VerifyWorkload();
        }
        finally
        {
            benchmark.CleanupIteration();
        }
    }
}
