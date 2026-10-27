using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    // WHY: Separate databases allow fixed identities without collisions. Printable provider-bound bytes
    // preserve valid native JSON plans while making physical rows identical across paired assemblies.
    private static readonly Guid s_consumerScope = new("61616161-6161-6161-6161-616161616161");
    private static readonly Guid s_consumerTree = new("62626262-6262-6262-6262-626262626262");

    /// <summary>Reads the diagnostic runner's opt-in without changing ordinary qualification windows.</summary>
    private static bool ConsumerAllocationProfilingEnabled()
    {
        var value = Environment.GetEnvironmentVariable("DOKA_NESTEDSET_ALLOCATION_PROFILE");

        if (value is not null and not "1")
        {
            throw new InvalidOperationException(
                "Set DOKA_NESTEDSET_ALLOCATION_PROFILE to 1 for diagnostic sampling, or leave it unset.");
        }

        return value == "1";
    }

    /// <summary>Records native schema and ordered logical row proofs outside every measured read window.</summary>
    private static async Task<ConsumerPhysicalTable[]> ConsumerPhysicalTablesAsync(
        AncestorIndexQueryContext context
    )
    {
        string[] names = ["AncestorIndexQueryNodes", "AncestorIndexQueryItems"];
        var tables = new ConsumerPhysicalTable[names.Length];

        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            await using var command = context
                .Database
                .GetDbConnection()
                .CreateCommand();

            command.CommandText = "SHOW CREATE TABLE `" + name + "`";
            string definition;

            await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
            {
                Assert.True(await reader.ReadAsync(CancellationToken.None));
                definition = reader.GetString(1);
            }

            // WHY: Keep EXTENDED's row-by-row native diagnostics before and after the logical scan.
            // Pair equality uses the ordered logical proof; native record checksums are not that proof.
            command.CommandText = "CHECKSUM TABLE `" + name + "` EXTENDED";
            string checksum;

            await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
            {
                Assert.True(await reader.ReadAsync(CancellationToken.None));
                Assert.False(await reader.IsDBNullAsync(1, CancellationToken.None));
                checksum = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!;
            }

            var logicalRows = await ConsumerLogicalRowsAsync(context, name);

            await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
            {
                Assert.True(await reader.ReadAsync(CancellationToken.None));
                Assert.False(await reader.IsDBNullAsync(1, CancellationToken.None));
                var checksumAfterRead = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!;
                tables[index] = new ConsumerPhysicalTable(
                    name,
                    definition,
                    checksum,
                    checksumAfterRead,
                    logicalRows.Count,
                    logicalRows.Sha256);
            }
        }

        return tables;
    }

    /// <summary>Folds every ordered row and column into a cryptographic proof with constant retained memory.</summary>
    private static async Task<ConsumerLogicalRows> ConsumerLogicalRowsAsync(
        AncestorIndexQueryContext context,
        string table
    )
    {
        var columns = table switch
        {
            "AncestorIndexQueryNodes" => "HEX(`Scope`),`Id`,HEX(`TreeId`),`ParentId`,`Left`,`Right`,"
                + "`Depth`,`Position`,`Visible`,HEX(`Details_Caption`),`NestedSetIsNonLeaf`",
            "AncestorIndexQueryItems" => "HEX(`Scope`),`Id`,`NodeId`,HEX(`Label`)",
            _ => throw new InvalidOperationException("The logical row proof requires an explicitly known table."),
        };

        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        // WHY: Native JSON preserves nulls and numeric types; HEX preserves exact binary/text bytes.
        // Fixed 32-byte row digests and the complete primary-key order avoid delimiter ambiguity,
        // population-sized managed buffers and the native table checksum's row-representation behavior.
        command.CommandText = "SELECT UNHEX(SHA2(JSON_ARRAY("
            + columns
            + "),256)) FROM `"
            + table
            + "` ORDER BY `Scope`,`Id`";

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var row = new byte[32];
        long count = 0;

        while (await reader.ReadAsync(CancellationToken.None))
        {
            // WHY: ReadAsync fetches the row. GetBytes copies its already buffered fixed-size digest
            // without allocating an array or requesting synchronous network I/O for each row.
            if (reader.GetBytes(0, 0, null, 0, 0) != row.Length
                || reader.GetBytes(0, 0, row, 0, row.Length) != row.Length)
            {
                throw new InvalidOperationException("The native logical row digest must contain exactly 32 bytes.");
            }

            hash.AppendData(row);
            count++;
        }

        return new ConsumerLogicalRows(count, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    /// <summary>Separates capture overhead from warm complete reads without replacing qualified observations.</summary>
    private async Task ObserveConsumerReadsWithoutCaptureAsync(
        AncestorIndexQueryContext observedContext,
        EnterpriseProbe originalCapture,
        Func<AncestorIndexQueryContext, IQueryable<ConsumerFactPair>> build,
        ConsumerFactPair[] expected,
        string profile,
        int readWarmups,
        int measuredReads
    )
    {
        var connectionString = observedContext.Database.GetConnectionString()!;
        var options = ModelCompatibilityDatabase.Options<AncestorIndexQueryContext>(Engine, connectionString);

        // WHY: Keep the same mapped database, query and full-result oracle. Removing test capture only
        // estimates observer overhead; these samples cannot be reported as product allocation improvements.
        await using var context = new AncestorIndexQueryContext(options);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var query = build(context);

        // WHY: Retain the same asynchronous read wrapper in both windows. The original capture is
        // consulted only on failure; it is never registered on this observer-light context.
        var lightProfile = profile + "-observer-light";

        for (var warmup = 0; warmup < readWarmups; warmup++)
        {
            var actual = await ReadConsumerFactsAsync(context, originalCapture, query, lightProfile);
            RequireConsumerOracle(actual, expected);
        }

        var reads = new ConsumerReadSample[measuredReads];
        var profileEvents = NativeReadProfileEvents.Log;

        for (var sample = 0; sample < reads.Length; sample++)
        {
            profileEvents.BeginRead(Engine, profile, "observer-light", sample);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.GetTimestamp();
            var actual = await ReadConsumerFactsAsync(context, originalCapture, query, lightProfile);
            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            profileEvents.EndRead(Engine, profile, "observer-light", sample);
            RequireConsumerOracle(actual, expected);
            reads[sample] = new ConsumerReadSample(elapsed.TotalMilliseconds, allocated);
        }

        var summary = JsonSerializer.Serialize(
            new
            {
                Engine,
                Profile = profile,
                Phase = "observer-light",
                Meaning = "Separate attribution only; not the fully observed qualification or a product gain.",
                ReadWarmups = readWarmups,
                MeasuredReads = measuredReads,
                ExpectedRows = expected.Length,
                Reads = reads,
            },
            s_consumerEvidenceJson);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-ancestor-consumer-" + profile + "-observer-light",
            [summary]);
    }

    /// <summary>
    /// Retains schema, complete logical row proofs and separately labeled native checksum diagnostics.
    /// </summary>
    private sealed record ConsumerPhysicalTable(
        string Name,
        string Definition,
        string ExtendedRowChecksum,
        string ExtendedRowChecksumAfterLogicalRead,
        long LogicalRowCount,
        string LogicalRowsSha256
    );

    /// <summary>Retains the count and ordered digest for one complete logical table scan.</summary>
    private sealed record ConsumerLogicalRows(
        long Count,
        string Sha256
    );
}
