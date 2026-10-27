using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Checks the provider-specific native JSON work reader without starting database resources.</summary>
[DatabaseIndependent]
public sealed class DensePathNativeWorkTests : ProviderTest,
    IClassFixture<ProviderFixture<ProviderResources, MySqlEngine>>
{
    /// <summary>Uses a resource-free fixture while retaining explicit test-engine ownership.</summary>
    /// <param name="fixture">The immutable provider marker; it owns no database.</param>
    public DensePathNativeWorkTests(
        ProviderFixture<ProviderResources, MySqlEngine> fixture
    ) : base(fixture) { }

    /// <summary>Separates materialized output from input while preserving native escapes and fractional rows.</summary>
    [Fact]
    public void NativeActualCountersRetainRowsTimesLoopsAndMaterialization()
    {
        // Arrange
        const string plan = """
                            {"query_block":{"filesort":{"r_output_rows":2,"table":{
                              "r_rows":2.5,"r_loops":4,"attached_condition":"TreeId = '\x00'",
                              "materialized":{"query_block":{"r_loops":1,"filesort":{"r_output_rows":25001,
                                "temporary_table":{"table":{"r_rows":300000,"r_loops":1}}}}}
                            }}}}
                            """;

        // Act
        var work = DensePathNativeWork.MariaDb(plan);

        // Assert
        Assert.Equal(300000D, work.OperatorTuples);
        Assert.Equal((double?)300000, work.PostIndexTuples);
        Assert.Equal((double?)25001, work.MaterializedOutput);
        Assert.Equal(0, work.UnknownMaterializedOutputs);
        Assert.Equal(2, work.ExecutedOperators);
        Assert.Equal((double?)2, work.RootRows);
        Assert.Null(work.FullPrefixLoops);
        Assert.Contains("\\x00", plan, StringComparison.Ordinal);
    }

    /// <summary>Pre-index enumeration includes rows rejected by index condition pushdown.</summary>
    [Fact]
    public void PreIndexRowsRetainCumulativeEnumerationBeforeIndexFiltering()
    {
        // Arrange
        const string plan = """
                            {"query_block":{"table":{"r_rows":2,"r_index_rows":1000,"r_loops":4}}}
                            """;

        // Act
        var work = DensePathNativeWork.MariaDb(plan);

        // Assert
        Assert.Equal(4000D, work.OperatorTuples);
        Assert.Equal((double?)8, work.PostIndexTuples);
        Assert.Null(work.MaterializedOutput);
        Assert.Equal(0, work.UnknownMaterializedOutputs);
    }

    /// <summary>Input observations cannot stand in for unexposed materialized output.</summary>
    [Fact]
    public void UnexposedMaterializedOutputRemainsUnknown()
    {
        // Arrange
        const string plan = """
                            {"query_block":{"table":{"r_rows":300000,"r_loops":1,
                              "materialized":{"query_block":{"r_loops":1,"table":{"r_rows":100,"r_loops":1}}}}}}
                            """;

        // Act
        var work = DensePathNativeWork.MariaDb(plan);

        // Assert
        Assert.Equal(300000D, work.OperatorTuples);
        Assert.Null(work.MaterializedOutput);
        Assert.Equal(1, work.UnknownMaterializedOutputs);
    }

    /// <summary>An unexposed relation cannot erase a separate measured output from the work guard.</summary>
    [Fact]
    public void UnknownMaterializationDoesNotHideKnownOutput()
    {
        // Arrange
        const string plan = """
                            {"query_block":{"tables":[
                              {"r_rows":1,"r_loops":1,"materialized":{"query_block":{"r_loops":1,
                                "filesort":{"r_output_rows":25001,"temporary_table":{}}}}},
                              {"r_rows":1,"r_loops":1,"materialized":{"query_block":{"r_loops":1,
                                "table":{"r_rows":10,"r_loops":1}}}}
                            ]}}
                            """;

        // Act
        var work = DensePathNativeWork.MariaDb(plan);

        // Assert
        Assert.Equal((double?)25001, work.MaterializedOutput);
        Assert.Equal(1, work.UnknownMaterializedOutputs);
    }

    /// <summary>Unexecuted native sorts retain unknown output instead of fabricating zero stored rows.</summary>
    [Fact]
    public void NeverExecutedMaterializedSortRemainsUnknown()
    {
        // Arrange
        const string plan = """
                            {"query_block":{"table":{"r_rows":1,"r_loops":1,
                              "materialized":{"query_block":{"filesort":{"r_loops":null,"r_output_rows":null,
                                "temporary_table":{"table":{"r_rows":null,"r_loops":0}}}}}}}}
                            """;

        // Act
        var work = DensePathNativeWork.MariaDb(plan);

        // Assert
        Assert.Equal(1D, work.OperatorTuples);
        Assert.Equal((double?)1, work.PostIndexTuples);
        Assert.Null(work.MaterializedOutput);
        Assert.Equal(1, work.UnknownMaterializedOutputs);
        Assert.Equal(1, work.ExecutedOperators);
    }

    /// <summary>A nonterminal sort cannot prove how many rows its enclosing materialization stores.</summary>
    [Fact]
    public void NonterminalSortOutputRemainsUnknown()
    {
        // Arrange
        const string plan = """
                            {"query_block":{"table":{"r_rows":300000,"r_loops":1,
                              "materialized":{"query_block":{"r_loops":1,"filesort":{"r_output_rows":25001,
                                "table":{"r_rows":300000,"r_loops":1}}}}}}}
                            """;

        // Act
        var work = DensePathNativeWork.MariaDb(plan);

        // Assert
        Assert.Null(work.MaterializedOutput);
        Assert.Equal(1, work.UnknownMaterializedOutputs);
    }

    /// <summary>Missing or malformed counters cannot silently reduce measured native work.</summary>
    /// <param name="plan">The native document with incomplete or invalid actual execution metadata.</param>
    [Theory]
    [InlineData("{\"query_block\":{\"table\":{\"r_rows\":10}}}")]
    [InlineData("{\"query_block\":{\"table\":{\"r_rows\":10,\"r_loops\":\"1\"}}}")]
    [InlineData("{\"query_block\":{\"table\":{\"r_rows\":10,\"r_loops\":1.5}}}")]
    [InlineData("{\"query_block\":{\"table\":{\"r_rows\":10,\"r_index_rows\":-1,\"r_loops\":1}}}")]
    [InlineData("{\"query_block\":{\"table\":{\"r_index_rows\":10,\"r_loops\":1}}}")]
    [InlineData(
        """
        {"query_block":{"table":{"r_rows":10,"r_loops":1,
          "materialized":{"query_block":{"filesort":{"r_output_rows":2,"temporary_table":{}}}}}}}
        """)]
    [InlineData(
        """
        {"query_block":{"table":{"r_rows":10,"r_loops":1,
          "materialized":{"query_block":{"filesort":{"r_loops":null,"r_output_rows":2,
            "temporary_table":{}}}}}}}
        """)]
    [InlineData(
        """
        {"query_block":{"table":{"r_rows":10,"r_loops":1,
          "materialized":{"query_block":{"filesort":{"r_loops":1,"r_output_rows":null,
            "temporary_table":{}}}}}}}
        """)]
    public void BrokenActualMetadataFailsClosed(
        string plan
    )
    {
        // Arrange
        var read = () => DensePathNativeWork.MariaDb(plan);

        // Act
        var error = Record.Exception(() => read());

        // Assert
        Assert.IsType<FormatException>(error);
    }

    /// <summary>Malformed JSON is rejected instead of producing zero work and a false passing qualification.</summary>
    [Fact]
    public void MalformedNativePlanFailsClosed()
    {
        // Arrange
        const string plan = "{\"query_block\":";

        // Act
        var error = Record.Exception(() => DensePathNativeWork.MariaDb(plan));

        // Assert
        Assert.IsAssignableFrom<JsonException>(error);
    }

    /// <summary>An estimated-only document cannot masquerade as executed native work.</summary>
    [Fact]
    public void EstimatedOnlyNativePlanFailsClosed()
    {
        // Arrange
        const string plan = "{\"query_block\":{\"table\":{\"rows\":100000,\"loops\":1}}}";

        // Act
        var error = Record.Exception(() => DensePathNativeWork.MariaDb(plan));

        // Assert
        Assert.IsType<FormatException>(error);
    }
}
