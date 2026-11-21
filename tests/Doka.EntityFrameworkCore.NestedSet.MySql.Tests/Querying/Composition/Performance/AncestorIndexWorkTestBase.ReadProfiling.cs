using System.Globalization;
using System.Security.Cryptography;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    // WHY: Separate databases allow fixed identities without collisions. Printable provider-bound bytes
    // preserve valid native JSON plans while making physical rows identical across paired assemblies.
    private static readonly Guid s_consumerScope = new("61616161-6161-6161-6161-616161616161");
    private static readonly Guid s_consumerTree = new("62626262-6262-6262-6262-626262626262");

    /// <summary>Records native schema and ordered logical row proofs outside every measured read window.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static async Task<ConsumerPhysicalTable[]> ConsumerPhysicalTablesAsync(
        AncestorIndexQueryContext context,
        CancellationToken cancellationToken
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

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                Assert.True(await reader.ReadAsync(cancellationToken));
                definition = reader.GetString(1);
            }

            // WHY: Keep EXTENDED's row-by-row native diagnostics before and after the logical scan.
            // Pair equality uses the ordered logical proof; native record checksums are not that proof.
            command.CommandText = "CHECKSUM TABLE `" + name + "` EXTENDED";
            string checksum;

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                Assert.True(await reader.ReadAsync(cancellationToken));
                Assert.False(await reader.IsDBNullAsync(1, cancellationToken));
                checksum = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!;
            }

            var logicalRows = await ConsumerLogicalRowsAsync(context, name, cancellationToken: cancellationToken);

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                Assert.True(await reader.ReadAsync(cancellationToken));
                Assert.False(await reader.IsDBNullAsync(1, cancellationToken));
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
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="table">The physical table name.</param>
    private static async Task<ConsumerLogicalRows> ConsumerLogicalRowsAsync(
        AncestorIndexQueryContext context,
        string table,
        CancellationToken cancellationToken
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var row = new byte[32];
        long count = 0;

        while (await reader.ReadAsync(cancellationToken))
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
