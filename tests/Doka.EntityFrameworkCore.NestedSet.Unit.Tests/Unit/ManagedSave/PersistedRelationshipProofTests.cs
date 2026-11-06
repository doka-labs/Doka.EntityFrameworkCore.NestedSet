namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Requires ordinary-FK write and readback evidence before accepting collection membership.</summary>
public sealed partial class PersistedRelationshipProofTests
{
    /// <summary>A current FK flag proves ordinary updates but not original-only stored-procedure inputs.</summary>
    /// <param name="originalOnlyProcedure">Whether the valid procedure omits the current owner-FK value.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleMemberFkProofRequiresCurrentValueWrite(
        bool originalOnlyProcedure
    )
    {
        // Arrange
        await using ProofContext context = originalOnlyProcedure
            ? new OriginalOnlyUpdateContext()
            : new NormalUpdateContext();

        var relationalModel = context.Model.GetRelationalModel();
        var originalOwner = new ProofOwner { Id = 7 };
        var selectedOwner = new ProofOwner { Id = 8 };
        context.AttachRange(originalOwner, selectedOwner);
        var node = new ProofNode
        {
            Id = 1,
            OwnerId = originalOwner.Id,
            Left = 1,
            Right = 2,
        };

        var entry = context.Attach(node);
        var ownerProperty = entry.Property(value => value.OwnerId);
        var key = ownerProperty
            .Metadata
            .GetContainingForeignKeys()
            .Single();

        var navigation = key.PrincipalToDependent!;
        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var selectedUpdate = NestedSetInsertionContract.Instance.Entry(context.Entry(selectedOwner));
        selectedOwner.Nodes.Add(node);
        node.OwnerId = selectedOwner.Id;
        ownerProperty.IsModified = true;
        var snapshotContainedBefore =
            (selectedUpdate.GetRelationshipSnapshotValue(navigation) as ISet<object>)?.Contains(node) == true;

        // Act
        var completion = NestedSetCollectionBaseline.CapturePersisted([update]);
        completion?.Invoke();

        // Assert
        Assert.NotEmpty(relationalModel.Tables);
        Assert.Single(key.Properties);
        Assert.Equal(EntityState.Modified, update.EntityState);
        Assert.True(ownerProperty.IsModified);
        Assert.False(update.IsStoreGenerated(ownerProperty.Metadata));
        Assert.False(snapshotContainedBefore);
        var snapshot = selectedUpdate.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? [];

        if (originalOnlyProcedure)
        {
            Assert.True(
                Assert.Single(ownerProperty.Metadata.GetUpdateStoredProcedureParameterMappings())
                    .Parameter.ForOriginalValue);

            Assert.Null(completion);
            Assert.DoesNotContain(node, snapshot);
        }
        else
        {
            Assert.Empty(entry.Metadata.GetUpdateStoredProcedureMappings());
            Assert.NotNull(completion);
            Assert.Contains(node, snapshot);
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A complete procedure FK result is trusted only after the provider actually returns it.</summary>
    /// <param name="returned">Whether the provider installs the mapped FK result sidecar before completion.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcedureFkProofRequiresActualCompleteReadback(
        bool returned
    )
    {
        // Arrange
        await using var context = new ReturnedOwnerProcedureContext();
        var relationalModel = context.Model.GetRelationalModel();
        var originalOwner = new ProofOwner { Id = 7 };
        var selectedOwner = new ProofOwner { Id = 8 };
        context.AttachRange(originalOwner, selectedOwner);
        var node = new ProofNode
        {
            Id = 1,
            OwnerId = originalOwner.Id,
            Left = 1,
            Right = 2,
        };

        var entry = context.Attach(node);
        var ownerProperty = entry.Property(value => value.OwnerId);
        var key = ownerProperty
            .Metadata
            .GetContainingForeignKeys()
            .Single();

        var navigation = key.PrincipalToDependent!;
        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var selectedUpdate = NestedSetInsertionContract.Instance.Entry(context.Entry(selectedOwner));
        selectedOwner.Nodes.Add(node);
        node.OwnerId = selectedOwner.Id;
        ownerProperty.IsModified = true;
        var generatedBefore = update.IsStoreGenerated(ownerProperty.Metadata);
        var returnedBefore = update.HasStoreGeneratedValue(ownerProperty.Metadata);

        // Act
        var completion = NestedSetCollectionBaseline.CapturePersisted([update]);

        if (returned)
        {
            // WHY: The raw current value is already 8 in both rows. Only the public provider-side result
            // assignment proves that the procedure actually returned that value for the complete FK.
            update.SetStoreGeneratedValue(ownerProperty.Metadata, selectedOwner.Id, setModified: false);
        }

        var returnedAtCompletion = update.HasStoreGeneratedValue(ownerProperty.Metadata);
        var snapshotBeforeCompletion =
            (selectedUpdate.GetRelationshipSnapshotValue(navigation) as ISet<object>)?.Contains(node) == true;

        completion?.Invoke();

        // Assert
        Assert.NotEmpty(relationalModel.Tables);
        Assert.Single(key.Properties);
        Assert.Single(ownerProperty.Metadata.GetUpdateStoredProcedureResultColumnMappings());
        Assert.True(
            Assert.Single(ownerProperty.Metadata.GetUpdateStoredProcedureParameterMappings())
                .Parameter.ForOriginalValue);

        Assert.Equal(PropertySaveBehavior.Ignore, ownerProperty.Metadata.GetAfterSaveBehavior());
        Assert.True(generatedBefore);
        Assert.False(returnedBefore);
        Assert.Equal(returned, returnedAtCompletion);
        Assert.Equal(selectedOwner.Id, update.GetCurrentValue<int?>(ownerProperty.Metadata));
        Assert.False(snapshotBeforeCompletion);
        Assert.NotNull(completion);
        var snapshot = selectedUpdate.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? [];

        if (returned)
        {
            Assert.Contains(node, snapshot);
        }
        else
        {
            Assert.DoesNotContain(node, snapshot);
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Only a written temporary FK may await its principal's authoritative provider-generated key.</summary>
    /// <param name="written">Whether the actual provider write set marks the ordinary FK as modified.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryFkProofRequiresWriteAndPropagatedReadback(
        bool written
    )
    {
        // Arrange
        await using var context = new GeneratedOwnerContext();
        var originalOwner = new ProofOwner { Id = 7 };
        context.Attach(originalOwner);
        var node = new ProofNode
        {
            Id = 1,
            OwnerId = originalOwner.Id,
            Left = 1,
            Right = 2,
        };

        var entry = context.Attach(node);
        node.Name = "After";
        entry.Property(value => value.Name)
            .IsModified = true;

        var generatedOwner = new ProofOwner();
        var generatedEntry = await context.AddAsync(generatedOwner, CancellationToken.None);
        generatedOwner.Nodes.Add(node);
        entry.Reference(value => value.Owner)
            .CurrentValue = generatedOwner;

        var ownerProperty = entry.Property(value => value.OwnerId);
        ownerProperty.IsModified = written;
        ownerProperty.IsTemporary = true;

        if (!generatedOwner.Nodes.Contains(node))
        {
            // WHY: Clearing IsModified can legitimately restore the stored FK and remove inverse membership.
            // A prepared CLR alias may still exist afterward; temporary metadata must not authorize saving it.
            generatedOwner.Nodes.Add(node);
        }

        var update = NestedSetInsertionContract.Instance.Entry(entry);
        var generatedUpdate = NestedSetInsertionContract.Instance.Entry(generatedEntry);
        var generatedKey = generatedEntry
            .Property(value => value.Id)
            .Metadata;

        var key = ownerProperty
            .Metadata
            .GetContainingForeignKeys()
            .Single();

        var navigation = key.PrincipalToDependent!;
        var temporaryBefore = update.HasTemporaryValue(ownerProperty.Metadata);
        var modifiedBefore = update.IsModified(ownerProperty.Metadata);
        var generatedBefore = update.IsStoreGenerated(ownerProperty.Metadata);
        var generatedSidecarBefore = update.HasStoreGeneratedValue(ownerProperty.Metadata);
        var snapshotContainedBefore =
            (generatedUpdate.GetRelationshipSnapshotValue(navigation) as ISet<object>)?.Contains(node) == true;

        // Act
        var completion = NestedSetCollectionBaseline.CapturePersisted([generatedUpdate, update]);

        // WHY: The public provider interface changes the principal's real generated-key sidecar. EF's own
        // native fixup must propagate that value; the test never assigns a generated value to the FK itself.
        generatedUpdate.SetStoreGeneratedValue(generatedKey, 42, setModified: false);
        var propagated = update.HasStoreGeneratedValue(ownerProperty.Metadata);
        var ownerValueBeforeCompletion = update.GetCurrentValue<int?>(ownerProperty.Metadata);
        var snapshotBeforeCompletion =
            (generatedUpdate.GetRelationshipSnapshotValue(navigation) as ISet<object>)?.Contains(node) == true;

        completion?.Invoke();

        // Assert
        Assert.Equal(ValueGenerated.Never, ownerProperty.Metadata.ValueGenerated);
        Assert.Equal(written, modifiedBefore);
        Assert.True(temporaryBefore);
        Assert.False(generatedBefore);
        Assert.False(generatedSidecarBefore);
        Assert.False(snapshotContainedBefore);
        Assert.False(snapshotBeforeCompletion);
        Assert.Equal(42, generatedUpdate.GetCurrentValue<int>(generatedKey));
        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);

        var snapshot = generatedUpdate.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? [];

        if (written)
        {
            Assert.True(propagated);
            Assert.Equal(42, ownerValueBeforeCompletion);
            Assert.NotNull(completion);
            Assert.Contains(node, snapshot);
        }
        else
        {
            Assert.Null(completion);
            Assert.DoesNotContain(node, snapshot);
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
