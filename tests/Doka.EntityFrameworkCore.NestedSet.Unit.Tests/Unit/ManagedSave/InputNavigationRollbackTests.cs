using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects sparse input collection receipts and the validated owned-graph cleanup boundary.</summary>
public sealed partial class InputNavigationRollbackTests
{
    /// <summary>Completed detached waves retain only the original null or replaced collection storage.</summary>
    /// <param name="initiallyNull">Whether the input collection starts literally null instead of empty.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedWaveRollbackRestoresReplacedInputCollection(
        bool initiallyNull
    )
    {
        // Arrange
        await using var context = new RollbackContext();
        var original = initiallyNull
            ? null
            : new List<RollbackNode>();

        var input = new RollbackNode
        {
            Id = 1,
            Children = original,
        };

        var entry = context.Entry(input);
        var replacement = new List<RollbackNode>();
        var writes = new NestedSetCallbackWrites();
        var introducedEntries = new HashSet<IUpdateEntry>(
            [NestedSetInsertionContract.Instance.Entry(entry)],
            ReferenceEqualityComparer.Instance);

        var introducedEntities = new HashSet<object>([input], ReferenceEqualityComparer.Instance);

        // Act
        writes.CaptureRelationships(entry);
        entry.State = EntityState.Added;
        var identity = NestedSetInsertionTracking.Capture(entry);

        // WHY: AcceptAllChanges performs the same public EF state transition as a successful native save;
        // this unit isolates the completed-wave journal without introducing unrelated database work.
        context.ChangeTracker.AcceptAllChanges();
        input.Children = replacement;
        writes.RecordSavedRelationships([identity], introducedEntries, introducedEntities);
        identity.PrepareDetach();
        identity.Detach();
        var completedCollection = input.Children;
        var completedState = entry.State;
        writes.Restore();

        // Assert
        Assert.Same(replacement, completedCollection);
        Assert.Equal(EntityState.Detached, completedState);
        Assert.Same(original, input.Children);

        if (original is not null)
        {
            Assert.Empty(original);
        }

        Assert.Empty(replacement);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Empty(
            context
                .GetService<IUpdateAdapterFactory>()
                .Create()
                .Entries);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Only fully validated owned graphs may have their initially empty ordinary links reset.</summary>
    /// <param name="populatedForeignReference">Whether the owned payload starts with an invalid foreign link.</param>
    /// <param name="changePayload">Whether the caller changes mapped application data before rollback.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OwnedGraphRollbackRespectsValidationBoundary(
        bool populatedForeignReference,
        bool changePayload
    )
    {
        // Arrange
        await using var context = new RollbackContext();
        var existingOwner = new RollbackOwner { Id = 7 };
        context.Attach(existingOwner);
        var originalForeignOwner = populatedForeignReference
            ? new RollbackOwner { Id = 9 }
            : null;

        var payload = new RollbackPayload
        {
            OwnerId = originalForeignOwner?.Id ?? existingOwner.Id,
            Owner = originalForeignOwner,
        };

        var input = new RollbackNode
        {
            Id = 1,
            Payload = payload,
        };

        payload.Root = input;
        var originalOwnerId = payload.OwnerId;
        var payloadProperty = context.Model.FindEntityType(typeof(RollbackPayload))!
            .FindProperty(nameof(RollbackPayload.Value));

        var store = new NestedSetStore<RollbackNode, int, Guid, NestedSetNoScope>(
            context,
            context.Model.FindEntityType(typeof(RollbackNode))!,
            default,
            Guid.Empty);

        var plan = new NestedSetBulkPlan<RollbackNode, int, Guid, NestedSetNoScope>(
            store,
            [new NestedSetBranch<RollbackNode>(input)],
            new HashSet<RollbackNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        var destination =
            new NestedSetPlacementResolver<RollbackNode, int, Guid, NestedSetNoScope>.Destination(1, default, 0, 0);

        plan.PrepareGeometry(destination, null, CancellationToken.None);
        var writes = new NestedSetCallbackWrites();

        // Act
        var error = Record.Exception(() => plan.StageBatch(destination, 0, 1, writes, CancellationToken.None));
        var ownerAfterStage = payload.Owner;

        if (changePayload)
        {
            // WHY: Ordinary application edits remain caller-owned even if graph validation rejected staging.
            payload.Value = "Application edit";
        }

        plan.Restore();
        writes.Restore();

        // Assert
        if (populatedForeignReference)
        {
            Assert.Equal(
                NestedSetErrorCode.InvalidImport,
                Assert.IsType<NestedSetException>(error)
                    .Code);

            Assert.Same(originalForeignOwner, ownerAfterStage);
        }
        else
        {
            Assert.Null(error);
            Assert.Same(existingOwner, ownerAfterStage);
        }

        Assert.Same(payload, input.Payload);
        Assert.Same(input, payload.Root);
        Assert.Same(originalForeignOwner, payload.Owner);
        Assert.Equal(originalOwnerId, payload.OwnerId);
        Assert.NotNull(payloadProperty);
        Assert.False(payloadProperty.IsShadowProperty());
        Assert.NotNull(payloadProperty.PropertyInfo!.SetMethod);
        Assert.Equal(changePayload ? "Application edit" : "Caller payload", payload.Value);
        Assert.Equal(
            EntityState.Detached,
            context.Entry(input)
                .State);

        Assert.Equal(
            EntityState.Detached,
            context.Entry(payload)
                .State);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(existingOwner)
                .State);

        if (originalForeignOwner is not null)
        {
            Assert.Equal(
                EntityState.Detached,
                context.Entry(originalForeignOwner)
                    .State);
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
