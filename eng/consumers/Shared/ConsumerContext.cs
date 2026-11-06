using System;
using System.Threading;
using System.Threading.Tasks;
using Doka.EntityFrameworkCore.NestedSet;
using Microsoft.EntityFrameworkCore;

namespace Doka.NestedSet.PackageConsumers;

/// <summary>Exercises shipping mapping and query contracts without source project references.</summary>
/// <param name="options">The selected native provider and candidate adapter options.</param>
internal sealed class ConsumerContext(DbContextOptions options) : NestedSetDbContext(options)
{
    /// <summary>Checks public registration, mapping, and SQL translation without opening a server connection.</summary>
    /// <param name="options">Options configured through the selected native provider callback.</param>
    /// <param name="cancellationToken">The caller's token for verification and console output.</param>
    internal static async Task VerifyQueryAsync(
        DbContextOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var context = new ConsumerContext(options);
        var sql = context.NestedSet<Node>().InTree(Guid.Empty).Nodes.ToQueryString();

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException("Candidate adapter model/query translation is empty.");
        }

        await Console.Out.WriteLineAsync(
            "Candidate adapter public contracts verified.".AsMemory(),
            cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<Node>();
        node.HasKey(value => value.Id);
        node.Property(value => value.Name).HasMaxLength(120);
        node.HasNestedSet(value => value.HasParent(item => item.ParentId));
    }
}
