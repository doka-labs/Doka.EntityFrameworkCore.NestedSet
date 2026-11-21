namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingNotificationTests
{
    /// <summary>Compares tracked coordinates with an independent database read and verifies pending flags.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The database read through an independent verification context.</param>
    /// <param name="context">The context whose caller-owned state must match the acceptance mode.</param>
    /// <param name="accepted">Whether the successful save accepted the caller's pending changes.</param>
    /// <returns>A task that completes after all persisted and tracked assertions.</returns>
    private static async Task AssertSuccessfulSaveAsync(
        TestDatabase database,
        OrderingNotificationContext context,
        bool accepted,
        CancellationToken cancellationToken
    )
    {
        var nodes = context
            .Set<OrderingNotificationNode>()
            .Local
            .OrderBy(node => node.Id)
            .ToArray();

        var payload = context
            .Set<OrderingNotificationPayload>()
            .Local
            .Single();

        var renamed = nodes.Single(node => node.Id == 2);
        await using var verification = await CreateContextAsync(database, cancellationToken: cancellationToken);
        var persisted = await verification
            .Set<OrderingNotificationNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(cancellationToken);

        Assert.Equal(
            new[]
            {
                (1, 1L, 8L, 0, 0L), (2, 4L, 7L, 1, 1L), (3, 2L, 3L, 1, 0L), (4, 5L, 6L, 2, 0L),
            },
            Coordinates(persisted));

        Assert.Equal(Coordinates(persisted), Coordinates(nodes));
        Assert.Equal(
            "Zulu",
            persisted.Single(node => node.Id == 2)
                .Name);

        Assert.Equal(
            accepted
                ? EntityState.Unchanged
                : EntityState.Modified,
            context.Entry(renamed)
                .State);

        Assert.Equal(
            !accepted,
            context
                .Entry(renamed)
                .Property(node => node.Name)
                .IsModified);

        Assert.Equal(
            accepted
                ? EntityState.Unchanged
                : EntityState.Modified,
            context.Entry(payload)
                .State);

        Assert.Equal(
            !accepted,
            context
                .Entry(payload)
                .Property(row => row.Value)
                .IsModified);

        Assert.All(
            nodes.Where(node => node.Id != 2),
            node => Assert.Equal(
                EntityState.Unchanged,
                context.Entry(node)
                    .State));

        Assert.Equal("pending application change", payload.Value);
        Assert.Equal(
            "pending application change",
            await verification
                .Set<OrderingNotificationPayload>()
                .Select(row => row.Value)
                .SingleAsync(cancellationToken));

        AssertUnavailableOriginals(context, nodes, payload);
        var tree = verification
            .NestedSet<OrderingNotificationNode>()
            .ForScope(1);

        var report = await tree
            .InTree(persisted[0].TreeId)
            .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

        Assert.True(report.IsValid);
    }

    /// <summary>Proves the scenario has no original-value storage and leaves managed coordinates unmodified.</summary>
    /// <param name="context">The context whose actual public entry behavior is inspected.</param>
    /// <param name="nodes">The tracked hierarchy nodes.</param>
    /// <param name="payload">The unrelated notification-tracked application entity.</param>
    private static void AssertUnavailableOriginals(
        OrderingNotificationContext context,
        IEnumerable<OrderingNotificationNode> nodes,
        OrderingNotificationPayload payload
    )
    {
        foreach (var node in nodes)
        {
            var entry = context.Entry(node);
            foreach (var name in new[] { "Left", "Right", "Depth", "Position" })
            {
                // WHY: This negative control fails if the model accidentally falls back to snapshot tracking.
                Assert.Throws<InvalidOperationException>(() => entry.Property(name)
                    .OriginalValue);

                Assert.False(
                    entry.Property(name)
                        .IsModified);
            }

            Assert.Throws<InvalidOperationException>(() => entry.Property(value => value.Name)
                .OriginalValue);
        }

        Assert.Throws<InvalidOperationException>(() => context
            .Entry(payload)
            .Property(row => row.Value)
            .OriginalValue);
    }
}
