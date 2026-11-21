using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ManagedSharingCaptureTests
{
    /// <summary>Marks one genuine write while preserving the other callback entries' independent states.</summary>
    private static void MarkWrite(
        IUpdateEntry entry,
        EntityState state
    )
    {
        if (state == EntityState.Modified)
        {
            entry
                .ToEntityEntry()
                .Property(nameof(SharingRoot.Payload))
                .IsModified = true;
        }
        else
        {
            entry.ToEntityEntry()
                .State = state;
        }
    }

    /// <summary>Runs EF's own public command preparation against an independent matching tracker.</summary>
    private static RowIdentity[] PrepareOracleEntries(
        SharingContext context,
        IUpdateEntry[] sources
    )
    {
        var writes = sources.ToList();
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        // WHY: EF's shared-row promotion depends on exact runtime metadata, including inherited aliases.
        // Prepare real commands in a separate context so the expected set is independent of our observer.
        var batches = context
            .GetService<ICommandBatchPreparer>()
            .BatchCommands(writes, updates)
            .ToArray();

        Assert.NotEmpty(batches);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        return writes
            .Select(Identity)
            .ToArray();
    }

    /// <summary>Compares native entities across independent trackers using this fixture's integer keys.</summary>
    private static RowIdentity Identity(
        IUpdateEntry entry
    )
        => new(
            entry.EntityType.Name,
            entry.GetCurrentValue<int>(entry.EntityType.FindPrimaryKey()!.Properties.Single()));

    /// <summary>Captures native scalar state before the read-only sharing traversal.</summary>
    private static NativeState[] CaptureState(
        SharingContext context
    )
        => context
            .ChangeTracker
            .Entries()
            .Select(entity =>
            {
                var entry = NestedSetInsertionContract.Instance.Entry(entity);
                var properties = entry
                    .EntityType
                    .GetProperties()
                    .ToArray();

                return new NativeState(
                    entry,
                    entry.EntityState,
                    properties,
                    properties
                        .Select(entry.GetCurrentValue)
                        .ToArray(),
                    properties
                        .Select(entry.GetOriginalValue)
                        .ToArray(),
                    properties
                        .Select(entry.IsModified)
                        .ToArray());
            })
            .ToArray();

    /// <summary>Requires exact native state, scalar values and flags without a detection or I/O side effect.</summary>
    private static void AssertUnchanged(
        SharingContext context,
        NativeState[] before
    )
    {
        foreach (var saved in before)
        {
            Assert.Equal(saved.State, saved.Entry.EntityState);

            // WHY: Pair each captured property with its native current/original/flag values by stable ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < saved.Properties.Length; index++)
            {
                var property = saved.Properties[index];
                Assert.Equal(saved.Current[index], saved.Entry.GetCurrentValue(property));
                Assert.Equal(saved.Original[index], saved.Entry.GetOriginalValue(property));
                Assert.Equal(saved.Modified[index], saved.Entry.IsModified(property));
            }
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Changes a real CLR payload without invoking EF's current-value setter.</summary>
    private static void SetRawPayload(
        object entity,
        string value
    )
    {
        switch (entity)
        {
            case SharingRoot root:       root.Payload = value; break;
            case SharingDetails details: details.Payload = value; break;
            default:                     throw new ArgumentOutOfRangeException(nameof(entity));
        }
    }

    /// <summary>Retains independent expected values so observation cannot mutate its own test oracle.</summary>
    private sealed record NativeState(
        IUpdateEntry Entry,
        EntityState State,
        IProperty[] Properties,
        object?[] Current,
        object?[] Original,
        bool[] Modified
    );

    /// <summary>Identifies one independently tracked row using its exact entity mapping and primary key.</summary>
    private readonly record struct RowIdentity(
        string Mapping,
        int Key
    );
}
