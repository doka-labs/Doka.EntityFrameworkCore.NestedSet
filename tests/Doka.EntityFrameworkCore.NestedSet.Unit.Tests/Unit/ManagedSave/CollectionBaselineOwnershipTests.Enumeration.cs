using System.Collections;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CollectionBaselineOwnershipTests
{
    /// <summary>Completes each original source once, including when no external relationship needs recording.</summary>
    /// <param name="withRelationship">Whether the saved wave also references an existing external owner.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedRelationshipsAcceptAOneShotSource(
        bool withRelationship
    )
    {
        // Arrange
        await using var context = new BaselineContext();
        var wave = PrepareSavedWave(context, withRelationship);
        var source = new SinglePassIdentities(wave.Identities);

        // Act
        wave.Writes.RecordSavedRelationships(source, wave.Entries, wave.Entities);
        DetachWave(wave.Identities);
        wave.Writes.Restore();

        // Assert
        Assert.Equal(1, source.Enumerations);
        Assert.Equal(wave.Identities.Length, source.Visits);
        Assert.Same(wave.ExistingMember, Assert.Single(wave.ExistingOwner.Dependents));
        Assert.All(wave.Identities, identity => Assert.Equal(EntityState.Detached, identity.UpdateEntry.EntityState));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Preserves external-owner rollback for array, list, value-view and lazy caller shapes.</summary>
    /// <param name="shape">The runtime collection representation supplied to completion.</param>
    [Theory]
    [InlineData("array")]
    [InlineData("list")]
    [InlineData("values")]
    [InlineData("projection")]
    [InlineData("flattened")]
    public async Task SavedRelationshipSourceShapesPreserveExternalOwner(
        string shape
    )
    {
        // Arrange
        await using var context = new BaselineContext();
        var wave = PrepareSavedWave(context, withRelationship: true);
        var originalCollection = wave.ExistingOwner.Dependents;
        var source = CompletionSource(wave.Identities, shape);
        var externalEntry = context.Entry(wave.ExistingOwner);
        var nativeOwner = NestedSetInsertionContract.Instance.Entry(externalEntry);
        var navigation = externalEntry
            .Metadata
            .GetNavigations()
            .Single();

        // Act
        wave.Writes.RecordSavedRelationships(source, wave.Entries, wave.Entities);
        DetachWave(wave.Identities);
        wave.Writes.Restore();

        // Assert
        Assert.Same(originalCollection, wave.ExistingOwner.Dependents);
        Assert.Same(wave.ExistingMember, Assert.Single(wave.ExistingOwner.Dependents));

        var members =
            Assert.IsType<ISet<object>>(nativeOwner.GetRelationshipSnapshotValue(navigation), exactMatch: false);

        Assert.Same(wave.ExistingMember, Assert.Single(members));
        Assert.Equal(EntityState.Unchanged, externalEntry.State);
        Assert.All(wave.Identities, identity => Assert.Equal(EntityState.Detached, identity.UpdateEntry.EntityState));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Completes no-op navigation receipts before the no-relationship return releases a saved wave.</summary>
    [Fact]
    public async Task NoRelationshipReturnStillCompletesInputNavigationReceipts()
    {
        // Arrange
        await using var context = new BaselineContext();
        var wave = PrepareSavedWave(context, withRelationship: false);
        var original = wave.InputOwner.Dependents;
        var laterMember = new ExternalDependent
        {
            Id = 91,
            ExternalOwnerId = wave.InputOwner.Id,
        };

        // Act
        wave.Writes.RecordSavedRelationships(wave.Identities, wave.Entries, wave.Entities);
        DetachWave(wave.Identities);

        // WHY: Completion must retire an unchanged input container receipt. A subsequent application edit
        // does not become library-owned cleanup merely because an earlier wave had no external FK to record.
        original.Add(laterMember);
        wave.Writes.Restore();

        // Assert
        Assert.All(wave.Identities, identity => Assert.False(identity.HasCollectionRelationships));
        Assert.Same(original, wave.InputOwner.Dependents);
        Assert.Same(laterMember, Assert.Single(original));
        Assert.Empty(RetainedOwnerEntries(wave.Writes));
        Assert.Equal(EntityState.Detached,
            context.Entry(laterMember).State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Never treats a principal from this same wave as an external rollback owner.</summary>
    /// <param name="principalFirst">Whether the principal precedes its dependent in the original source.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntroducedPrincipalIsExcludedInEitherSourceOrder(
        bool principalFirst
    )
    {
        // Arrange
        await using var context = new BaselineContext();
        var owner = new ExternalOwner { Id = 7 };
        var dependent = new ExternalDependent
        {
            Id = 21,
            ExternalOwnerId = owner.Id,
        };

        var ownerEntry = context.Entry(owner);
        var dependentEntry = context.Entry(dependent);
        var writes = new NestedSetCallbackWrites();
        writes.CaptureRelationships(ownerEntry);
        ownerEntry.State = EntityState.Added;
        writes.CaptureRelationships(dependentEntry);
        dependentEntry.State = EntityState.Added;
        var ownerIdentity = NestedSetInsertionTracking.Capture(ownerEntry);
        var dependentIdentity = NestedSetInsertionTracking.Capture(dependentEntry);
        NestedSetInsertionTracking.Identity[] identities = principalFirst
            ? [ownerIdentity, dependentIdentity]
            : [dependentIdentity, ownerIdentity];

        var introducedEntries = identities
            .Select(identity => identity.UpdateEntry)
            .ToHashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);

        var introducedEntities = new HashSet<object>([owner, dependent], ReferenceEqualityComparer.Instance);
        var source = new SinglePassIdentities(identities);
        context.ChangeTracker.AcceptAllChanges();

        // Act
        writes.RecordSavedRelationships(source, introducedEntries, introducedEntities);
        var retainedOwners = RetainedOwnerEntries(writes);
        dependentIdentity.PrepareDetach();
        ownerIdentity.PrepareDetach();
        dependentIdentity.Detach();
        ownerIdentity.Detach();
        writes.Restore();

        // Assert
        Assert.Equal(1, source.Enumerations);
        Assert.Equal(2, source.Visits);
        Assert.Empty(retainedOwners);
        Assert.Equal(EntityState.Detached, ownerEntry.State);
        Assert.Equal(EntityState.Detached, dependentEntry.State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Prepares a completed wave with a no-FK input principal and an optional external dependent.</summary>
    private static CompletionWave PrepareSavedWave(
        BaselineContext context,
        bool withRelationship
    )
    {
        var existingOwner = new ExternalOwner { Id = 7 };
        var existingMember = new ExternalDependent
        {
            Id = 11,
            ExternalOwnerId = existingOwner.Id,
        };

        existingOwner.Dependents.Add(existingMember);
        context.Attach(existingOwner);
        var inputOwner = new ExternalOwner { Id = 8 };
        var ownerEntry = context.Entry(inputOwner);
        var writes = new NestedSetCallbackWrites();
        writes.CaptureRelationships(ownerEntry);
        ownerEntry.State = EntityState.Added;
        var identities = new List<NestedSetInsertionTracking.Identity>
        {
            NestedSetInsertionTracking.Capture(ownerEntry),
        };

        if (withRelationship)
        {
            var dependent = new ExternalDependent
            {
                Id = 21,
                ExternalOwnerId = existingOwner.Id,
            };

            var entry = context.Entry(dependent);
            writes.CaptureRelationships(entry);
            entry.State = EntityState.Added;
            identities.Add(NestedSetInsertionTracking.Capture(entry));
        }

        var captured = identities.ToArray();
        var entries = captured
            .Select(identity => identity.UpdateEntry)
            .ToHashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);

        var entities = captured
            .Select(identity => identity.UpdateEntry.ToEntityEntry().Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        context.ChangeTracker.AcceptAllChanges();

        return new CompletionWave(
            writes,
            captured,
            entries,
            entities,
            inputOwner,
            existingOwner,
            existingMember);
    }

    /// <summary>Builds the same stable input bag using the runtime representations of production callers.</summary>
    private static IEnumerable<NestedSetInsertionTracking.Identity> CompletionSource(
        NestedSetInsertionTracking.Identity[] identities,
        string shape
    ) => shape switch
    {
        "array" => identities,
        "list" => identities.ToList(),
        "values" => identities
            .Select((identity, index) => (identity, index))
            .ToDictionary(item => item.index, item => item.identity)
            .Values,
        "projection" => identities
            .Where(static identity => identity is not null)
            .Select(static identity => identity),
        "flattened" => new[] { identities }.SelectMany(static batch => batch),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Releases exact introduced identities in dependency-safe reverse order.</summary>
    private static void DetachWave(
        NestedSetInsertionTracking.Identity[] identities
    )
    {
        foreach (var identity in identities)
        {
            identity.PrepareDetach();
        }

        // WHY: Later dependents retire before their preceding input principals, as in insertion cleanup.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = identities.Length - 1; index >= 0; index--)
        {
            identities[index].Detach();
        }
    }

    /// <summary>Observes retained owner authorities without invoking the production repair logic.</summary>
    private static IUpdateEntry[] RetainedOwnerEntries(
        NestedSetCallbackWrites writes
    )
    {
        var journal = typeof(NestedSetCallbackWrites).GetField(
            "_relationships",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(writes);

        if (journal is null)
        {
            return [];
        }

        // WHY: The ownership invariant is about retained repair authority, not merely today's CLR contents.
        // Inspect our own journal rather than adding a production API whose only consumer would be this test.
        var originals =
            typeof(NestedSetCollectionBaseline).GetField("_originals", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(journal)!;

        var keys = (IEnumerable)originals
            .GetType()
            .GetProperty("Keys")!.GetValue(originals)!;

        return keys
            .Cast<(IUpdateEntry Entry, INavigation Navigation)>()
            .Select(key => key.Entry)
            .ToArray();
    }

    /// <summary>Retains setup data outside each test's single completion and recovery action.</summary>
    private sealed record CompletionWave(
        NestedSetCallbackWrites Writes,
        NestedSetInsertionTracking.Identity[] Identities,
        IReadOnlySet<IUpdateEntry> Entries,
        IReadOnlySet<object> Entities,
        ExternalOwner InputOwner,
        ExternalOwner ExistingOwner,
        ExternalDependent ExistingMember
    );

    /// <summary>Rejects a second traversal rather than silently replaying a projected source.</summary>
    private sealed class SinglePassIdentities(
        NestedSetInsertionTracking.Identity[] identities
    ) : IEnumerable<NestedSetInsertionTracking.Identity>
    {
        /// <summary>Gets how often completion requested the original sequence.</summary>
        internal int Enumerations { get; private set; }

        /// <summary>Gets how many exact original identities completion visited.</summary>
        internal int Visits { get; private set; }

        /// <inheritdoc />
        public IEnumerator<NestedSetInsertionTracking.Identity> GetEnumerator()
        {
            Enumerations++;

            return Enumerations != 1
                ? throw new InvalidOperationException("The saved identity source was enumerated more than once.")
                : Enumerate()
                    .GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Counts actual elements independently of the caller's retained native authority set.</summary>
        private IEnumerable<NestedSetInsertionTracking.Identity> Enumerate()
        {
            foreach (var identity in identities)
            {
                Visits++;

                yield return identity;
            }
        }
    }
}
