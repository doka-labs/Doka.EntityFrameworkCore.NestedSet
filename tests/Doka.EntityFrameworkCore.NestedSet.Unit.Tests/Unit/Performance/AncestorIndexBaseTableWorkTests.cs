namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Rejects false base-table attribution and incomplete native observations in the fixed ancestor fixture.</summary>
public sealed class AncestorIndexBaseTableWorkTests
{
    /// <summary>Retains actual index/table leaves while excluding parent filters and temporary C lookups.</summary>
    [Fact]
    public void MySqlSeparatesBaseAccessFromTemporaryAndParentTuples()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a` JOIN `AncestorIndexQueryNodes` AS `a4` ON 1=1";
        const string plan = "-> Filter: (a4.Id > 0) (actual time=0..1 rows=100000000 loops=1)\n"
            + " -> Table scan on u (actual time=0..1 rows=100000000 loops=1)\n"
            + " -> Covering index range scan on a (actual time=0..1 rows=1000 loops=1)\n"
            + " -> Single-row index lookup on a4 using PRIMARY (actual time=0..1 rows=1 loops=1000)";

        // Act
        var work = AncestorIndexBaseTableWork.Read(sql, plan, "MySql");

        // Assert
        Assert.Equal(2_000, work.ObservedTuples);
        Assert.Equal(2, work.PhysicalAccesses);
        Assert.Equal(2, work.UnexposedPreIndexAccesses);
    }

    /// <summary>Counts repeated native range access even when the chosen index name is not displayed.</summary>
    [Fact]
    public void MySqlRetainsRepeatedBaseRangeScans()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a4`";
        const string plan = "-> Index range scan on a4 (re-planned for each iteration) "
            + "(actual time=0..1 rows=36382 loops=2000)";

        // Act
        var work = AncestorIndexBaseTableWork.Read(sql, plan, "MySql");

        // Assert
        Assert.Equal(72_764_000, work.ObservedTuples);
    }

    /// <summary>Does not attribute a native access to a base alias reused by a derived relation.</summary>
    [Fact]
    public void AmbiguousBaseAndDerivedAliasFailsClosed()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a` JOIN (SELECT 1) AS `a` ON 1=1";
        const string plan = "-> Table scan on a (actual time=0..1 rows=1 loops=1)";

        // Act
        var exception = Record.Exception(() => AncestorIndexBaseTableWork.Read(sql, plan, "MySql"));

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Rejects an estimated physical access rather than treating absent actual work as zero.</summary>
    [Fact]
    public void EstimatedOnlyBaseAccessFailsClosed()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a`";
        const string plan = "-> Table scan on a (cost=1 rows=1)";

        // Act
        var exception = Record.Exception(() => AncestorIndexBaseTableWork.Read(sql, plan, "MySql"));

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Rejects an incomplete physical alias inventory even when another actual access is valid.</summary>
    [Fact]
    public void MissingBaseAccessFailsClosed()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a` JOIN `AncestorIndexQueryNodes` AS `a4` ON 1=1";
        const string plan = "-> Table scan on a (actual time=0..1 rows=1 loops=1)\n"
            + " -> Unsupported scan on a4 (actual time=0..1 rows=100000000 loops=1)";

        // Act
        var exception = Record.Exception(() => AncestorIndexBaseTableWork.Read(sql, plan, "MySql"));

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Uses MariaDB pre-index observations and folds repeated shared-CTE metadata by its unique SQL owner.</summary>
    [Fact]
    public void MariaDbRetainsPreIndexBaseWorkWithoutCountingTemporaryRows()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a`";
        const string plan = "{\"query_block\":{\"select_id\":1,\"nested_loop\":["
            + "{\"table\":{\"table_name\":\"u\",\"r_loops\":1,\"r_rows\":100000000}},"
            + "{\"table\":{\"table_name\":\"a\",\"r_loops\":2,\"r_rows\":3,\"r_index_rows\":11}},"
            + "{\"table\":{\"table_name\":\"a\",\"r_loops\":2,\"r_rows\":3,\"r_index_rows\":11}}]}}";

        // Act
        var work = AncestorIndexBaseTableWork.Read(sql, plan, "MariaDb");

        // Assert
        Assert.Equal(22, work.ObservedTuples);
        Assert.Equal(1, work.PhysicalAccesses);
        Assert.Equal(0, work.UnexposedPreIndexAccesses);
    }

    /// <summary>Does not infer sharing from equal counters when query-block ownership is absent or different.</summary>
    /// <param name="differentBlocks">Whether identical accesses belong to explicitly different SELECT blocks.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MariaDbRepeatedAccessRequiresTheSameKnownQueryBlock(bool differentBlocks)
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a`";
        const string access = "{\"table_name\":\"a\",\"r_loops\":2,\"r_rows\":3,\"r_index_rows\":11}";
        var plan = differentBlocks
            ? "{\"blocks\":[{\"query_block\":{\"select_id\":1,\"table\":" + access
                + "}},{\"query_block\":{\"select_id\":2,\"table\":" + access + "}}]}"
            : "{\"query_block\":{\"nested_loop\":[{\"table\":" + access + "},{\"table\":" + access + "}]}}";

        // Act
        var exception = Record.Exception(() => AncestorIndexBaseTableWork.Read(sql, plan, "MariaDb"));

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Rejects malformed query-block ownership before attributing physical work.</summary>
    /// <param name="identity">The invalid native SELECT identity encoded as JSON.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("\"block\"")]
    [InlineData("9223372036854775808")]
    public void MariaDbInvalidQueryBlockFailsClosed(string identity)
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a`";
        var plan = "{\"query_block\":{\"select_id\":" + identity
            + ",\"table\":{\"table_name\":\"a\",\"r_loops\":1,\"r_rows\":3}}}";

        // Act
        var exception = Record.Exception(() => AncestorIndexBaseTableWork.Read(sql, plan, "MariaDb"));

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Reports missing MariaDB pre-index evidence separately instead of inventing an input count.</summary>
    [Fact]
    public void MariaDbLabelsUnexposedPreIndexWork()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a`";
        const string plan = "{\"table_name\":\"a\",\"r_loops\":2,\"r_rows\":3}";

        // Act
        var work = AncestorIndexBaseTableWork.Read(sql, plan, "MariaDb");

        // Assert
        Assert.Equal(6, work.ObservedTuples);
        Assert.Equal(1, work.UnexposedPreIndexAccesses);
    }

    /// <summary>Does not discard a differing repeated physical observation as shared-CTE metadata.</summary>
    [Fact]
    public void MariaDbConflictingRepeatedAccessFailsClosed()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a`";
        const string plan = "{\"query_block\":{\"nested_loop\":["
            + "{\"table\":{\"table_name\":\"a\",\"r_loops\":1,\"r_rows\":3,\"r_index_rows\":11}},"
            + "{\"table\":{\"table_name\":\"a\",\"r_loops\":2,\"r_rows\":3,\"r_index_rows\":11}}]}}";

        // Act
        var exception = Record.Exception(() => AncestorIndexBaseTableWork.Read(sql, plan, "MariaDb"));

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Does not treat a row estimate without actual loop evidence as completed base access.</summary>
    [Fact]
    public void MariaDbMissingActualLoopsFailsClosed()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a`";
        const string plan = "{\"table_name\":\"a\",\"r_rows\":3}";

        // Act
        var exception = Record.Exception(() => AncestorIndexBaseTableWork.Read(sql, plan, "MariaDb"));

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Accepts an explicitly unused branch without mistaking null row observations for executed work.</summary>
    [Fact]
    public void MariaDbExplicitUnusedAliasCompletesInventory()
    {
        // Arrange
        const string sql = "SELECT * FROM `AncestorIndexQueryNodes` AS `a` JOIN `AncestorIndexQueryNodes` AS `a4` ON 1=1";
        const string plan = "{\"query_block\":{\"nested_loop\":["
            + "{\"table\":{\"table_name\":\"a\",\"r_loops\":1,\"r_rows\":3}},"
            + "{\"table\":{\"table_name\":\"a4\",\"r_loops\":0,\"r_rows\":null,\"r_index_rows\":null}}]}}";

        // Act
        var work = AncestorIndexBaseTableWork.Read(sql, plan, "MariaDb");

        // Assert
        Assert.Equal(3, work.ObservedTuples);
        Assert.Equal(1, work.PhysicalAccesses);
    }
}
