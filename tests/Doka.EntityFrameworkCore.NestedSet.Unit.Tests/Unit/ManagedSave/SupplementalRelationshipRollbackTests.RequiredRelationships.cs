using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SupplementalRelationshipRollbackTests
{
    /// <summary>Required-FK recovery preserves actual bucket preimages independently of scalar/null flags.</summary>
    /// <param name="initial">The ordinary EF operations that prepare the captured relationship.</param>
    /// <param name="callback">The ordinary EF operations applied after capture.</param>
    [Theory]
    [InlineData("attached", "reference-null")]
    [InlineData("attached", "scalar-roundtrip")]
    [InlineData("attached", "unmarked-roundtrip")]
    [InlineData("reference-null", "attached")]
    [InlineData("reference-null", "raw-reset")]
    [InlineData("scalar-null", "scalar-attached")]
    [InlineData("scalar-roundtrip", "attached")]
    [InlineData("unmarked-roundtrip", "attached")]
    public async Task Restore_RequiredConceptualNull_RestoresOriginalNativeRelationship(
        string initial,
        string callback
    )
    {
        // Arrange
        await using var context = new RequiredContext();
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        var owner = new Owner
        {
            Id = 2,
            Nodes = [],
        };

        var node = new Node
        {
            Id = 10,
            OwnerId = owner.Id,
            Owner = owner,
        };

        owner.Nodes.Add(node);
        context.Attach(owner);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var update = updates.Entries.Single(entry => ReferenceEquals(
            entry.ToEntityEntry()
                .Entity,
            node));

        var principal = updates.Entries.Single(entry => ReferenceEquals(
            entry.ToEntityEntry()
                .Entity,
            owner));

        var reference = update.EntityType.FindNavigation(nameof(Node.Owner))!;
        var property = update.EntityType.FindProperty(nameof(Node.OwnerId))!;

        // WHY: EF's public read initializes its lazy map before the tested initial changes. Otherwise the
        // first later lookup would rebuild membership from current values and conceal these distinct states.
        var initialBucket = updates.GetDependents(principal, reference.ForeignKey);
        var initiallyInstalled = initialBucket.ToArray();
        PrepareRequiredRelationship(context, node, owner, initial);
        var originalNull = update.IsConceptualNull(property);
        var originalModified = update.IsModified(property);
        var originalState = update.EntityState;
        var originalReference = node.Owner;
        var originalSnapshot = update.GetRelationshipSnapshotValue(reference);
        var originalMembers = owner.Nodes!.ToArray();
        var originalNativeMembers = Assert
            .IsAssignableFrom<ISet<object>>(
                principal.GetRelationshipSnapshotValue(reference.Inverse!))
            .ToArray();

        var originalDependents = updates
            .GetDependents(principal, reference.ForeignKey)
            .ToArray();

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        PrepareRequiredRelationship(context, node, owner, callback);
        var callbackNull = update.IsConceptualNull(property);

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.IsType<HashSet<IUpdateEntry>>(initialBucket);
        Assert.Same(update, Assert.Single(initiallyInstalled));
        Assert.Equal(initial is "reference-null" or "scalar-null", originalNull);
        Assert.Equal(initial is "attached" or "scalar-null", originalDependents.Length == 1);
        Assert.Equal(callback == "reference-null", callbackNull);
        Assert.Equal(originalNull, update.IsConceptualNull(property));
        Assert.Equal(originalModified, update.IsModified(property));
        Assert.Equal(owner.Id, node.OwnerId);
        Assert.Same(originalReference, node.Owner);
        Assert.Equal(originalMembers, owner.Nodes!);
        Assert.Same(originalSnapshot, update.GetRelationshipSnapshotValue(reference));
        Assert.Equal(originalDependents, updates.GetDependents(principal, reference.ForeignKey));
        var nativeMembers = Assert.IsAssignableFrom<ISet<object>>(
            principal.GetRelationshipSnapshotValue(reference.Inverse!));

        Assert.True(nativeMembers.SetEquals(originalNativeMembers));
        Assert.Equal(originalState, update.EntityState);
        Assert.Empty(
            NestedSetInsertionContract.Instance.GetOrCreateDependents(
                NestedSetInsertionContract.Instance.Manager(update),
                [3],
                reference.ForeignKey));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>
    ///     Uses public EF operations to create distinct required-FK states without fabricating native maps.
    /// </summary>
    private static void PrepareRequiredRelationship(
        SupplementalContext context,
        Node node,
        Owner owner,
        string state
    )
    {
        var entry = context.Entry(node);

        switch (state)
        {
            case "attached":
                entry.Reference(value => value.Owner)
                    .CurrentValue = owner;

                break;
            case "reference-null":
                entry.Reference(value => value.Owner)
                    .CurrentValue = null;

                break;
            case "scalar-attached":
                entry.Property(value => value.OwnerId)
                    .CurrentValue = owner.Id;

                break;
            case "scalar-null":
                entry.Property(value => value.OwnerId)
                    .CurrentValue = null;

                break;
            case "raw-reset":
                entry.Property(value => value.OwnerId)
                    .CurrentValue = 3;

                node.OwnerId = owner.Id;
                break;
            case "scalar-roundtrip":
            case "unmarked-roundtrip":
                entry.Reference(value => value.Owner)
                    .CurrentValue = null;

                entry.Property(value => value.OwnerId)
                    .CurrentValue = owner.Id;

                if (state == "unmarked-roundtrip")
                {
                    entry.Property(value => value.OwnerId)
                        .IsModified = false;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }
}
