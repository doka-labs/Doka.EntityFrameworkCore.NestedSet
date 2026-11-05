using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects exact same-row callback capture without detecting unrelated caller changes.</summary>
public sealed partial class ManagedSharingCaptureTests
{
    /// <summary>Every write direction captures exactly the peers promoted by EF's actual command preparation.</summary>
    /// <param name="seedIndex">The main, owned, nested or split entry whose payload is a callback write.</param>
    /// <param name="derived">Whether the main entity uses its inherited TPH row mapping.</param>
    /// <param name="state">The actual pending write state retained in the callback list.</param>
    [Theory]
    [InlineData(0, false, EntityState.Modified)]
    [InlineData(1, false, EntityState.Modified)]
    [InlineData(2, false, EntityState.Modified)]
    [InlineData(3, false, EntityState.Modified)]
    [InlineData(0, true, EntityState.Modified)]
    [InlineData(1, true, EntityState.Modified)]
    [InlineData(2, true, EntityState.Modified)]
    [InlineData(3, true, EntityState.Modified)]
    [InlineData(1, false, EntityState.Added)]
    [InlineData(1, false, EntityState.Deleted)]
    [InlineData(3, false, EntityState.Added)]
    [InlineData(3, false, EntityState.Deleted)]
    public async Task PendingWriteCapturesExactUnchangedPhysicalRowClosure(
        int seedIndex,
        bool derived,
        EntityState state
    )
    {
        // Arrange
        await using var context = new SharingContext();
        await using var oracleContext = new SharingContext();
        var root = CreateRoot(1, derived);
        var unrelated = CreateRoot(2, derived);
        var jsonOwner = new JsonOwner
        {
            Id = 1,
            Document = new JsonPayload { Payload = "JSON payload" },
        };

        context.AttachRange(root, unrelated, jsonOwner);
        var sharing = SharedEntries(context, root);
        var seed = sharing[seedIndex];
        MarkWrite(seed, state);
        var rawPeer = seedIndex == 0
            ? Entry(context, root.Details)
            : sharing[0];

        // WHY: A CLR-only mutation must remain unmarked; a PropertyEntry setter would change the input contract.
        SetRawPayload(
            rawPeer.ToEntityEntry()
                .Entity,
            "Raw unmarked caller payload");

        var before = CaptureState(context);
        var oracleRoot = CreateRoot(1, derived);
        oracleContext.AttachRange(oracleRoot, CreateRoot(2, derived), new JsonOwner { Id = 1 });
        var oracleSharing = SharedEntries(oracleContext, oracleRoot);
        var oracleSeed = oracleSharing[seedIndex];
        MarkWrite(oracleSeed, state);
        var oracleRawPeer = seedIndex == 0
            ? oracleRoot.Details
            : (object)oracleRoot;

        SetRawPayload(oracleRawPeer, "Raw unmarked caller payload");
        var expected = PrepareOracleEntries(oracleContext, [oracleSeed]);
        var callbacks = new List<IUpdateEntry> { seed };

        // Act
        NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);

        // Assert
        Assert.Same(seed, callbacks[0]);
        Assert.Equal(expected.Length, callbacks.Count);

        foreach (var entry in expected)
        {
            Assert.Single(callbacks, value => Identity(value) == entry);
        }

        Assert.DoesNotContain(
            callbacks,
            value => ReferenceEquals(
                value.ToEntityEntry()
                    .Entity,
                root.Separate));

        Assert.DoesNotContain(
            callbacks,
            value => ReferenceEquals(
                value.ToEntityEntry()
                    .Entity,
                jsonOwner.Document));

        Assert.DoesNotContain(
            callbacks,
            value => ReferenceEquals(
                value.ToEntityEntry()
                    .Entity,
                root.Related[0]));

        Assert.DoesNotContain(
            callbacks,
            value => ReferenceEquals(
                value.ToEntityEntry()
                    .Entity,
                unrelated));

        Assert.Equal(
            "Raw unmarked caller payload",
            rawPeer.GetCurrentValue(
                rawPeer.EntityType.FindProperty(nameof(SharingRoot.Payload))!));

        AssertUnchanged(context, before);
    }

    /// <summary>A peer already owning pending work or no longer tracked is never appended as unchanged.</summary>
    /// <param name="peerState">The independently selected peer's preexisting state.</param>
    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    [InlineData(EntityState.Detached)]
    public async Task ExistingPeerWriteStateIsNotPromotedIntoCallbackCapture(
        EntityState peerState
    )
    {
        // Arrange
        await using var context = new SharingContext();
        var root = CreateRoot(1);
        context.Attach(root);
        var sharing = SharedEntries(context, root);
        sharing[0]
            .ToEntityEntry()
            .Property(nameof(SharingRoot.Payload))
            .IsModified = true;

        var excluded = sharing[3];
        excluded.ToEntityEntry()
            .State = peerState;

        var callbacks = new List<IUpdateEntry> { sharing[0] };
        var before = CaptureState(context);

        // Act
        NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);

        // Assert
        Assert.Same(sharing[0], callbacks[0]);
        Assert.Equal(3, callbacks.Count);
        Assert.DoesNotContain(callbacks, value => ReferenceEquals(value, excluded));
        Assert.Single(callbacks, value => ReferenceEquals(value, sharing[1]));
        Assert.Single(callbacks, value => ReferenceEquals(value, sharing[2]));
        Assert.Equal(peerState, excluded.EntityState);
        AssertUnchanged(context, before);
    }

    /// <summary>Two actual writes into one shared row append each remaining unchanged peer exactly once.</summary>
    [Fact]
    public async Task ConvergingWriteAliasesPreservePrefixWithoutDuplicatePeers()
    {
        // Arrange
        await using var context = new SharingContext();
        var root = CreateRoot(1);
        context.Attach(root);
        var sharing = SharedEntries(context, root);
        sharing[0]
            .ToEntityEntry()
            .Property(nameof(SharingRoot.Payload))
            .IsModified = true;

        sharing[3]
            .ToEntityEntry()
            .Property(nameof(SharingRoot.Payload))
            .IsModified = true;

        var callbacks = new List<IUpdateEntry>
        {
            sharing[3],
            sharing[0],
        };

        var before = CaptureState(context);

        // Act
        NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);

        // Assert
        Assert.Equal(4, callbacks.Count);
        Assert.Same(sharing[3], callbacks[0]);
        Assert.Same(sharing[0], callbacks[1]);

        foreach (var entry in sharing)
        {
            Assert.Single(callbacks, value => ReferenceEquals(value, entry));
        }

        AssertUnchanged(context, before);
    }

    /// <summary>Separate-table ownership and ordinary FKs do not imply a promoted shared-row entry.</summary>
    /// <param name="shape">The separate-table owned or regular-FK write mapping.</param>
    [Theory]
    [InlineData("separate")]
    [InlineData("regular")]
    public async Task NonSharingWriteRetainsExactCallbackList(
        string shape
    )
    {
        // Arrange
        await using var context = new SharingContext();
        var root = CreateRoot(1);
        var jsonOwner = new JsonOwner
        {
            Id = 1,
            Document = new JsonPayload { Payload = "JSON payload" },
        };

        context.AttachRange(root, jsonOwner);
        var entity = shape switch
        {
            "separate" => (object)root.Separate,
            "regular" => root.Related[0],
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var entry = Entry(context, entity);
        entry
            .ToEntityEntry()
            .Property(nameof(SharingRoot.Payload))
            .IsModified = true;

        var callbacks = new List<IUpdateEntry> { entry };
        var before = CaptureState(context);

        // Act
        NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);

        // Assert
        Assert.Same(entry, Assert.Single(callbacks));
        AssertUnchanged(context, before);
    }

    /// <summary>A modified JSON-owned entry captures the owner EF promotes to update the JSON column.</summary>
    [Fact]
    public async Task JsonPayloadWriteCapturesItsPromotedOwner()
    {
        // Arrange
        await using var context = new SharingContext();
        await using var oracleContext = new SharingContext();
        var owner = new JsonOwner { Id = 1 };
        var oracleOwner = new JsonOwner { Id = 1 };
        context.Attach(owner);
        oracleContext.Attach(oracleOwner);
        var source = Entry(context, owner.Document);
        var promotedOwner = Entry(context, owner);
        var oracleSource = Entry(oracleContext, oracleOwner.Document);
        MarkWrite(source, EntityState.Modified);
        MarkWrite(oracleSource, EntityState.Modified);
        var expected = PrepareOracleEntries(oracleContext, [oracleSource]);
        var callbacks = new List<IUpdateEntry> { source };
        var before = CaptureState(context);

        // Act
        NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);

        // Assert
        Assert.Equal(2, expected.Length);
        Assert.Equal(expected.Length, callbacks.Count);
        Assert.Same(source, callbacks[0]);
        Assert.Single(callbacks, value => ReferenceEquals(value, promotedOwner));

        foreach (var entry in expected)
        {
            Assert.Single(callbacks, value => Identity(value) == entry);
        }

        AssertUnchanged(context, before);
    }

    /// <summary>An owner's scalar write never captures an unchanged JSON-owned entry as a promoted table row.</summary>
    [Fact]
    public async Task JsonOwnerWriteDoesNotCaptureUnchangedDocument()
    {
        // Arrange
        await using var context = new SharingContext();
        await using var oracleContext = new SharingContext();
        var owner = new JsonOwner { Id = 1 };
        var oracleOwner = new JsonOwner { Id = 1 };
        context.Attach(owner);
        oracleContext.Attach(oracleOwner);
        var source = Entry(context, owner);
        var document = Entry(context, owner.Document);
        var oracleSource = Entry(oracleContext, oracleOwner);
        MarkWrite(source, EntityState.Modified);
        MarkWrite(oracleSource, EntityState.Modified);
        var expected = PrepareOracleEntries(oracleContext, [oracleSource]);
        var callbacks = new List<IUpdateEntry> { source };
        var before = CaptureState(context);

        // Act
        NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);

        // Assert
        Assert.Single(expected);
        Assert.Same(source, Assert.Single(callbacks));
        Assert.Equal(expected[0], Identity(source));
        Assert.DoesNotContain(callbacks, value => ReferenceEquals(value, document));
        AssertUnchanged(context, before);
    }

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
