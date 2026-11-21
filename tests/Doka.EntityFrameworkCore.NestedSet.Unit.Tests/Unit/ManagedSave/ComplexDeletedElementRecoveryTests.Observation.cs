namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexDeletedElementRecoveryTests
{
    /// <summary>Creates a persisted single-row store and a fresh context sharing that connection.</summary>
    /// <param name="seed">The independent payload persisted before the recovery scenario.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static Task<DeletedContext> CreateAsync(
        Payload seed,
        CancellationToken cancellationToken
    )
        => CreateAsync(seed, new SqliteConnection("Data Source=:memory:"), cancellationToken: cancellationToken);

    /// <summary>Owns the supplied connection until initialization transfers it to the returned context.</summary>
    /// <param name="seed">The single payload to persist before recovery begins.</param>
    /// <param name="connection">The connection whose ownership transfers to this factory on entry.</param>
    /// <returns>A fresh context that owns the initialized connection.</returns>
    /// <exception cref="AggregateException">
    /// Initialization and cleanup both fail; the initialization error is first.
    /// </exception>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task<DeletedContext> CreateAsync(
        Payload seed,
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using (var context = new DeletedContext(connection, ownsConnection: false))
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
                context.Add(seed);
                await context.SaveChangesAsync(cancellationToken);
            }

            return new DeletedContext(connection, ownsConnection: true);
        }
        catch (Exception initializationFailure)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(initializationFailure, cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>Reads stored JSON without tracking anything or reusing the context's change tracker.</summary>
    /// <param name="context">The context used by this fixture operation.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task<string> StoredRowsAsync(
        DeletedContext context,
        CancellationToken cancellationToken
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT \"Rows\" FROM \"Payload\"";

        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Observes both cached native lanes at every level, including holes, independent of recovery.</summary>
    private static string Lanes(
        EntityEntry<Payload> entry
    )
    {
        var rows = entry.Metadata.FindComplexProperty(nameof(Payload.Rows))!;

        return Lanes(NestedSetComplexEntryAccess.Owner(entry.Property(value => value.Id)), rows);
    }

    /// <summary>Renders one owner's current and original lanes recursively.</summary>
    private static string Lanes(
        object owner,
        IComplexProperty property
    )
    {
        List<string> parts = [];

        foreach (var original in new[] { false, true })
        {
            var entries = NestedSetComplexEntryAccess.Entries(owner, property, original);
            parts.Add(
                (original
                    ? "o"
                    : "c")
                + entries.Count
                + "["
                + string.Join(
                    ",",
                    entries
                        .Cast<object?>()
                        .Select(native => native is null
                            ? "hole"
                            : Describe(native, property)))
                + "]");
        }

        return property.Name + ":" + string.Join("/", parts);
    }

    /// <summary>Renders one native element's identity-free lifecycle, positions and nested lanes.</summary>
    private static string Describe(
        object native,
        IComplexProperty property
    )
        => NestedSetComplexEntryAccess.State(native)
            + "@"
            + NestedSetComplexEntryAccess.Ordinal(native, false)
            + "/"
            + NestedSetComplexEntryAccess.Ordinal(native, true)
            + string.Concat(
                property
                    .ComplexType
                    .GetComplexProperties()
                    .Where(nested => nested.IsCollection)
                    .Select(nested => Lanes(native, nested)));

    /// <summary>Gets a first-level original element entry through EF's public API.</summary>
    private static ComplexElementEntry Original(
        EntityEntry<Payload> entry,
        int ordinal
    )
        => entry
            .ComplexCollection(nameof(Payload.Rows))
            .GetOriginalEntry(ordinal);

    /// <summary>Gets a second-level current element entry through EF's public API.</summary>
    private static ComplexElementEntry Current(
        EntityEntry<Payload> entry,
        int outer,
        int middle
    )
        => entry
            .ComplexCollection(nameof(Payload.Rows))[outer]
            .ComplexCollection(nameof(Outer.Middles))[middle];
}
